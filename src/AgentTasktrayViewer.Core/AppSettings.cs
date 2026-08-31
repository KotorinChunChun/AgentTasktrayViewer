using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentTasktrayViewer.Core;

public sealed class AppSettings
{
    public bool ShowCodexFiveHour { get; set; } = true;
    public bool ShowCodexWeekly { get; set; } = true;
    public bool ShowClaudeFiveHour { get; set; } = true;
    public bool ShowClaudeWeekly { get; set; } = true;
    public int UpdateIntervalMinutes { get; set; } = 5;
    public bool PauseWhenInactive { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool ClaudeIntegrationEnabled { get; set; }

    [JsonIgnore]
    public TimeSpan UpdateInterval => TimeSpan.FromMinutes(UpdateIntervalMinutes);

    public void Normalize()
    {
        if (!new[] { 1, 3, 5, 10 }.Contains(UpdateIntervalMinutes))
        {
            UpdateIntervalMinutes = 5;
        }
    }
}

public sealed class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SettingsManager(string? applicationDataDirectory = null)
    {
        ApplicationDataDirectory = applicationDataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AgentTasktrayViewer");
        SettingsPath = Path.Combine(ApplicationDataDirectory, "settings.json");
    }

    public string ApplicationDataDirectory { get; }
    public string SettingsPath { get; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();
        Directory.CreateDirectory(ApplicationDataDirectory);
        string temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, SettingsPath, true);
    }
}
