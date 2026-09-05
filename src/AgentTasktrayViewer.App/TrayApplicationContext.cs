using AgentTasktrayViewer.Core;

namespace AgentTasktrayViewer;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly TimeSpan InactivityThreshold = TimeSpan.FromMinutes(15);
    private readonly SettingsManager _settingsManager;
    private readonly AppSettings _settings;
    private readonly CodexExecutableLocator _codexExecutableLocator = new();
    private readonly CodexUsageProvider _codexProvider;
    private readonly ClaudeUsageProvider _claudeProvider;
    private readonly ClaudeCliLocator _claudeCliLocator = new();
    private readonly ClaudeIntegrationManager _claudeIntegration = new();
    private readonly StartupManager _startupManager = new();
    private readonly CodexActivityDetector _activityDetector = new();
    private readonly NamedPipeClaudeUsageServer _ipcServer = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private readonly System.Windows.Forms.Timer _updateTimer;
    private readonly System.Windows.Forms.Timer _activityTimer;
    private readonly TrayIconManager _iconManager;
    private readonly SynchronizationContext _uiContext;
    private readonly ToolStripMenuItem _pauseMenuItem;
    private readonly ToolStripMenuItem _startWithWindowsMenuItem;
    private readonly IReadOnlyList<string> _availableIconFontFamilies;
    private readonly bool _firstRun;
    private ToolStripMenuItem? _codexDiagnosticItem;
    private ToolStripMenuItem? _claudeDiagnosticItem;
    private ToolStripMenuItem? _codexInstallItem;
    private ToolStripMenuItem? _claudeInstallItem;
    private bool _codexDataAvailable;
    private bool _claudeDataAvailable;
    private bool _codexWarningShown;
    private bool _claudeWarningShown;
    private bool _codexDefaultHidden;
    private bool _claudeDefaultHidden;
    private SettingsForm? _settingsForm;
    private bool _wasInactive;
    private bool _isExiting;

    public TrayApplicationContext()
    {
        _settingsManager = new SettingsManager();
        _firstRun = !File.Exists(_settingsManager.SettingsPath);
        _settings = _settingsManager.Load();
        _availableIconFontFamilies = IconFactory.GetAvailableRecommendedFontFamilies();
        ApplyMissingProviderDefaults();
        _codexProvider = new CodexUsageProvider(_codexExecutableLocator);
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _claudeProvider = new ClaudeUsageProvider(_settingsManager.ApplicationDataDirectory);
        _iconManager = new TrayIconManager(_settings);
        _iconManager.SettingsIcon.Click += (_, _) => OpenSettings();
        _iconManager.VisibilityChanged += OnVisibilityChanged;

        ContextMenuStrip menu = BuildMenu(out _pauseMenuItem, out _startWithWindowsMenuItem);
        _iconManager.AttachMenu(menu);
        _iconManager.ApplyVisibility(_settings);
        _settingsManager.Save(_settings);

        _updateTimer = new System.Windows.Forms.Timer { Interval = (int)_settings.UpdateInterval.TotalMilliseconds };
        _updateTimer.Tick += (_, _) => _ = UpdateCodexIfNeededAsync();
        _updateTimer.Start();

        _activityTimer = new System.Windows.Forms.Timer { Interval = 15_000 };
        _activityTimer.Tick += (_, _) => CheckActivity();
        _activityTimer.Start();

        _ipcServer.Start(OnClaudeUsageNotificationAsync);
        _pauseMenuItem.Checked = _settings.PauseWhenInactive;
        _startWithWindowsMenuItem.Checked = _settings.StartWithWindows;
        _ = InitializeAsync();
    }

    protected override void ExitThreadCore()
    {
        if (_isExiting)
        {
            return;
        }
        _isExiting = true;
        _shutdown.Cancel();
        _updateTimer.Stop();
        _activityTimer.Stop();
        _ipcServer.Dispose();
        _iconManager.Dispose();
        _updateTimer.Dispose();
        _activityTimer.Dispose();
        _updateGate.Dispose();
        _shutdown.Dispose();
        base.ExitThreadCore();
    }

    private ContextMenuStrip BuildMenu(out ToolStripMenuItem pauseMenuItem, out ToolStripMenuItem startWithWindowsMenuItem)
    {
        var menu = new ContextMenuStrip();
        var codexFiveHourStatus = new ToolStripMenuItem { Enabled = false };
        var codexWeeklyStatus = new ToolStripMenuItem { Enabled = false };
        var claudeFiveHourStatus = new ToolStripMenuItem { Enabled = false };
        var claudeWeeklyStatus = new ToolStripMenuItem { Enabled = false };
        _iconManager.StatusTextChanged += (_, e) =>
        {
            codexFiveHourStatus.Text = e.CodexFiveHour;
            codexWeeklyStatus.Text = e.CodexWeekly;
            claudeFiveHourStatus.Text = e.ClaudeFiveHour;
            claudeWeeklyStatus.Text = e.ClaudeWeekly;
        };
        menu.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripMenuItem("Codex") { DropDownItems = { codexFiveHourStatus, codexWeeklyStatus } },
            new ToolStripMenuItem("Claude") { DropDownItems = { claudeFiveHourStatus, claudeWeeklyStatus } },
            new ToolStripSeparator()
        });

        menu.Items.Add(BuildDiagnosticsMenu());

        var visibility = new ToolStripMenuItem("表示するアイコン");
        visibility.DropDown.Closing += (_, e) =>
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
            {
                e.Cancel = true;
            }
        };
        AddVisibilityItem(visibility, "Codex 5時間", TrayIconKind.CodexFiveHour);
        AddVisibilityItem(visibility, "Codex 週間", TrayIconKind.CodexWeekly);
        AddVisibilityItem(visibility, "Claude 5時間", TrayIconKind.ClaudeFiveHour);
        AddVisibilityItem(visibility, "Claude 週間", TrayIconKind.ClaudeWeekly);
        menu.Items.Add(visibility);

        var visualStyle = new ToolStripMenuItem("アイコンスタイル");
        AddVisualStyleItem(visualStyle, "背景色版", TrayIconVisualStyle.ColoredBackground);
        AddVisualStyleItem(visualStyle, "文字色版（背景なし）", TrayIconVisualStyle.ColoredText);
        menu.Items.Add(visualStyle);

        var iconFont = new ToolStripMenuItem("アイコンフォント");
        foreach (string fontFamily in _availableIconFontFamilies)
        {
            AddFontFamilyItem(iconFont, fontFamily);
        }
        menu.Items.Add(iconFont);

        var interval = new ToolStripMenuItem("更新間隔");
        foreach (int minutes in new[] { 1, 3, 5, 10 })
        {
            var item = new ToolStripMenuItem($"{minutes}分") { Tag = minutes };
            item.Click += (_, _) => SetUpdateInterval(minutes, interval);
            interval.DropDownItems.Add(item);
        }
        menu.Items.Add(interval);

        var pauseItem = new ToolStripMenuItem("使っていない間は更新を止める")
        {
            CheckOnClick = true,
            Checked = _settings.PauseWhenInactive
        };
        pauseMenuItem = pauseItem;
        pauseItem.CheckedChanged += (_, _) =>
        {
            _settings.PauseWhenInactive = pauseItem.Checked;
            _settingsManager.Save(_settings);
        };
        menu.Items.Add(pauseItem);

        var startItem = new ToolStripMenuItem("Windows起動時に開始")
        {
            CheckOnClick = true,
            Checked = _settings.StartWithWindows
        };
        startWithWindowsMenuItem = startItem;
        startItem.Click += async (_, _) =>
        {
            bool requested = startItem.Checked;
            bool applied = await _startupManager.SetEnabledAsync(requested);
            _settings.StartWithWindows = applied && requested;
            startItem.Checked = _settings.StartWithWindows;
            _settingsManager.Save(_settings);
        };
        menu.Items.Add(startItem);
        menu.Items.Add(new ToolStripSeparator());

        var claudeIntegrationStatus = new ToolStripMenuItem { Enabled = false };
        var claudeLastReceived = new ToolStripMenuItem { Enabled = false };
        menu.Items.Add(new ToolStripMenuItem("Claude Code連携")
        {
            DropDownItems =
            {
                claudeIntegrationStatus,
                claudeLastReceived,
                new ToolStripSeparator(),
                CreateClaudeConfigureItem(),
                CreateClaudeUnconfigureItem()
            }
        });
        menu.Opening += (_, _) =>
        {
            claudeIntegrationStatus.Text = $"状態: {FormatIntegrationStatus(_claudeIntegration.GetStatus())}";
            DateTimeOffset? receivedAt = _claudeProvider.GetLastReceivedAt();
            claudeLastReceived.Text = receivedAt is null
                ? "最終受信: なし"
                : $"最終受信: {receivedAt.Value:HH:mm:ss}";
        };

        var updateNow = new ToolStripMenuItem("今すぐ更新");
        updateNow.Click += (_, _) => _ = RefreshAllAsync(true);
        menu.Items.Add(updateNow);
        menu.Items.Add(CreateBrowserItem("Codex 使用量ページを開く", "https://chatgpt.com/codex/settings/usage"));
        menu.Items.Add(CreateBrowserItem("Claude 使用量ページを開く", "https://claude.ai/settings/usage"));
        menu.Items.Add(new ToolStripSeparator());
        var settings = new ToolStripMenuItem("設定...");
        settings.Click += (_, _) => OpenSettings();
        menu.Items.Add(settings);
        var exit = new ToolStripMenuItem("終了");
        exit.Click += (_, _) => ExitThread();
        menu.Items.Add(exit);

        menu.Opening += (_, _) =>
        {
            foreach (ToolStripMenuItem item in interval.DropDownItems)
            {
                item.Checked = (int)item.Tag! == _settings.UpdateIntervalMinutes;
            }
            foreach (ToolStripMenuItem item in visualStyle.DropDownItems)
            {
                item.Checked = (TrayIconVisualStyle)item.Tag! == _settings.IconVisualStyle;
            }
            foreach (ToolStripMenuItem item in iconFont.DropDownItems)
            {
                item.Checked = string.Equals((string)item.Tag!, _settings.IconFontFamily, StringComparison.OrdinalIgnoreCase);
            }
            _iconManager.RefreshStatusText();
        };
        return menu;
    }

    private void AddVisualStyleItem(ToolStripMenuItem parent, string text, TrayIconVisualStyle visualStyle)
    {
        var item = new ToolStripMenuItem(text) { Tag = visualStyle };
        item.Click += (_, _) => SetIconAppearance(visualStyle, _settings.IconFontFamily);
        parent.DropDownItems.Add(item);
    }

    private void AddFontFamilyItem(ToolStripMenuItem parent, string fontFamily)
    {
        var item = new ToolStripMenuItem(fontFamily)
        {
            Tag = fontFamily,
            Font = new Font(fontFamily, SystemFonts.MenuFont?.Size ?? 9f)
        };
        item.Click += (_, _) => SetIconAppearance(_settings.IconVisualStyle, fontFamily);
        parent.DropDownItems.Add(item);
    }

    private void SetIconAppearance(TrayIconVisualStyle visualStyle, string fontFamily)
    {
        _settings.IconVisualStyle = visualStyle;
        _settings.IconFontFamily = fontFamily;
        _settings.Normalize();
        _settingsManager.Save(_settings);
        _iconManager.ApplyAppearance(_settings.IconVisualStyle, _settings.IconFontFamily);
    }

    private void AddVisibilityItem(ToolStripMenuItem parent, string text, TrayIconKind kind)
    {
        var item = new ToolStripMenuItem(text) { CheckOnClick = true, Checked = _iconManager.IsVisible(kind), Tag = kind };
        item.Click += (_, _) =>
        {
            if (_iconManager.TrySetVisibility(kind, item.Checked))
            {
                UpdateProviderDiagnostics(true);
            }
        };
        _iconManager.VisibilityStateChanged += (_, e) =>
        {
            if (e.Kind == kind)
            {
                item.Checked = e.IsVisible;
            }
        };
        parent.DropDownItems.Add(item);
    }

    private ToolStripMenuItem BuildDiagnosticsMenu()
    {
        var menu = new ToolStripMenuItem("接続状態");
        _codexDiagnosticItem = new ToolStripMenuItem("Codex: 確認中") { Enabled = false };
        _claudeDiagnosticItem = new ToolStripMenuItem("Claude Code: 確認中") { Enabled = false };
        _codexInstallItem = CreateBrowserItem("Codex CLIの導入案内を開く", "https://developers.openai.com/codex/");
        _claudeInstallItem = CreateBrowserItem("Claude Codeの導入案内を開く", "https://docs.anthropic.com/en/docs/claude-code/getting-started");
        _codexInstallItem.Visible = false;
        _claudeInstallItem.Visible = false;

        var refresh = new ToolStripMenuItem("再確認");
        refresh.Click += (_, _) => _ = RefreshAllAsync(true);
        menu.DropDownItems.Add(_codexDiagnosticItem);
        menu.DropDownItems.Add(_claudeDiagnosticItem);
        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add(_codexInstallItem);
        menu.DropDownItems.Add(_claudeInstallItem);
        menu.DropDownItems.Add(refresh);
        return menu;
    }

    private static ToolStripMenuItem CreateBrowserItem(string text, string url)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception) { }
        };
        return item;
    }

    private ToolStripMenuItem CreateClaudeConfigureItem()
    {
        var item = new ToolStripMenuItem("連携を設定");
        item.Click += (_, _) =>
        {
            string executablePath = Environment.ProcessPath ?? "AgentTasktrayViewer.exe";
            if (_claudeIntegration.Configure(executablePath))
            {
                _settings.ClaudeIntegrationEnabled = true;
                _settingsManager.Save(_settings);
            }
        };
        return item;
    }

    private ToolStripMenuItem CreateClaudeUnconfigureItem()
    {
        var item = new ToolStripMenuItem("連携を解除");
        item.Click += (_, _) =>
        {
            string executablePath = Environment.ProcessPath ?? "AgentTasktrayViewer.exe";
            if (_claudeIntegration.Unconfigure(executablePath))
            {
                _settings.ClaudeIntegrationEnabled = false;
                _settingsManager.Save(_settings);
            }
        };
        return item;
    }

    private static string FormatIntegrationStatus(ClaudeIntegrationStatus status) => status switch
    {
        ClaudeIntegrationStatus.ConfiguredByAgentTasktrayViewer => "連携済み",
        ClaudeIntegrationStatus.ExistingStatusLinePreserved => "既存設定を維持中",
        _ => "未設定"
    };

    private void SetUpdateInterval(int minutes, ToolStripMenuItem intervalMenu)
    {
        _settings.UpdateIntervalMinutes = minutes;
        _settingsManager.Save(_settings);
        _updateTimer.Interval = (int)_settings.UpdateInterval.TotalMilliseconds;
        foreach (ToolStripMenuItem item in intervalMenu.DropDownItems)
        {
            item.Checked = (int)item.Tag! == minutes;
        }
    }

    private void OnVisibilityChanged(object? sender, EventArgs e)
    {
        _iconManager.CopyVisibilityTo(_settings);
        _settingsManager.Save(_settings);
    }

    private async Task InitializeAsync()
    {
        await RefreshAllAsync(true);
    }

    private async Task RefreshAllAsync(bool notify)
    {
        await UpdateAllAsync();
        UpdateProviderDiagnostics(notify);
    }

    private void UpdateProviderDiagnostics(bool notify)
    {
        UpdateCodexDiagnostics(notify);
        UpdateClaudeDiagnostics(notify);
    }

    private void UpdateCodexDiagnostics(bool notify)
    {
        if (!_iconManager.IsProviderVisible(UsageProvider.Codex))
        {
            if (_firstRun && _codexDefaultHidden && _codexExecutableLocator.Find() is null)
            {
                SetDiagnostic(_codexDiagnosticItem, "Codex CLI: 未検出（既定で非表示）", _codexInstallItem, true);
                if (notify && !_codexWarningShown)
                {
                    _codexWarningShown = true;
                    _iconManager.ShowWarning(UsageProvider.Codex, "Codex CLIが見つかりません", "Codexのアイコンは非表示にしました。接続状態メニューから導入案内を開けます。");
                }
                return;
            }
            SetDiagnostic(_codexDiagnosticItem, "Codex: アイコン非表示（確認対象外）", _codexInstallItem, false);
            return;
        }

        string? executablePath = _codexExecutableLocator.Find();
        if (executablePath is null)
        {
            SetDiagnostic(_codexDiagnosticItem, "Codex CLI: 未検出", _codexInstallItem, true);
            if (notify && !_codexWarningShown)
            {
                _codexWarningShown = true;
                _iconManager.ShowWarning(UsageProvider.Codex, "Codex CLIが見つかりません", "Codexの使用量を取得できません。接続状態メニューから導入案内を開けます。");
            }
            return;
        }

        if (!_codexDataAvailable)
        {
            SetDiagnostic(_codexDiagnosticItem, "Codex CLI: 検出済み・取得失敗", _codexInstallItem, true);
            if (notify && !_codexWarningShown)
            {
                _codexWarningShown = true;
                _iconManager.ShowWarning(UsageProvider.Codex, "Codexの使用量を取得できません", "Codex CLIは見つかりましたが、使用量情報を取得できませんでした。ログイン状態やCLIの更新を確認してください。");
            }
            return;
        }

        SetDiagnostic(_codexDiagnosticItem, "Codex CLI: 検出・更新済み", _codexInstallItem, false);
    }

    private void UpdateClaudeDiagnostics(bool notify)
    {
        if (!_iconManager.IsProviderVisible(UsageProvider.Claude))
        {
            if (_firstRun && _claudeDefaultHidden && _claudeCliLocator.Find() is null)
            {
                SetDiagnostic(_claudeDiagnosticItem, "Claude Code: CLI未検出（既定で非表示）", _claudeInstallItem, true);
                if (notify && !_claudeWarningShown)
                {
                    _claudeWarningShown = true;
                    _iconManager.ShowWarning(UsageProvider.Claude, "Claude Codeが見つかりません", "Claude Codeのアイコンは非表示にしました。接続状態メニューから導入案内を開けます。");
                }
                return;
            }
            SetDiagnostic(_claudeDiagnosticItem, "Claude Code: アイコン非表示（確認対象外）", _claudeInstallItem, false);
            return;
        }

        string? executablePath = _claudeCliLocator.Find();
        ClaudeIntegrationStatus integrationStatus = _claudeIntegration.GetStatus();
        if (executablePath is null && integrationStatus == ClaudeIntegrationStatus.NotConfigured)
        {
            SetDiagnostic(_claudeDiagnosticItem, "Claude Code: CLI未検出・未連携", _claudeInstallItem, true);
            if (notify && !_claudeWarningShown)
            {
                _claudeWarningShown = true;
                _iconManager.ShowWarning(UsageProvider.Claude, "Claude Codeが見つかりません", "Claude Codeを導入し、接続状態メニューから導入案内または連携設定を確認してください。");
            }
            return;
        }

        if (integrationStatus == ClaudeIntegrationStatus.NotConfigured)
        {
            SetDiagnostic(_claudeDiagnosticItem, "Claude Code: 連携未設定", _claudeInstallItem, true);
            if (notify && !_claudeWarningShown)
            {
                _claudeWarningShown = true;
                _iconManager.ShowWarning(UsageProvider.Claude, "Claude Code連携が未設定です", "Claude Code連携メニューからstatusLine連携を設定してください。");
            }
            return;
        }

        if (!_claudeDataAvailable)
        {
            SetDiagnostic(_claudeDiagnosticItem, "Claude Code: データ未受信", _claudeInstallItem, true);
            if (notify && !_claudeWarningShown)
            {
                _claudeWarningShown = true;
                _iconManager.ShowWarning(UsageProvider.Claude, "Claude Codeからデータを受信していません", "Claude Codeを起動してstatusLine連携を確認してください。");
            }
            return;
        }

        SetDiagnostic(_claudeDiagnosticItem, "Claude Code: 連携・更新済み", _claudeInstallItem, false);
    }

    private static void SetDiagnostic(ToolStripMenuItem? statusItem, string text, ToolStripMenuItem? installItem, bool showInstallItem)
    {
        if (statusItem is not null)
        {
            statusItem.Text = text;
        }
        if (installItem is not null)
        {
            installItem.Visible = showInstallItem;
        }
    }

    private void ApplyMissingProviderDefaults()
    {
        if (!_firstRun)
        {
            return;
        }

        if (_codexExecutableLocator.Find() is null)
        {
            _settings.ShowCodexFiveHour = false;
            _settings.ShowCodexWeekly = false;
            _codexDefaultHidden = true;
        }

        if (_claudeCliLocator.Find() is null)
        {
            _settings.ShowClaudeFiveHour = false;
            _settings.ShowClaudeWeekly = false;
            _claudeDefaultHidden = true;
        }
    }

    private void OpenSettings()
    {
        if (_settingsForm is not null && !_settingsForm.IsDisposed)
        {
            _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(
            kind => _iconManager.IsVisible(kind),
            (kind, isVisible) =>
            {
                bool changed = _iconManager.TrySetVisibility(kind, isVisible);
                if (changed)
                {
                    UpdateProviderDiagnostics(true);
                }
                return changed;
            },
            () => _ = RefreshAllAsync(true),
            _settings.IconVisualStyle,
            _settings.IconFontFamily,
            _availableIconFontFamilies,
            visualStyle => SetIconAppearance(visualStyle, _settings.IconFontFamily),
            fontFamily => SetIconAppearance(_settings.IconVisualStyle, fontFamily));
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
    }

    private async Task UpdateAllAsync()
    {
        if (!await _updateGate.WaitAsync(0, _shutdown.Token))
        {
            return;
        }
        try
        {
            IReadOnlyList<UsageWindow> codex = await _codexProvider.GetUsageAsync(_shutdown.Token);
            _codexDataAvailable = codex.Any(window => window.IsAvailable);
            ApplyWindows(codex);
            IReadOnlyList<UsageWindow> claude = _claudeProvider.LoadCachedUsage();
            _claudeDataAvailable = claude.Any(window => window.IsAvailable);
            ApplyWindows(claude);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            _updateGate.Release();
        }
    }

    private async Task UpdateCodexIfNeededAsync()
    {
        ActivityStatus status = _activityDetector.GetStatus(InactivityThreshold);
        if (_settings.PauseWhenInactive && status == ActivityStatus.Inactive)
        {
            _wasInactive = true;
            return;
        }
        await UpdateAllAsync();
    }

    private void CheckActivity()
    {
        ActivityStatus status = _activityDetector.GetStatus(InactivityThreshold);
        if (status == ActivityStatus.Inactive)
        {
            _wasInactive = true;
        }
        else if (_wasInactive && status == ActivityStatus.Active)
        {
            _wasInactive = false;
            _ = UpdateCodexIfNeededAsync();
        }
    }

    private async Task OnClaudeUsageNotificationAsync()
    {
        if (_isExiting)
        {
            return;
        }
        _uiContext.Post(_ =>
        {
            if (!_isExiting)
            {
                ApplyWindows(_claudeProvider.LoadCachedUsage());
            }
        }, null);
        await Task.CompletedTask;
    }

    private void ApplyWindows(IEnumerable<UsageWindow> windows)
    {
        foreach (UsageWindow window in windows)
        {
            _iconManager.Update(window);
        }
        _iconManager.RefreshStatusText();
    }
}
