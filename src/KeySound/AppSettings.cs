using System.IO;
using System.Text.Json;

namespace KeySound;

/// <summary>Last-used device and sound choices, stored in %AppData%\KeySound\settings.json.</summary>
public sealed class AppSettings
{
    public string? MidiInput { get; set; }
    public string? AudioOutput { get; set; }
    public string? Sound { get; set; }
    /// <summary>Null until the user (or auto-detection) picks a mode.</summary>
    public string? VelocityMode { get; set; }
    public double Volume { get; set; } = 0.8;

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeySound", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings: start fresh.
        }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
