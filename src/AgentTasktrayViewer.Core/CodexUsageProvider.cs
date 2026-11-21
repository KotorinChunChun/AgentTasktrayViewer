using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AgentTasktrayViewer.Core;

public sealed class CodexExecutableLocator
{
    public string? Find()
    {
        string? configuredPath = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenAI", "Codex", "bin");
        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory.EnumerateFiles(root, "codex.exe", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .FirstOrDefault();
    }
}

public sealed class CodexUsageProvider : IUsageProvider
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private readonly CodexExecutableLocator _executableLocator;

    public CodexUsageProvider(CodexExecutableLocator? executableLocator = null)
    {
        _executableLocator = executableLocator ?? new CodexExecutableLocator();
    }

    public async Task<IReadOnlyList<UsageWindow>> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        string? executablePath = _executableLocator.Find();
        if (executablePath is null)
        {
            return Array.Empty<UsageWindow>();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = "app-server --stdio",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        try
        {
            if (!process.Start())
            {
                return Array.Empty<UsageWindow>();
            }

            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 0,
                method = "initialize",
                @params = new
                {
                    clientInfo = new { name = "AgentTasktrayViewer", version = "0.0.1" }
                }
            }, timeout.Token);
            await SendAsync(process, new { jsonrpc = "2.0", method = "initialized", @params = new { } }, timeout.Token);
            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "account/rateLimits/read",
                @params = new { }
            }, timeout.Token);

            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                if (CodexRateLimitParser.IsResponseForRequest(line, 1))
                {
                    return CodexRateLimitParser.Parse(line);
                }
            }

            return Array.Empty<UsageWindow>();
        }
        catch (OperationCanceledException)
        {
            return Array.Empty<UsageWindow>();
        }
        catch (Exception)
        {
            return Array.Empty<UsageWindow>();
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    await process.WaitForExitAsync();
                }
            }
            catch (Exception)
            {
                // 子プロセスの終了処理はベストエフォートで行う。
            }
        }
    }

    private static async Task SendAsync(Process process, object message, CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }
}

public static class CodexRateLimitParser
{
    public static bool IsResponseForRequest(string json, int requestId)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("id", out JsonElement id) &&
                   id.ValueKind == JsonValueKind.Number && id.GetInt32() == requestId;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static IReadOnlyList<UsageWindow> Parse(string json, DateTimeOffset? now = null)
    {
        var windows = new List<UsageWindow>();
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("result", out JsonElement result) ||
                !result.TryGetProperty("rateLimits", out JsonElement rateLimits))
            {
                return windows;
            }

            if (rateLimits.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in rateLimits.EnumerateObject())
                {
                    AddIfRecognized(windows, property.Value, now);
                }
            }
            else if (rateLimits.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in rateLimits.EnumerateArray())
                {
                    AddIfRecognized(windows, item, now);
                }
            }
        }
        catch (JsonException)
        {
            // 不正な応答は空の結果として扱う。
        }

        return windows;
    }

    private static void AddIfRecognized(List<UsageWindow> windows, JsonElement value, DateTimeOffset? now)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!value.TryGetProperty("windowDurationMins", out JsonElement duration) ||
            !duration.TryGetInt32(out int durationMinutes) ||
            !value.TryGetProperty("usedPercent", out JsonElement used) ||
            !used.TryGetDouble(out double usedPercent))
        {
            return;
        }

        UsageWindowType? type = durationMinutes switch
        {
            300 => UsageWindowType.FiveHour,
            > 300 => UsageWindowType.Weekly,
            _ => null
        };
        if (type is null)
        {
            return;
        }

        long? resetsAt = value.TryGetProperty("resetsAt", out JsonElement reset) &&
                         reset.TryGetInt64(out long resetValue)
            ? resetValue
            : null;
        windows.Add(UsageWindow.Available(UsageProvider.Codex, type.Value, usedPercent,
            UnixTimeConverter.ToLocal(resetsAt), now, durationMinutes));
    }
}
