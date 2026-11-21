namespace AgentTasktrayViewer;

internal sealed class StartupManager
{
    private const string StartupTaskId = "AgentTasktrayViewerStartup";

    public async Task<bool> SetEnabledAsync(bool enabled)
    {
        try
        {
            Windows.ApplicationModel.StartupTask task = await Windows.ApplicationModel.StartupTask.GetAsync(StartupTaskId);
            if (enabled)
            {
                Windows.ApplicationModel.StartupTaskState state = await task.RequestEnableAsync();
                return state is Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
            }

            task.Disable();
            return true;
        }
        catch (Exception)
        {
            // 未パッケージの開発実行ではStartupTaskを利用できない。
            return false;
        }
    }
}
