using System.Text.Json;

namespace ImageConverter.App.Services;

/// <summary>Small JSON settings file in %LOCALAPPDATA%\ImageConverter (unpackaged apps have no ApplicationData).</summary>
public sealed class AppSettings
{
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImageConverter");

    private static string FilePath => Path.Combine(Root, "settings.json");

    public bool IncludeSubfolders { get; set; }
    public int WindowWidth { get; set; } = 1240;
    public int WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }

    /// <summary>The conversion options from the last session.</summary>
    public string? OptionsJson { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception) { /* corrupt or unreadable: start fresh */ }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { /* settings are best effort */ }
    }
}
