using ImageMagick;

namespace ImageConverter.Core;

/// <summary>Converts one file. Thread-safe: every call works on its own image.</summary>
public static class Converter
{
    private static readonly string[] IconSizes = ["256", "48", "32", "16"];

    /// <summary>
    /// Reads <paramref name="source"/>, applies the options and writes <paramref name="target"/>.
    /// The file is written next to the target under a temporary name and moved into place at the
    /// end, so a failed or cancelled conversion never leaves a half-written image behind.
    /// Returns the size of the written file.
    /// </summary>
    public static long Convert(string source, string target, ConvertOptions options, CancellationToken ct = default)
    {
        var info = options.Info;
        using var image = Read(source);
        ct.ThrowIfCancellationRequested();

        image.AutoOrient();
        Resize(image, options);
        if (!options.KeepMetadata) StripMetadata(image);
        if (!info.HasAlpha && image.HasAlpha)
        {
            image.BackgroundColor = ParseColor(options.Background);
            image.Alpha(AlphaAction.Remove);
        }
        ApplyFormatSettings(image, options);
        ct.ThrowIfCancellationRequested();

        var folder = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.partial");
        try
        {
            image.Write(temp, info.Magick);
            ct.ThrowIfCancellationRequested();
            File.Move(temp, target, overwrite: options.Conflict == ConflictMode.Overwrite);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        return new FileInfo(target).Length;
    }

    /// <summary>Opens an image the way conversions and previews both need it (largest icon size, first frame).</summary>
    public static MagickImage Read(string source)
    {
        var ext = Path.GetExtension(source);
        if (ext.Equals(".dds", StringComparison.OrdinalIgnoreCase) && DdsReader.TryRead(source) is { } dds)
            return dds;
        if (ext.Equals(".ico", StringComparison.OrdinalIgnoreCase) || ext.Equals(".cur", StringComparison.OrdinalIgnoreCase))
        {
            // Icons hold several sizes; the first is often the smallest.
            using var all = new MagickImageCollection(source);
            return (MagickImage)all.OrderByDescending(i => (long)i.Width * i.Height).First().Clone();
        }
        // Animated GIF/WebP and layered files: the first frame only.
        return new MagickImage(source, new MagickReadSettings { FrameIndex = 0, FrameCount = 1 });
    }

    private static void Resize(MagickImage image, ConvertOptions options)
    {
        switch (options.Resize)
        {
            case ResizeMode.Fit:
                uint w = (uint)Math.Max(0, options.MaxWidth), h = (uint)Math.Max(0, options.MaxHeight);
                if (w == 0 && h == 0) return;
                // 0 on one side means "only limit the other": ImageMagick keeps the aspect ratio.
                image.Resize(new MagickGeometry(w, h) { Greater = !options.AllowUpscale });
                break;
            case ResizeMode.Percent:
                if (options.Percent <= 0 || options.Percent == 100) return;
                if (options.Percent > 100 && !options.AllowUpscale) return;
                image.Resize(new Percentage(options.Percent));
                break;
        }
    }

    /// <summary>Removes EXIF, XMP, IPTC and comments, but keeps the colour profile so colours don't shift.</summary>
    private static void StripMetadata(MagickImage image)
    {
        var icc = image.GetColorProfile();
        image.Strip();
        if (icc != null) image.SetProfile(icc);
    }

    private static void ApplyFormatSettings(MagickImage image, ConvertOptions options)
    {
        var info = options.Info;
        if (info.HasQuality) image.Quality = (uint)Math.Clamp(options.Quality, 1, 100);

        switch (options.Format)
        {
            case OutputFormat.WebP when options.Lossless:
                image.Settings.SetDefine(MagickFormat.WebP, "lossless", true);
                break;

            case OutputFormat.Ico:
                // Square it up on a transparent canvas first; auto-resize would otherwise stretch it.
                if (image.Width != image.Height)
                {
                    var side = Math.Max(image.Width, image.Height);
                    image.BackgroundColor = MagickColors.Transparent;
                    image.Extent(side, side, Gravity.Center);
                }
                image.Settings.SetDefine(MagickFormat.Icon, "auto-resize", string.Join(",", IconSizes));
                break;

            case OutputFormat.Dds:
                var compression = options.DdsCompression switch
                {
                    DdsCompression.Dxt1 => "dxt1",
                    DdsCompression.Dxt5 => "dxt5",
                    DdsCompression.Uncompressed => "none",
                    _ => image.HasAlpha ? "dxt5" : "dxt1",
                };
                image.Settings.SetDefine(MagickFormat.Dds, "compression", compression);
                if (!options.DdsMipmaps) image.Settings.SetDefine(MagickFormat.Dds, "mipmaps", "0");
                break;
        }
    }

    public static MagickColor ParseColor(string? text)
    {
        try { return new MagickColor(string.IsNullOrWhiteSpace(text) ? "#FFFFFF" : text.Trim()); }
        catch (MagickException) { return MagickColors.White; }
        catch (ArgumentException) { return MagickColors.White; }
    }
}
