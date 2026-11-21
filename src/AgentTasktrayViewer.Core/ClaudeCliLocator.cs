namespace AgentTasktrayViewer.Core;

public sealed class ClaudeCliLocator
{
    public string? Find()
    {
        string? configuredPath = Environment.GetEnvironmentVariable("CLAUDE_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        IEnumerable<string> directories = (pathValue ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));

        foreach (string directory in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (string fileName in new[] { "claude.exe", "claude.cmd", "claude.bat", "claude" })
            {
                string path = Path.Combine(directory, fileName);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }
}
