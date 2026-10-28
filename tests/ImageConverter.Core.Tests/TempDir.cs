using ImageMagick;

namespace ImageConverter.Core.Tests;

/// <summary>A scratch folder per test, deleted afterwards.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ImageConverterTests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    /// <summary>Writes a test image: a colour gradient, optionally with a transparent left half.</summary>
    public string Image(string name, uint width = 64, uint height = 48, bool alpha = false, MagickFormat? format = null)
    {
        var path = File(name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var image = new MagickImage("gradient:red-blue", width, height);
        if (alpha)
        {
            image.Alpha(AlphaAction.Set);
            using var pixels = image.GetPixels();
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width / 2; x++)
                {
                    var p = pixels.GetPixel(x, y);
                    p.SetChannel(3, 0);
                    pixels.SetPixel(p);
                }
        }
        if (format is { } f) image.Write(path, f);
        else image.Write(path);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}
