using System.Text;
using ImageMagick;

namespace ImageConverter.Core.Tests;

/// <summary>Checks the DDS header directly, since that is what game engines and GPU loaders read.</summary>
public class DdsTests
{
    private sealed record Header(uint Height, uint Width, uint MipCount, string FourCC);

    private static Header ReadHeader(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.Equal("DDS ", Encoding.ASCII.GetString(bytes, 0, 4));
        // DDS_HEADER starts at 4: size, flags, height, width, pitch, depth, mipMapCount … pixel format at 76.
        uint U(int offset) => BitConverter.ToUInt32(bytes, offset);
        var fourCC = Encoding.ASCII.GetString(bytes, 84, 4).TrimEnd('\0');
        return new Header(U(12), U(16), U(28), fourCC);
    }

    private static string Convert(TempDir dir, string source, DdsCompression compression, bool mipmaps = true)
    {
        var target = dir.File($"out-{compression}-{mipmaps}.dds");
        Converter.Convert(source, target, new ConvertOptions { Format = OutputFormat.Dds, DdsCompression = compression, DdsMipmaps = mipmaps });
        return target;
    }

    [Theory]
    [InlineData(DdsCompression.Dxt1, "DXT1")]
    [InlineData(DdsCompression.Dxt5, "DXT5")]
    [InlineData(DdsCompression.Uncompressed, "")]
    public void WritesRequestedCompression(DdsCompression compression, string fourCC)
    {
        using var dir = new TempDir();
        var source = dir.Image("in.png", 64, 64, alpha: true);

        var header = ReadHeader(Convert(dir, source, compression));

        Assert.Equal(fourCC, header.FourCC);
        Assert.Equal((64u, 64u), (header.Width, header.Height));
    }

    [Fact]
    public void AutoPicksDxt1ForOpaqueAndDxt5ForAlpha()
    {
        using var dir = new TempDir();
        var opaque = dir.Image("opaque.png", 32, 32);
        var alpha = dir.Image("alpha.png", 32, 32, alpha: true);

        Assert.Equal("DXT1", ReadHeader(Convert(dir, opaque, DdsCompression.Auto)).FourCC);
        var target = dir.File("alpha.dds");
        Converter.Convert(alpha, target, new ConvertOptions { Format = OutputFormat.Dds });
        Assert.Equal("DXT5", ReadHeader(target).FourCC);
    }

    [Fact]
    public void MipmapsCanBeTurnedOff()
    {
        using var dir = new TempDir();
        var source = dir.Image("in.png", 64, 64);

        var with = ReadHeader(Convert(dir, source, DdsCompression.Dxt1, mipmaps: true));
        var without = ReadHeader(Convert(dir, source, DdsCompression.Dxt1, mipmaps: false));

        Assert.Equal(7u, with.MipCount); // 64, 32, 16, 8, 4, 2, 1
        Assert.True(without.MipCount <= 1, $"mip count {without.MipCount}");
    }

    [Theory]
    [InlineData(100u, 60u)]
    [InlineData(37u, 13u)]
    public void NonPowerOfTwoSizesRoundTrip(uint width, uint height)
    {
        using var dir = new TempDir();
        var source = dir.Image("in.png", width, height);
        var dds = Convert(dir, source, DdsCompression.Dxt1);

        Converter.Convert(dds, dir.File("back.png"), new ConvertOptions { Format = OutputFormat.Png });

        using var back = new MagickImage(dir.File("back.png"));
        Assert.Equal((width, height), (back.Width, back.Height));
    }

    [Fact]
    public void ReadingDdsWithMipmapsGivesTheFullSizeImage()
    {
        using var dir = new TempDir();
        var dds = Convert(dir, dir.Image("in.png", 128, 64, alpha: true), DdsCompression.Dxt5);

        Converter.Convert(dds, dir.File("back.png"), new ConvertOptions { Format = OutputFormat.Png });

        using var back = new MagickImageCollection(dir.File("back.png"));
        Assert.Single(back);
        Assert.Equal((128u, 64u), (back[0].Width, back[0].Height));
        Assert.True(back[0].HasAlpha);
    }
}
