using System.IO;
using System.Text.Json;

namespace PowerControlProWpf;

internal sealed class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Power Control PRO", "settings.json");

    public string Language { get; set; } = "de";
    public string Theme { get; set; } = "Standard";
    public int SizeIndex { get; set; } = 1;
    public bool WarningEnabled { get; set; } = true;
    public string WarningSound { get; set; } = "system-warning.wav";
    public int WarningVolume { get; set; } = 80;
    public bool RequestAdministratorOnLaunch { get; set; }
    public int? WindowLeftPx { get; set; }
    public int? WindowTopPx { get; set; }
    public string? WindowMonitor { get; set; }

    public static AppSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch (IOException) { return new AppSettings(); }
        catch (JsonException) { return new AppSettings(); }
        catch (UnauthorizedAccessException) { return new AppSettings(); }
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = SettingsPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, SettingsPath, true);
    }
}
