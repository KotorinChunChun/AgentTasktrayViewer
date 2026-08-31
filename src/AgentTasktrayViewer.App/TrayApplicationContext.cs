using AgentTasktrayViewer.Core;

namespace AgentTasktrayViewer;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly TimeSpan InactivityThreshold = TimeSpan.FromMinutes(15);
    private readonly SettingsManager _settingsManager;
    private readonly AppSettings _settings;
    private readonly CodexUsageProvider _codexProvider = new();
    private readonly ClaudeUsageProvider _claudeProvider;
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
    private bool _wasInactive;
    private bool _isExiting;

    public TrayApplicationContext()
    {
        _settingsManager = new SettingsManager();
        _settings = _settingsManager.Load();
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _claudeProvider = new ClaudeUsageProvider(_settingsManager.ApplicationDataDirectory);
        _iconManager = new TrayIconManager(_settings);
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
        _ = UpdateAllAsync();
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

        var visibility = new ToolStripMenuItem("表示するアイコン");
        AddVisibilityItem(visibility, "Codex 5時間", TrayIconKind.CodexFiveHour);
        AddVisibilityItem(visibility, "Codex 週間", TrayIconKind.CodexWeekly);
        AddVisibilityItem(visibility, "Claude 5時間", TrayIconKind.ClaudeFiveHour);
        AddVisibilityItem(visibility, "Claude 週間", TrayIconKind.ClaudeWeekly);
        menu.Items.Add(visibility);

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
        updateNow.Click += (_, _) => _ = UpdateAllAsync();
        menu.Items.Add(updateNow);
        menu.Items.Add(CreateBrowserItem("Codex 使用量ページを開く", "https://chatgpt.com/codex/settings/usage"));
        menu.Items.Add(CreateBrowserItem("Claude 使用量ページを開く", "https://claude.ai/settings/usage"));
        menu.Items.Add(new ToolStripSeparator());
        var exit = new ToolStripMenuItem("終了");
        exit.Click += (_, _) => ExitThread();
        menu.Items.Add(exit);

        menu.Opening += (_, _) =>
        {
            foreach (ToolStripMenuItem item in interval.DropDownItems)
            {
                item.Checked = (int)item.Tag! == _settings.UpdateIntervalMinutes;
            }
            _iconManager.RefreshStatusText();
        };
        return menu;
    }

    private void AddVisibilityItem(ToolStripMenuItem parent, string text, TrayIconKind kind)
    {
        var item = new ToolStripMenuItem(text) { CheckOnClick = true, Checked = _iconManager.IsVisible(kind), Tag = kind };
        item.Click += (_, _) => _iconManager.TrySetVisibility(kind, item.Checked);
        _iconManager.VisibilityStateChanged += (_, e) =>
        {
            if (e.Kind == kind)
            {
                item.Checked = e.IsVisible;
            }
        };
        parent.DropDownItems.Add(item);
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

    private async Task UpdateAllAsync()
    {
        if (!await _updateGate.WaitAsync(0, _shutdown.Token))
        {
            return;
        }
        try
        {
            IReadOnlyList<UsageWindow> codex = await _codexProvider.GetUsageAsync(_shutdown.Token);
            ApplyWindows(codex);
            ApplyWindows(_claudeProvider.LoadCachedUsage());
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
