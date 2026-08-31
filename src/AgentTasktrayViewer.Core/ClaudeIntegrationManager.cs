using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentTasktrayViewer.Core;

public enum ClaudeIntegrationStatus
{
    NotConfigured,
    ConfiguredByAgentTasktrayViewer,
    ExistingStatusLinePreserved
}

public sealed class ClaudeIntegrationManager
{
    private const string Marker = " --claude-statusline";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;
    private readonly string _backupPath;

    public ClaudeIntegrationManager(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");
        _backupPath = _settingsPath + ".agenttasktrayviewer.bak";
    }

    public ClaudeIntegrationStatus GetStatus()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return ClaudeIntegrationStatus.NotConfigured;
            }
            JsonObject root = ReadRoot();
            if (!root.TryGetPropertyValue("statusLine", out JsonNode? value) || value is null)
            {
                return ClaudeIntegrationStatus.NotConfigured;
            }
            return IsOurStatusLine(value)
                ? ClaudeIntegrationStatus.ConfiguredByAgentTasktrayViewer
                : ClaudeIntegrationStatus.ExistingStatusLinePreserved;
        }
        catch (Exception)
        {
            return ClaudeIntegrationStatus.ExistingStatusLinePreserved;
        }
    }

    public bool Configure(string executablePath)
    {
        try
        {
            JsonObject root = File.Exists(_settingsPath) ? ReadRoot() : new JsonObject();
            if (root.TryGetPropertyValue("statusLine", out JsonNode? existing) && existing is not null)
            {
                // 既存形式との安全な合成は行わず、既存設定を保護する。
                return false;
            }

            string? directory = Path.GetDirectoryName(_settingsPath);
            if (directory is null)
            {
                return false;
            }
            Directory.CreateDirectory(directory);
            if (File.Exists(_settingsPath))
            {
                File.Copy(_settingsPath, _backupPath, true);
            }
            root["statusLine"] = new JsonObject
            {
                ["type"] = "command",
                ["command"] = $"{Quote(executablePath)}{Marker}"
            };
            WriteRoot(root);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool Unconfigure(string executablePath)
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return false;
            }
            JsonObject root = ReadRoot();
            if (!root.TryGetPropertyValue("statusLine", out JsonNode? current) ||
                current is null || !IsOurStatusLine(current, executablePath))
            {
                return false;
            }

            if (File.Exists(_backupPath))
            {
                File.Copy(_backupPath, _settingsPath, true);
                File.Delete(_backupPath);
            }
            else
            {
                root.Remove("statusLine");
                WriteRoot(root);
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private JsonObject ReadRoot()
    {
        JsonNode? node = JsonNode.Parse(File.ReadAllText(_settingsPath));
        return node as JsonObject ?? new JsonObject();
    }

    private void WriteRoot(JsonObject root)
    {
        string temporaryPath = _settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, root.ToJsonString(JsonOptions));
        File.Move(temporaryPath, _settingsPath, true);
    }

    private bool IsOurStatusLine(JsonNode value, string? executablePath = null)
    {
        if (value is not JsonObject objectValue ||
            objectValue["type"]?.GetValue<string>() != "command" ||
            objectValue["command"]?.GetValue<string>() is not string command)
        {
            return false;
        }
        return command.EndsWith(Marker, StringComparison.Ordinal) &&
               (executablePath is null || command.StartsWith(Quote(executablePath), StringComparison.Ordinal));
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
