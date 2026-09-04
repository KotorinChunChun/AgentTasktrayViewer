using AgentTasktrayViewer.Core;

namespace AgentTasktrayViewer.Core.Tests;

[TestClass]
public sealed class ProjectStructureTests
{
    [TestMethod]
    public void Coreアセンブリ名が想定どおりである()
    {
        string? assemblyName = typeof(ProjectMarker).Assembly.GetName().Name;

        Assert.AreEqual("AgentTasktrayViewer.Core", assemblyName);
    }

    [TestMethod]
    public void 使用量モデルは残量を0から100へ制限する()
    {
        UsageWindow window = UsageWindow.Available(
            UsageProvider.Codex,
            UsageWindowType.FiveHour,
            125,
            null);

        Assert.AreEqual(100d, window.UsedPercent);
        Assert.AreEqual(0d, window.RemainingPercent);
    }

    [TestMethod]
    public void Codex応答は枠時間で5時間枠と週間枠へ分類する()
    {
        const string json = """
            {"result":{"rateLimits":{"primary":{"usedPercent":18.5,"windowDurationMins":300,"resetsAt":1700000000},"secondary":{"usedPercent":39,"windowDurationMins":10080,"resetsAt":1700100000}}}}
            """;

        IReadOnlyList<UsageWindow> windows = CodexRateLimitParser.Parse(json);

        Assert.AreEqual(2, windows.Count);
        Assert.AreEqual(UsageWindowType.FiveHour, windows[0].WindowType);
        Assert.AreEqual(UsageWindowType.Weekly, windows[1].WindowType);
        Assert.AreEqual(81.5d, windows[0].RemainingPercent, 0.001d);
    }

    [TestMethod]
    public void Codex応答は未知の枠を無視し既知の枠だけ返す()
    {
        const string json = """
            {"result":{"rateLimits":{"a":{"usedPercent":10,"windowDurationMins":60},"b":{"usedPercent":20,"windowDurationMins":300}}}}
            """;

        IReadOnlyList<UsageWindow> windows = CodexRateLimitParser.Parse(json);

        Assert.AreEqual(1, windows.Count);
        Assert.AreEqual(UsageWindowType.FiveHour, windows[0].WindowType);
    }

    [TestMethod]
    public void Codex応答は5時間を超える長期枠も週間枠として取得する()
    {
        const string json = """
            {"result":{"rateLimits":{"primary":{"usedPercent":74,"windowDurationMins":43200,"resetsAt":1700000000},"secondary":null}}}
            """;

        IReadOnlyList<UsageWindow> windows = CodexRateLimitParser.Parse(json);

        Assert.AreEqual(1, windows.Count);
        Assert.AreEqual(UsageWindowType.Weekly, windows[0].WindowType);
        Assert.AreEqual(43200, windows[0].WindowDurationMinutes);
        Assert.AreEqual(26d, windows[0].RemainingPercent);
    }

    [TestMethod]
    public void ClaudeStatusLineは必要な2枠だけ抽出する()
    {
        const string json = """
            {"rate_limits":{"five_hour":{"used_percentage":63,"resets_at":1700000000},"seven_day":{"used_percentage":26,"resets_at":1700100000}},"transcript_path":"secret","cwd":"secret"}
            """;

        ClaudeUsageCache? cache = ClaudeStatusLineParser.Parse(json);

        Assert.IsNotNull(cache);
        Assert.AreEqual(63d, cache!.FiveHour!.UsedPercentage);
        Assert.AreEqual(26d, cache.SevenDay!.UsedPercentage);
    }

    [TestMethod]
    public void 設定ファイル破損時は既定値へ戻る()
    {
        string directory = Path.Combine(Path.GetTempPath(), "AgentTasktrayViewerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, "{ broken json");

            AppSettings settings = new SettingsManager(directory).Load();

            Assert.IsTrue(settings.ShowCodexFiveHour);
            Assert.AreEqual(5, settings.UpdateIntervalMinutes);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task ClaudeStatusLine受信は最小キャッシュを保存する()
    {
        string directory = Path.Combine(Path.GetTempPath(), "AgentTasktrayViewerTests", Guid.NewGuid().ToString("N"));
        try
        {
            const string json = "{\"rate_limits\":{\"five_hour\":{\"used_percentage\":12,\"resets_at\":1700000000}}}";
            var receiver = new ClaudeStatusLineReceiver(directory);

            bool result = await receiver.ReceiveAsync(new StringReader(json));
            string cache = await File.ReadAllTextAsync(Path.Combine(directory, "claude-usage-cache.json"));

            Assert.IsTrue(result);
            StringAssert.Contains(cache, "FiveHour");
            Assert.DoesNotContain("transcript_path", cache);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [TestMethod]
    public void Claude既存statusLineは設定操作で上書きしない()
    {
        string directory = Path.Combine(Path.GetTempPath(), "AgentTasktrayViewerTests", Guid.NewGuid().ToString("N"));
        string settingsPath = Path.Combine(directory, "settings.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(settingsPath, "{\"statusLine\":{\"type\":\"command\",\"command\":\"existing\"},\"other\":true}");
            var manager = new ClaudeIntegrationManager(settingsPath);

            bool configured = manager.Configure("C:\\AgentTasktrayViewer.exe");

            Assert.IsFalse(configured);
            StringAssert.Contains(File.ReadAllText(settingsPath), "existing");
            Assert.AreEqual(ClaudeIntegrationStatus.ExistingStatusLinePreserved, manager.GetStatus());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
