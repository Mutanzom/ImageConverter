using ImageMagick;

namespace ImageConverter.Core.Tests;

public class ConverterTests
{
    public static TheoryData<OutputFormat> WritableFormats()
    {
        var data = new TheoryData<OutputFormat>();
        foreach (var f in FormatInfo.Writable) data.Add(f.Format);
        return data;
    }

    [Fact]
    public void KeyFormatsAreWritable()
    {
        var writable = FormatInfo.Writable.Select(f => f.Format).ToHashSet();
        foreach (var f in new[] { OutputFormat.Png, OutputFormat.Jpeg, OutputFormat.WebP, OutputFormat.Avif, OutputFormat.Tiff,
                     OutputFormat.Bmp, OutputFormat.Gif, OutputFormat.Ico, OutputFormat.Dds })
            Assert.Contains(f, writable);
    }

    [Theory]
    [MemberData(nameof(WritableFormats))]
    public void ConvertsToEveryFormat(OutputFormat format)
    {
        using var dir = new TempDir();
        var source = dir.Image("in.png", 64, 48, alpha: true);
        var options = new ConvertOptions { Format = format };
        var target = dir.File("out" + options.Info.Extension);

        var bytes = Converter.Convert(source, target, options);

        Assert.True(bytes > 0);
        Assert.Equal(bytes, new FileInfo(target).Length);
        if (format == OutputFormat.Ico)
        {
            using var frames = new MagickImageCollection(target);
            Assert.Equal(256u, frames.Max(f => f.Width));
        }
        else
        {
            using var result = new MagickImage(target);
            Assert.Equal(64u, result.Width);
            Assert.Equal(48u, result.Height);
        }
        Assert.Single(Directory.GetFiles(dir.Path, "*.partial", SearchOption.AllDirectories).Concat([target]).Distinct());
    }

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".webp")]
    [InlineData(".tga")]
    [InlineData(".dds")]
    [InlineData(".avif")]
    [InlineData(".tif")]
    [InlineData(".bmp")]
    public void ReadsCommonInputs(string ext)
    {
        using var dir = new TempDir();
        var source = dir.Image("in" + ext, 40, 30);
        var target = dir.File("out.png");

        Converter.Convert(source, target, new ConvertOptions { Format = OutputFormat.Png });

        using var result = new MagickImage(target);
        Assert.Equal(40u, result.Width);
        Assert.Equal(30u, result.Height);
    }

    [Fact]
    public void TransparencyIsFlattenedOntoBackgroundForJpeg()
    {
        using var dir = new TempDir();
        var source = dir.Image("in.png", 20, 20, alpha: true);
        var target = dir.File("out.jpg");

        Converter.Convert(source, target, new ConvertOptions { Format = OutputFormat.Jpeg, Background = "#00FF00", Quality = 100 });

        using var result = new MagickImage(target);
        Assert.False(result.HasAlpha);
        var color = result.GetPixels().GetPixel(2, 10).ToColor()!;
        Assert.True(color.R < 30 && color.G > 225 && color.B < 30, $"expected green, got {color}");
    }

    [Fact]
    public void FitResizeKeepsAspectAndDoesNotUpscale()
    {
        using var dir = new TempDir();
        var big = dir.Image("big.png", 400, 200);
        var small = dir.Image("small.png", 50, 40);
        var options = new ConvertOptions { Format = OutputFormat.Png, Resize = ResizeMode.Fit, MaxWidth = 100, MaxHeight = 100 };

        Converter.Convert(big, dir.File("big-out.png"), options);
        Converter.Convert(small, dir.File("small-out.png"), options);

        using (var r = new MagickImage(dir.File("big-out.png"))) Assert.Equal((100u, 50u), (r.Width, r.Height));
        using (var r = new MagickImage(dir.File("small-out.png"))) Assert.Equal((50u, 40u), (r.Width, r.Height));
    }

    [Fact]
    public void FitWithOneSideZeroLimitsOnlyTheOther()
    {
        using var dir = new TempDir();
        var source = dir.Image("in.png", 400, 200);

        Converter.Convert(source, dir.File("out.png"), new ConvertOptions
        {
            Format = OutputFormat.Png, Resize = ResizeMode.Fit, MaxWidth = 0, MaxHeight = 50,
        });

        using var r = new MagickImage(dir.File("out.png"));
        Assert.Equal((100u, 50u), (r.Width, r.Height));
    }

    [Fact]
    public void PercentResize()
    {
        using var dir = new TempDir();
        var source = dir.Image("in.png", 200, 100);

        Converter.Convert(source, dir.File("out.png"), new ConvertOptions { Format = OutputFormat.Png, Resize = ResizeMode.Percent, Percent = 25 });

        using var r = new MagickImage(dir.File("out.png"));
        Assert.Equal((50u, 25u), (r.Width, r.Height));
    }

    [Fact]
    public void ExifOrientationIsApplied()
    {
        using var dir = new TempDir();
        var source = dir.File("rotated.jpg");
        using (var image = new MagickImage("gradient:red-blue", 40, 20))
        {
            // Camera held sideways: viewers rotate it 90° for display.
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)Orientation.RightTop);
            image.SetProfile(exif);
            image.Orientation = Orientation.RightTop;
            image.Write(source, MagickFormat.Jpeg);
        }
        using (var check = new MagickImage(source))
            Assert.Equal(Orientation.RightTop, check.Orientation);

        Converter.Convert(source, dir.File("out.png"), new ConvertOptions { Format = OutputFormat.Png });

        using var r = new MagickImage(dir.File("out.png"));
        Assert.Equal((20u, 40u), (r.Width, r.Height));
    }

    [Fact]
    public void StripMetadataRemovesExifButKeepsColorProfile()
    {
        using var dir = new TempDir();
        var source = dir.File("in.jpg");
        using (var image = new MagickImage("gradient:red-blue", 40, 20))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Make, "TestCam");
            image.SetProfile(exif);
            image.SetProfile(ColorProfiles.SRGB);
            image.Write(source, MagickFormat.Jpeg);
        }

        Converter.Convert(source, dir.File("keep.jpg"), new ConvertOptions { Format = OutputFormat.Jpeg, KeepMetadata = true });
        Converter.Convert(source, dir.File("strip.jpg"), new ConvertOptions { Format = OutputFormat.Jpeg, KeepMetadata = false });

        using (var kept = new MagickImage(dir.File("keep.jpg")))
            Assert.Equal("TestCam", kept.GetExifProfile()?.GetValue(ExifTag.Make)?.Value);
        using (var stripped = new MagickImage(dir.File("strip.jpg")))
        {
            Assert.Null(stripped.GetExifProfile());
            Assert.NotNull(stripped.GetColorProfile());
        }
    }

    [Fact]
    public void AnimatedGifUsesFirstFrame()
    {
        using var dir = new TempDir();
        var source = dir.File("anim.gif");
        using (var frames = new MagickImageCollection())
        {
            frames.Add(new MagickImage(MagickColors.Red, 10, 10));
            frames.Add(new MagickImage(MagickColors.Blue, 10, 10));
            frames.Write(source);
        }

        Converter.Convert(source, dir.File("out.png"), new ConvertOptions { Format = OutputFormat.Png });

        using var result = new MagickImageCollection(dir.File("out.png"));
        Assert.Single(result);
        var c = result[0].GetPixels().GetPixel(5, 5).ToColor()!;
        Assert.True(c.R > 200 && c.B < 50, $"expected red, got {c}");
    }

    [Fact]
    public void IcoInputUsesLargestSize()
    {
        using var dir = new TempDir();
        var source = dir.Image("in.png", 300, 300);
        Converter.Convert(source, dir.File("icon.ico"), new ConvertOptions { Format = OutputFormat.Ico });

        Converter.Convert(dir.File("icon.ico"), dir.File("out.png"), new ConvertOptions { Format = OutputFormat.Png });

        using var r = new MagickImage(dir.File("out.png"));
        Assert.Equal(256u, r.Width);
    }

    [Fact]
    public void IcoFromNonSquareImageIsPaddedNotStretched()
    {
        using var dir = new TempDir();
        var source = dir.Image("wide.png", 200, 100);

        Converter.Convert(source, dir.File("out.ico"), new ConvertOptions { Format = OutputFormat.Ico });

        using var frames = new MagickImageCollection(dir.File("out.ico"));
        Assert.Equal(new uint[] { 16, 32, 48, 256 }, frames.Select(f => f.Width).Order().ToArray());
        Assert.All(frames, f => Assert.Equal(f.Width, f.Height));
        var big = frames.First(f => f.Width == 256);
        Assert.Equal(0, big.GetPixels().GetPixel(128, 2).ToColor()!.A); // padding above the image is transparent
    }

    [Fact]
    public void FailedConversionLeavesNoFiles()
    {
        using var dir = new TempDir();
        var source = dir.File("broken.png");
        File.WriteAllText(source, "not an image");
        var target = dir.File("out", "x.webp");

        Assert.ThrowsAny<MagickException>(() => Converter.Convert(source, target, new ConvertOptions()));

        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.partial", SearchOption.AllDirectories));
    }
}

internal static class TempDirExtensions
{
    public static string File(this TempDir dir, string folder, string name) => Path.Combine(dir.Path, folder, name);
}
