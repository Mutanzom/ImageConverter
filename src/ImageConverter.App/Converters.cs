using System.Text.RegularExpressions;
using ImageConverter.Core;
using Microsoft.UI.Xaml.Data;

namespace ImageConverter.App;

/// <summary>Friendly labels for enum values shown in combo boxes.</summary>
public partial class EnumLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => Label(value);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    public static string Label(object? value) => value switch
    {
        ResizeMode.None => "Keep original size",
        ResizeMode.Fit => "Fit within width × height",
        ResizeMode.Percent => "Scale by percent",
        OutputLocation.SameFolder => "Same folder as the original",
        OutputLocation.Subfolder => "Subfolder next to the original",
        OutputLocation.Folder => "Another folder",
        ConflictMode.AddNumber => "Add (2), (3)…",
        ConflictMode.Overwrite => "Overwrite",
        ConflictMode.Skip => "Skip",
        DdsCompression.Auto => "Auto (DXT1, or DXT5 with alpha)",
        DdsCompression.Dxt1 => "BC1 / DXT1 — no alpha, smallest",
        DdsCompression.Dxt5 => "BC3 / DXT5 — full alpha",
        DdsCompression.Uncompressed => "Uncompressed RGBA",
        FormatInfo f => f.Label,
        null => "",
        _ => Regex.Replace(value.ToString()!, "(?<=[a-z])(?=[A-Z])", " "),
    };
}

/// <summary>Enum value lists for x:Bind ItemsSource.</summary>
public static class Choices
{
    public static IReadOnlyList<FormatInfo> Formats => FormatInfo.Writable;
    public static ResizeMode[] ResizeModes { get; } = Enum.GetValues<ResizeMode>();
    public static OutputLocation[] Locations { get; } = Enum.GetValues<OutputLocation>();
    public static ConflictMode[] Conflicts { get; } = Enum.GetValues<ConflictMode>();
    public static DdsCompression[] DdsCompressions { get; } = Enum.GetValues<DdsCompression>();
}
