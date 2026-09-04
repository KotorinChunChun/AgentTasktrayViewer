namespace AgentTasktrayViewer.Core;

public enum UsageProvider
{
    Codex,
    Claude
}

public enum UsageWindowType
{
    FiveHour,
    Weekly
}

public sealed record UsageWindow(
    UsageProvider Provider,
    UsageWindowType WindowType,
    double UsedPercent,
    DateTimeOffset? ResetsAt,
    DateTimeOffset LastUpdatedAt,
    bool IsAvailable,
    int? WindowDurationMinutes = null)
{
    public double RemainingPercent => Math.Clamp(100d - UsedPercent, 0d, 100d);

    public static UsageWindow Unavailable(
        UsageProvider provider,
        UsageWindowType windowType,
        DateTimeOffset? lastUpdatedAt = null) =>
        new(provider, windowType, 0d, null, lastUpdatedAt ?? DateTimeOffset.Now, false, null);

    public static UsageWindow Available(
        UsageProvider provider,
        UsageWindowType windowType,
        double usedPercent,
        DateTimeOffset? resetsAt,
        DateTimeOffset? lastUpdatedAt = null,
        int? windowDurationMinutes = null) =>
        new(provider, windowType, Math.Clamp(usedPercent, 0d, 100d), resetsAt,
            lastUpdatedAt ?? DateTimeOffset.Now, true, windowDurationMinutes);
}

public interface IUsageProvider
{
    Task<IReadOnlyList<UsageWindow>> GetUsageAsync(CancellationToken cancellationToken = default);
}

public static class UnixTimeConverter
{
    public static DateTimeOffset? ToLocal(long? unixSeconds) =>
        unixSeconds is null
            ? null
            : DateTimeOffset.FromUnixTimeSeconds(unixSeconds.Value).ToLocalTime();
}
