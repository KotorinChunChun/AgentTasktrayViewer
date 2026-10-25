namespace AgentTasktrayViewer.Core.Tests;

[TestClass]
public sealed class LaunchOptionsTests
{
    [TestMethod]
    public void 通常起動は画面表示して無表示指定は優先する()
    {
        Assert.IsFalse(LaunchOptions.Parse([]).Hidden);
        Assert.IsTrue(LaunchOptions.Parse(["--startup"]).Hidden);
        Assert.IsTrue(LaunchOptions.Parse(["--launch-origin", "host_manual", "--hidden"]).Hidden);
    }

    [TestMethod]
    public void 設定先を絶対パスにして試験時の取得を停止できる()
    {
        var options = LaunchOptions.Parse(["--settings-dir", "tmp/test-settings", "--passive"]);
        Assert.AreEqual(Path.GetFullPath("tmp/test-settings"), options.SettingsDirectory);
        Assert.IsTrue(options.Passive);
    }

    [TestMethod]
    public void 不完全な引数と未知の経路を拒否する()
    {
        foreach (var args in new[] {
            new[] { "--settings-dir" }, new[] { "--settings-dir", "--startup" },
            new[] { "--launch-origin" }, new[] { "--launch-origin", "unknown" }, new[] { "--unknown" }
        })
        {
            bool rejected = false;
            try { LaunchOptions.Parse(args); } catch (ArgumentException) { rejected = true; }
            Assert.IsTrue(rejected, string.Join(" ", args));
        }
    }
}
