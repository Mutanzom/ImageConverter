using ImageConverter.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ImageConverter.App;

/// <summary>Pure functions used from x:Bind expressions.</summary>
public static class Ui
{
    public static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility Hide(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility ShowText(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
    public static bool Not(bool value) => !value;

    public static Style? AccentIf(bool accent) =>
        Application.Current.Resources.TryGetValue(accent ? "AccentButtonStyle" : "DefaultButtonStyle", out var s) ? s as Style : null;

    // Nested function calls in x:Bind don't compile, hence the separate negation.
    public static Style? AccentUnless(bool plain) => AccentIf(!plain);

    // x:Bind can't pass enum literals, hence one function per case.
    public static Visibility ShowIfFit(ResizeMode mode) => Show(mode == ResizeMode.Fit);
    public static Visibility ShowIfPercent(ResizeMode mode) => Show(mode == ResizeMode.Percent);
    public static Visibility ShowIfResizing(ResizeMode mode) => Show(mode != ResizeMode.None);
    public static Visibility ShowIfSubfolder(OutputLocation location) => Show(location == OutputLocation.Subfolder);
    public static Visibility ShowIfFolder(OutputLocation location) => Show(location == OutputLocation.Folder);
    public static Visibility ShowIfDds(OutputFormat format) => Show(format == OutputFormat.Dds);
    public static Visibility ShowIfIco(OutputFormat format) => Show(format == OutputFormat.Ico);
    public static Visibility ShowIfWebP(OutputFormat format) => Show(format == OutputFormat.WebP);
    public static Visibility ShowIfQuality(FormatInfo info, bool lossless) => Show(info.HasQuality && !(info.HasLossless && lossless));
    public static Visibility ShowIfNoAlpha(FormatInfo info) => Show(!info.HasAlpha);

    public static string Percent(int value) => $"{value}";

    public static Brush ColorBrush(string? hex) => new SolidColorBrush(ParseHex(hex));

    /// <summary>"#RRGGBB" as written by the colour picker; anything else shows as white.</summary>
    public static Windows.UI.Color ParseHex(string? hex)
    {
        var s = hex?.Trim().TrimStart('#') ?? "";
        if (s.Length == 6 && uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var v))
            return Windows.UI.Color.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return Microsoft.UI.Colors.White;
    }

    public static string ToHex(Windows.UI.Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static string Size(long bytes) => bytes switch
    {
        <= 0 => "",
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _ => $"{bytes} B",
    };

    public static string SourceCaption(string format, long bytes) => $"{format} · {Size(bytes)}";

    public static string TargetCaption(ItemStatus status, long outputBytes, long sourceBytes) => status switch
    {
        ItemStatus.Done when sourceBytes > 0 => $"{Size(outputBytes)} · {outputBytes * 100 / sourceBytes}% of original",
        ItemStatus.Done => Size(outputBytes),
        ItemStatus.Converting => "Converting…",
        ItemStatus.Skipped => "Skipped",
        ItemStatus.Failed => "Failed",
        ItemStatus.Cancelled => "Cancelled",
        _ => "",
    };

    public static string StatusGlyph(ItemStatus status) => status switch
    {
        ItemStatus.Done => "",       // check
        ItemStatus.Converting => "", // sync
        ItemStatus.Skipped => "",    // forward
        ItemStatus.Failed => "",     // error
        ItemStatus.Cancelled => "",  // cancel
        _ => "",
    };

    public static Brush StatusBrush(ItemStatus status) => Resource(status switch
    {
        ItemStatus.Done => "SystemFillColorSuccessBrush",
        ItemStatus.Skipped => "SystemFillColorCautionBrush",
        ItemStatus.Failed => "SystemFillColorCriticalBrush",
        ItemStatus.Converting => "AccentTextFillColorPrimaryBrush",
        _ => "TextFillColorTertiaryBrush",
    });

    public static string StatusTip(ItemStatus status, string? message) => message ?? status switch
    {
        ItemStatus.Done => "Converted",
        ItemStatus.Converting => "Converting",
        ItemStatus.Skipped => "Skipped",
        ItemStatus.Cancelled => "Cancelled",
        ItemStatus.Failed => "Failed",
        _ => "Waiting",
    };

    private static Brush Resource(string key) =>
        Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b ? b : new SolidColorBrush(Microsoft.UI.Colors.Gray);
}
