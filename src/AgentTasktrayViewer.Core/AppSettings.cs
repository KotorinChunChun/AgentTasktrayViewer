using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentTasktrayViewer.Core;

public enum TrayIconVisualStyle
{
    ColoredBackground,
    ColoredText
}

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

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TrayIconVisualStyle IconVisualStyle { get; set; } = TrayIconVisualStyle.ColoredBackground;

    public string IconFontFamily { get; set; } = "Segoe UI";

    [JsonIgnore]
    public TimeSpan UpdateInterval => TimeSpan.FromMinutes(UpdateIntervalMinutes);

    public void Normalize()
    {
        if (!new[] { 1, 3, 5, 10 }.Contains(UpdateIntervalMinutes))
        {
            UpdateIntervalMinutes = 5;
        }
        if (!Enum.IsDefined(IconVisualStyle))
        {
            IconVisualStyle = TrayIconVisualStyle.ColoredBackground;
        }
        if (string.IsNullOrWhiteSpace(IconFontFamily) || IconFontFamily.Length > 64)
        {
            IconFontFamily = "Segoe UI";
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
