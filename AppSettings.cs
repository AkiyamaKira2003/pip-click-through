using System.Text.Json;

namespace PiPClickThrough;

public sealed class AppSettings
{
    public bool AutoDetect { get; set; } = true;
    public bool AutoEnable { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
    public int Opacity { get; set; } = 100;
    public bool MinimizeToTray { get; set; } = true;
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "PiPClickThrough");
    public static string FilePath => Path.Combine(DataDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath))
                       ?? new AppSettings();
        }
        catch { /* invalid settings -> defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* settings should never crash the app */ }
    }
}
