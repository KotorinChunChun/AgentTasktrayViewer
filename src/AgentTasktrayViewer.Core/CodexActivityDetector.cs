namespace AgentTasktrayViewer.Core;

public enum ActivityStatus
{
    Active,
    Inactive,
    Unknown
}

public sealed class CodexActivityDetector
{
    private readonly string _sessionsDirectory;

    public CodexActivityDetector(string? sessionsDirectory = null)
    {
        _sessionsDirectory = sessionsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex", "sessions");
    }

    public ActivityStatus GetStatus(TimeSpan inactivityThreshold)
    {
        try
        {
            if (!Directory.Exists(_sessionsDirectory))
            {
                return ActivityStatus.Unknown;
            }

            DateTime latest = Directory.EnumerateFiles(_sessionsDirectory, "*.jsonl", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path).LastWriteTimeUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();
            if (latest == DateTime.MinValue)
            {
                return ActivityStatus.Unknown;
            }

            return DateTime.UtcNow - latest <= inactivityThreshold
                ? ActivityStatus.Active
                : ActivityStatus.Inactive;
        }
        catch (Exception)
        {
            return ActivityStatus.Unknown;
        }
    }
}
