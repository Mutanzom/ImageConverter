using ImageMagick;

namespace ImageConverter.Core.Tests;

public class GifBuilderTests
{
    private static string Solid(TempDir dir, string name, MagickColor color, uint w, uint h)
    {
        var path = dir.File(name);
        using var image = new MagickImage(color, w, h);
        image.Write(path);
        return path;
    }

    private static (byte R, byte G, byte B) Center(IMagickImage<byte> frame)
    {
        var c = frame.GetPixels().GetPixel((int)frame.Width / 2, (int)frame.Height / 2).ToColor()!;
        return (c.R, c.G, c.B);
    }

    [Fact]
    public void FramesKeepOrderDelayAndLoopCount()
    {
        using var dir = new TempDir();
        var sources = new[]
        {
            Solid(dir, "1.png", MagickColors.Red, 40, 30),
            Solid(dir, "2.png", MagickColors.Lime, 40, 30),
            Solid(dir, "3.png", MagickColors.Blue, 40, 30),
        };
        var target = dir.File("anim.gif");

        GifBuilder.Create(sources, target, new GifOptions { FrameDelayMs = 250, Loops = 3, MaxSize = 0 }, "#FFFFFF");

        using var gif = new MagickImageCollection(target);
        Assert.Equal(3, gif.Count);
        Assert.All(gif, f => Assert.Equal((40u, 30u), (f.Width, f.Height)));
        Assert.All(gif, f => Assert.Equal(25u, f.AnimationDelay));
        Assert.Equal(3u, gif[0].AnimationIterations);
        Assert.True(Center(gif[0]).R > 200 && Center(gif[1]).G > 200 && Center(gif[2]).B > 200);
    }

    [Fact]
    public void PingPongPlaysBackWithoutRepeatingTheEnds()
    {
        using var dir = new TempDir();
        var sources = new[] { "1", "2", "3", "4" }.Select(n => Solid(dir, n + ".png", MagickColors.Gray, 10, 10)).ToArray();

        GifBuilder.Create(sources, dir.File("a.gif"), new GifOptions { PingPong = true, MaxSize = 0 }, "#FFFFFF");

        using var gif = new MagickImageCollection(dir.File("a.gif"));
        Assert.Equal(6, gif.Count); // 1 2 3 4 3 2
    }

    [Fact]
    public void LaterImagesAreFittedIntoTheFirstImagesCanvas()
    {
        using var dir = new TempDir();
        var sources = new[]
        {
            dir.Image("wide.png", 1600, 800),
            Solid(dir, "tall.png", MagickColors.Red, 100, 400),
        };

        GifBuilder.Create(sources, dir.File("a.gif"), new GifOptions { MaxSize = 400, KeepTransparency = false }, "#00FF00");

        using var gif = new MagickImageCollection(dir.File("a.gif"));
        Assert.All(gif, f => Assert.Equal((400u, 200u), (f.Width, f.Height)));
        // The tall red image is centred; the sides are the background colour.
        Assert.True(Center(gif[1]).R > 200);
        var side = gif[1].GetPixels().GetPixel(5, 100).ToColor()!;
        Assert.True(side.G > 200 && side.R < 50, $"expected green padding, got {side}");
    }

    [Fact]
    public void ReadsDdsFrames()
    {
        using var dir = new TempDir();
        var png = dir.Image("in.png", 64, 64);
        Converter.Convert(png, dir.File("in.dds"), new ConvertOptions { Format = OutputFormat.Dds });

        GifBuilder.Create([dir.File("in.dds"), png], dir.File("a.gif"), new GifOptions(), "#FFFFFF");

        using var gif = new MagickImageCollection(dir.File("a.gif"));
        Assert.Equal(2, gif.Count);
    }

    [Fact]
    public void CancelledRunWritesNothing()
    {
        using var dir = new TempDir();
        var sources = new[] { dir.Image("1.png"), dir.Image("2.png") };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            GifBuilder.Create(sources, dir.File("a.gif"), new GifOptions(), "#FFFFFF", ct: cts.Token));

        Assert.False(File.Exists(dir.File("a.gif")));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.partial"));
    }

    [Fact]
    public void SuggestedNameIsNumberedWhenTaken()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\p\animation.gif" };
        Assert.Equal(@"C:\p\animation (2).gif", GifBuilder.SuggestTarget(@"C:\p\a.png", taken.Contains));
    }
}
