namespace AgentTasktrayViewer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // 常駐メッセージループはトレイ管理の実装フェーズで追加します。
    }
}

