namespace AgentTasktrayViewer;

internal static class WindowTitle
{
    public static string Create(string? screen = null)
    {
        var version = typeof(WindowTitle).Assembly.GetName().Version!.ToString(3);
        return (string.IsNullOrEmpty(screen) ? "" : screen + " - ") + "AgentTasktrayViewer - ver " + version;
    }
}
