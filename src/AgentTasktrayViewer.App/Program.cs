using AgentTasktrayViewer.Core;

namespace AgentTasktrayViewer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Any(argument => string.Equals(argument, "--claude-statusline", StringComparison.OrdinalIgnoreCase)))
        {
            var settingsManager = new SettingsManager();
            var receiver = new ClaudeStatusLineReceiver(
                settingsManager.ApplicationDataDirectory,
                new NamedPipeClaudeUsageNotifier());
            receiver.ReceiveAsync(Console.In).GetAwaiter().GetResult();
            return;
        }

        using var mutex = new Mutex(true, "AgentTasktrayViewer.NormalMode", out bool createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
