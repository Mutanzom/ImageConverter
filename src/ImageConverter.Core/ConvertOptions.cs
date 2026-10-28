using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ImageConverter.Core;

public enum ResizeMode { None, Fit, Percent }
public enum OutputLocation { SameFolder, Subfolder, Folder }
public enum ConflictMode { AddNumber, Overwrite, Skip }

/// <summary>Everything the user can set for a conversion. Observable so the options panel binds to it directly.</summary>
public partial class ConvertOptions : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Info))] private OutputFormat _format = OutputFormat.WebP;

    /// <summary>1–100, for formats with <see cref="FormatInfo.HasQuality"/>.</summary>
    [ObservableProperty] private int _quality = 85;

    [ObservableProperty] private bool _lossless;

    /// <summary>Colour transparent pixels become in formats without alpha (JPEG, BMP).</summary>
    [ObservableProperty] private string _background = "#FFFFFF";

    [ObservableProperty] private ResizeMode _resize = ResizeMode.None;
    /// <summary>0 = no limit on that side.</summary>
    [ObservableProperty] private int _maxWidth = 1920;
    [ObservableProperty] private int _maxHeight = 1920;
    [ObservableProperty] private int _percent = 50;
    [ObservableProperty] private bool _allowUpscale;

    [ObservableProperty] private bool _keepMetadata = true;

    [ObservableProperty] private DdsCompression _ddsCompression = DdsCompression.Auto;
    [ObservableProperty] private bool _ddsMipmaps = true;

    [ObservableProperty] private OutputLocation _location = OutputLocation.Subfolder;
    [ObservableProperty] private string _subfolderName = "converted";
    [ObservableProperty] private string _outputFolder = "";
    [ObservableProperty] private bool _keepFolderStructure = true;
    [ObservableProperty] private string _suffix = "";
    [ObservableProperty] private ConflictMode _conflict = ConflictMode.AddNumber;

    public FormatInfo Info => FormatInfo.Of(Format);

    public ConvertOptions Clone() => FromJson(ToJson());

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static ConvertOptions FromJson(string json) =>
        JsonSerializer.Deserialize<ConvertOptions>(json, JsonOptions) ?? new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        IgnoreReadOnlyProperties = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
