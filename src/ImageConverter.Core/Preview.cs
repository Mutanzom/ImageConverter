using ImageMagick;

namespace ImageConverter.Core;

/// <summary>Small previews for formats Explorer has no thumbnail for (DDS, TGA, PSD…).</summary>
public static class Preview
{
    /// <summary>A PNG at most <paramref name="size"/> pixels on its longest side, or null if the file can't be read.</summary>
    public static byte[]? RenderPng(string path, uint size)
    {
        try
        {
            using var image = Converter.Read(path);
            image.AutoOrient();
            image.Thumbnail(new MagickGeometry(size, size) { Greater = true });
            return image.ToByteArray(MagickFormat.Png);
        }
        catch (MagickException)
        {
            return null;
        }
    }
}
