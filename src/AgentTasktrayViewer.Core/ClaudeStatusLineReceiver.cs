namespace AgentTasktrayViewer.Core;

public interface IClaudeUsageUpdateNotifier
{
    Task NotifyAsync(CancellationToken cancellationToken = default);
}

public sealed class ClaudeStatusLineReceiver
{
    private readonly string _applicationDataDirectory;
    private readonly IClaudeUsageUpdateNotifier? _notifier;

    public ClaudeStatusLineReceiver(string applicationDataDirectory, IClaudeUsageUpdateNotifier? notifier = null)
    {
        _applicationDataDirectory = applicationDataDirectory;
        _notifier = notifier;
    }

    public async Task<bool> ReceiveAsync(TextReader input, CancellationToken cancellationToken = default)
    {
        string json = await input.ReadToEndAsync(cancellationToken);
        ClaudeUsageCache? cache = ClaudeStatusLineParser.Parse(json);
        if (cache is null || !ClaudeUsageProvider.SaveCache(_applicationDataDirectory, cache))
        {
            return false;
        }

        if (_notifier is not null)
        {
            await _notifier.NotifyAsync(cancellationToken);
        }
        return true;
    }
}
