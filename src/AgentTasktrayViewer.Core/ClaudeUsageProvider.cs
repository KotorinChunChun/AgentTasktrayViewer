using System.Text.Json;

namespace AgentTasktrayViewer.Core;

public sealed record ClaudeRateLimitCacheEntry(double UsedPercentage, long? ResetsAt);

public sealed class ClaudeUsageCache
{
    public ClaudeRateLimitCacheEntry? FiveHour { get; init; }
    public ClaudeRateLimitCacheEntry? SevenDay { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
}

public static class ClaudeStatusLineParser
{
    public static ClaudeUsageCache? Parse(string json, DateTimeOffset? receivedAt = null)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("rate_limits", out JsonElement limits))
            {
                return null;
            }

            return new ClaudeUsageCache
            {
                FiveHour = ReadEntry(limits, "five_hour"),
                SevenDay = ReadEntry(limits, "seven_day"),
                ReceivedAt = receivedAt ?? DateTimeOffset.Now
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ClaudeRateLimitCacheEntry? ReadEntry(JsonElement limits, string name)
    {
        if (!limits.TryGetProperty(name, out JsonElement value) ||
            !value.TryGetProperty("used_percentage", out JsonElement used) ||
            !used.TryGetDouble(out double usedPercentage))
        {
            return null;
        }

        long? resetsAt = value.TryGetProperty("resets_at", out JsonElement reset) &&
                         reset.TryGetInt64(out long resetValue)
            ? resetValue
            : null;
        return new ClaudeRateLimitCacheEntry(Math.Clamp(usedPercentage, 0d, 100d), resetsAt);
    }
}

public sealed class ClaudeUsageProvider : IUsageProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly string _cachePath;

    public ClaudeUsageProvider(string applicationDataDirectory)
    {
        _cachePath = Path.Combine(applicationDataDirectory, "claude-usage-cache.json");
    }

    public string CachePath => _cachePath;

    public IReadOnlyList<UsageWindow> LoadCachedUsage()
    {
        try
        {
            if (!File.Exists(_cachePath))
            {
                return UnavailableWindows();
            }

            ClaudeUsageCache? cache = JsonSerializer.Deserialize<ClaudeUsageCache>(File.ReadAllText(_cachePath), JsonOptions);
            if (cache is null)
            {
                return UnavailableWindows();
            }

            return new[]
            {
                ToWindow(UsageWindowType.FiveHour, cache.FiveHour, cache.ReceivedAt),
                ToWindow(UsageWindowType.Weekly, cache.SevenDay, cache.ReceivedAt)
            };
        }
        catch (Exception)
        {
            return UnavailableWindows();
        }
    }

    public async Task<IReadOnlyList<UsageWindow>> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        return LoadCachedUsage();
    }

    public static bool SaveCache(string applicationDataDirectory, ClaudeUsageCache cache)
    {
        try
        {
            Directory.CreateDirectory(applicationDataDirectory);
            string path = Path.Combine(applicationDataDirectory, "claude-usage-cache.json");
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(cache, JsonOptions));
            File.Move(temporaryPath, path, true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static UsageWindow ToWindow(UsageWindowType type, ClaudeRateLimitCacheEntry? entry, DateTimeOffset receivedAt) =>
        entry is null
            ? UsageWindow.Unavailable(UsageProvider.Claude, type, receivedAt)
            : UsageWindow.Available(UsageProvider.Claude, type, entry.UsedPercentage,
                UnixTimeConverter.ToLocal(entry.ResetsAt), receivedAt);

    private static IReadOnlyList<UsageWindow> UnavailableWindows() => new[]
    {
        UsageWindow.Unavailable(UsageProvider.Claude, UsageWindowType.FiveHour),
        UsageWindow.Unavailable(UsageProvider.Claude, UsageWindowType.Weekly)
    };
}
