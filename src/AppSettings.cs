using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace SpotifyToast;

internal sealed class AppSettings
{
    public bool Glass { get; set; } = true;
    public string Theme { get; set; } = "dark";      // dark | light | system
    public string Position { get; set; } = "left";   // left | right
    public int DurationSeconds { get; set; } = 5;
    public bool ShowInFullscreen { get; set; } = false;

    [JsonIgnore] public bool IsDark => Theme switch
    {
        "light" => false,
        "system" => !ReadLight("AppsUseLightTheme"),
        _ => true,
    };

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpotifyToast");
    static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load(out bool firstRun)
    {
        firstRun = !File.Exists(FilePath);
        if (firstRun) return new AppSettings();
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings(); }
        catch { return new AppSettings(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static bool ReadLight(string name)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue(name) is int v && v == 1;
        }
        catch { return false; }
    }
}

internal static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "SpotifyToast";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                return k?.GetValue(Name) is string;
            }
            catch { return false; }
        }
    }

    public static void Set(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) k.SetValue(Name, $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue(Name, false);
        }
        catch { }
    }
}
