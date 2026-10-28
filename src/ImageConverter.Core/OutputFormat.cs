using ImageMagick;

namespace ImageConverter.Core;

public enum OutputFormat { Png, Jpeg, WebP, Avif, Heic, Tiff, Bmp, Gif, Ico, Dds }

/// <summary>What each output format can do, so the UI only shows options that apply.</summary>
public sealed record FormatInfo(
    OutputFormat Format,
    string Label,
    string Extension,
    MagickFormat Magick,
    bool HasQuality,
    bool HasLossless,
    bool HasAlpha)
{
    public static readonly IReadOnlyList<FormatInfo> All =
    [
        new(OutputFormat.Png, "PNG", ".png", MagickFormat.Png, HasQuality: false, HasLossless: false, HasAlpha: true),
        new(OutputFormat.Jpeg, "JPEG", ".jpg", MagickFormat.Jpeg, HasQuality: true, HasLossless: false, HasAlpha: false),
        new(OutputFormat.WebP, "WebP", ".webp", MagickFormat.WebP, HasQuality: true, HasLossless: true, HasAlpha: true),
        new(OutputFormat.Avif, "AVIF", ".avif", MagickFormat.Avif, HasQuality: true, HasLossless: false, HasAlpha: true),
        new(OutputFormat.Heic, "HEIC", ".heic", MagickFormat.Heic, HasQuality: true, HasLossless: false, HasAlpha: true),
        new(OutputFormat.Tiff, "TIFF", ".tif", MagickFormat.Tiff, HasQuality: false, HasLossless: false, HasAlpha: true),
        new(OutputFormat.Bmp, "BMP", ".bmp", MagickFormat.Bmp3, HasQuality: false, HasLossless: false, HasAlpha: false),
        new(OutputFormat.Gif, "GIF", ".gif", MagickFormat.Gif, HasQuality: false, HasLossless: false, HasAlpha: true),
        new(OutputFormat.Ico, "ICO (icon)", ".ico", MagickFormat.Ico, HasQuality: false, HasLossless: false, HasAlpha: true),
        new(OutputFormat.Dds, "DDS (texture)", ".dds", MagickFormat.Dds, HasQuality: false, HasLossless: false, HasAlpha: true),
    ];

    public static FormatInfo Of(OutputFormat format) => All.First(f => f.Format == format);

    /// <summary>Combo boxes and screen readers show this.</summary>
    public override string ToString() => Label;

    /// <summary>Formats this build of Magick.NET can actually write (HEIC encoding is not always present).</summary>
    public static IReadOnlyList<FormatInfo> Writable { get; } = All.Where(f => CanWrite(f.Magick)).ToList();

    private static bool CanWrite(MagickFormat format) =>
        MagickNET.SupportedFormats.Any(f => f.Format == format && f.SupportsWriting);
}

/// <summary>DDS block compression. Auto picks DXT1 for opaque images and DXT5 when there is alpha.</summary>
public enum DdsCompression { Auto, Dxt1, Dxt5, Uncompressed }
