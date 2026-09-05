using AgentTasktrayViewer.Core;

namespace AgentTasktrayViewer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        try
        {
            Run(args);
        }
        catch (Exception exception)
        {
            WriteCrashLog("Program.Main", exception);
            MessageBox.Show(
                $"起動中にエラーが発生しました。詳細を次のログへ記録しました。\n\n{GetLogPath()}\n\n{exception.Message}",
                "AgentTasktrayViewer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void Run(string[] args)
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

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }

    private static void WriteCrashLog(string source, Exception? exception)
    {
        try
        {
            string path = GetLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path,
                $"{DateTimeOffset.Now:O} [ERROR] {source}{Environment.NewLine}" +
                $"{exception?.ToString() ?? "Unknown exception"}{Environment.NewLine}");
        }
        catch
        {
            // 障害記録の失敗によって二次障害を起こさない。
        }
    }

    private static string GetLogPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AgentTasktrayViewer",
        "app.log");
}
