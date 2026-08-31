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
}

