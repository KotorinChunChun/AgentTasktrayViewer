using AgentTasktrayViewer.Core;

namespace AgentTasktrayViewer;

internal enum TrayIconKind
{
    CodexFiveHour,
    CodexWeekly,
    ClaudeFiveHour,
    ClaudeWeekly
}

internal sealed class VisibilityStateEventArgs(TrayIconKind kind, bool isVisible) : EventArgs
{
    public TrayIconKind Kind { get; } = kind;
    public bool IsVisible { get; } = isVisible;
}

internal sealed class StatusTextEventArgs(
    string codexFiveHour,
    string codexWeekly,
    string claudeFiveHour,
    string claudeWeekly) : EventArgs
{
    public string CodexFiveHour { get; } = codexFiveHour;
    public string CodexWeekly { get; } = codexWeekly;
    public string ClaudeFiveHour { get; } = claudeFiveHour;
    public string ClaudeWeekly { get; } = claudeWeekly;
}

internal sealed class TrayIconManager : IDisposable
{
    private static readonly IReadOnlyDictionary<TrayIconKind, Color> Colors = new Dictionary<TrayIconKind, Color>
    {
        [TrayIconKind.CodexFiveHour] = Color.FromArgb(37, 99, 235),
        [TrayIconKind.CodexWeekly] = Color.FromArgb(124, 58, 237),
        [TrayIconKind.ClaudeFiveHour] = Color.FromArgb(217, 119, 6),
        [TrayIconKind.ClaudeWeekly] = Color.FromArgb(15, 118, 110)
    };

    private readonly Dictionary<TrayIconKind, UsageWindow> _windows = new();
    private ContextMenuStrip? _menu;
    private bool _disposed;

    public TrayIconManager(AppSettings settings)
    {
        CodexFiveHourIcon = CreateIcon(UsageProvider.Codex, UsageWindowType.FiveHour, Colors[TrayIconKind.CodexFiveHour]);
        CodexWeeklyIcon = CreateIcon(UsageProvider.Codex, UsageWindowType.Weekly, Colors[TrayIconKind.CodexWeekly]);
        ClaudeFiveHourIcon = CreateIcon(UsageProvider.Claude, UsageWindowType.FiveHour, Colors[TrayIconKind.ClaudeFiveHour]);
        ClaudeWeeklyIcon = CreateIcon(UsageProvider.Claude, UsageWindowType.Weekly, Colors[TrayIconKind.ClaudeWeekly]);

        foreach (TrayIconKind kind in Enum.GetValues<TrayIconKind>())
        {
            _windows[kind] = UsageWindow.Unavailable(ToProvider(kind), ToWindowType(kind));
        }
        ApplyVisibility(settings);
    }

    public NotifyIcon CodexFiveHourIcon { get; }
    public NotifyIcon CodexWeeklyIcon { get; }
    public NotifyIcon ClaudeFiveHourIcon { get; }
    public NotifyIcon ClaudeWeeklyIcon { get; }

    public event EventHandler? VisibilityChanged;
    public event EventHandler<VisibilityStateEventArgs>? VisibilityStateChanged;
    public event EventHandler<StatusTextEventArgs>? StatusTextChanged;

    public bool IsVisible(TrayIconKind kind) => GetIcon(kind).Visible;

    public void AttachMenu(ContextMenuStrip menu)
    {
        _menu = menu;
        foreach (NotifyIcon icon in AllIcons())
        {
            icon.ContextMenuStrip = menu;
        }
    }

    public void ApplyVisibility(AppSettings settings)
    {
        SetVisible(TrayIconKind.CodexFiveHour, settings.ShowCodexFiveHour);
        SetVisible(TrayIconKind.CodexWeekly, settings.ShowCodexWeekly);
        SetVisible(TrayIconKind.ClaudeFiveHour, settings.ShowClaudeFiveHour);
        SetVisible(TrayIconKind.ClaudeWeekly, settings.ShowClaudeWeekly);
        if (AllIcons().All(icon => !icon.Visible))
        {
            CodexFiveHourIcon.Visible = true;
        }
    }

    public bool TrySetVisibility(TrayIconKind kind, bool isVisible)
    {
        if (!isVisible && AllIcons().Count(icon => icon.Visible) <= 1)
        {
            VisibilityStateChanged?.Invoke(this, new VisibilityStateEventArgs(kind, true));
            return false;
        }

        SetVisible(kind, isVisible);
        VisibilityChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Update(UsageWindow window)
    {
        TrayIconKind kind = ToKind(window.Provider, window.WindowType);
        _windows[kind] = window;
        NotifyIcon icon = GetIcon(kind);
        Icon? previous = icon.Icon;
        icon.Icon = IconFactory.Create(window, Colors[kind]);
        previous?.Dispose();
        icon.Text = BuildToolTip(window);
    }

    public void RefreshStatusText()
    {
        StatusTextChanged?.Invoke(this, new StatusTextEventArgs(
            BuildStatusText(TrayIconKind.CodexFiveHour),
            BuildStatusText(TrayIconKind.CodexWeekly),
            BuildStatusText(TrayIconKind.ClaudeFiveHour),
            BuildStatusText(TrayIconKind.ClaudeWeekly)));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        foreach (NotifyIcon icon in AllIcons())
        {
            icon.Visible = false;
            Icon? current = icon.Icon;
            icon.Icon = null;
            current?.Dispose();
            icon.Dispose();
        }
        _menu?.Dispose();
    }

    private void SetVisible(TrayIconKind kind, bool isVisible)
    {
        GetIcon(kind).Visible = isVisible;
        VisibilityStateChanged?.Invoke(this, new VisibilityStateEventArgs(kind, isVisible));
    }

    private NotifyIcon CreateIcon(UsageProvider provider, UsageWindowType windowType, Color color)
    {
        var icon = new NotifyIcon
        {
            Icon = IconFactory.Create(UsageWindow.Unavailable(provider, windowType), color),
            Text = "AgentTasktrayViewer: ?"
        };
        icon.Click += (_, _) => { };
        return icon;
    }

    private NotifyIcon GetIcon(TrayIconKind kind) => kind switch
    {
        TrayIconKind.CodexFiveHour => CodexFiveHourIcon,
        TrayIconKind.CodexWeekly => CodexWeeklyIcon,
        TrayIconKind.ClaudeFiveHour => ClaudeFiveHourIcon,
        TrayIconKind.ClaudeWeekly => ClaudeWeeklyIcon,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private IEnumerable<NotifyIcon> AllIcons()
    {
        yield return CodexFiveHourIcon;
        yield return CodexWeeklyIcon;
        yield return ClaudeFiveHourIcon;
        yield return ClaudeWeeklyIcon;
    }

    private string BuildStatusText(TrayIconKind kind)
    {
        UsageWindow window = _windows[kind];
        return $"{DisplayName(kind)}  残り{DisplayPercent(window)}%（{DisplayUsed(window)}%使用）  リセット {FormatReset(window.ResetsAt)}";
    }

    private string BuildToolTip(UsageWindow window)
    {
        string text = $"{DisplayName(ToKind(window.Provider, window.WindowType))}: 残り{DisplayPercent(window)}% / {DisplayUsed(window)}%使用 / {FormatReset(window.ResetsAt)}リセット";
        return text.Length <= 63 ? text : text[..60] + "...";
    }

    private static string DisplayPercent(UsageWindow window) => window.IsAvailable ? $"{Math.Round(window.RemainingPercent, MidpointRounding.AwayFromZero):0}" : "?";
    private static string DisplayUsed(UsageWindow window) => window.IsAvailable ? $"{Math.Round(window.UsedPercent, MidpointRounding.AwayFromZero):0}" : "-";
    private static string FormatReset(DateTimeOffset? value) => value is null ? "不明" : value.Value.ToString("M/d HH:mm");
    private static string DisplayName(TrayIconKind kind) => kind switch
    {
        TrayIconKind.CodexFiveHour => "Codex 5時間",
        TrayIconKind.CodexWeekly => "Codex 週間",
        TrayIconKind.ClaudeFiveHour => "Claude 5時間",
        TrayIconKind.ClaudeWeekly => "Claude 週間",
        _ => "使用量"
    };
    private static UsageProvider ToProvider(TrayIconKind kind) => kind is TrayIconKind.CodexFiveHour or TrayIconKind.CodexWeekly
        ? UsageProvider.Codex : UsageProvider.Claude;
    private static UsageWindowType ToWindowType(TrayIconKind kind) => kind is TrayIconKind.CodexFiveHour or TrayIconKind.ClaudeFiveHour
        ? UsageWindowType.FiveHour : UsageWindowType.Weekly;
    private static TrayIconKind ToKind(UsageProvider provider, UsageWindowType type) => (provider, type) switch
    {
        (UsageProvider.Codex, UsageWindowType.FiveHour) => TrayIconKind.CodexFiveHour,
        (UsageProvider.Codex, UsageWindowType.Weekly) => TrayIconKind.CodexWeekly,
        (UsageProvider.Claude, UsageWindowType.FiveHour) => TrayIconKind.ClaudeFiveHour,
        (UsageProvider.Claude, UsageWindowType.Weekly) => TrayIconKind.ClaudeWeekly,
        _ => throw new ArgumentOutOfRangeException()
    };
}
