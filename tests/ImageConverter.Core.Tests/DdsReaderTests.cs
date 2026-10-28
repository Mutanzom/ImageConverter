using System.Buffers.Binary;
using ImageMagick;

namespace ImageConverter.Core.Tests;

/// <summary>Hand-built uncompressed DDS files, so the byte order in the file is known exactly.</summary>
public class DdsReaderTests
{
    // Two pixels per test file: tan straw at half alpha, then opaque pure red.
    private static readonly (byte R, byte G, byte B, byte A)[] Pixels = [(0xC8, 0xAA, 0x79, 0x80), (0xFF, 0x00, 0x00, 0xFF)];

    private const uint RgbaR = 0x000000FF, RgbaG = 0x0000FF00, RgbaB = 0x00FF0000, RgbaA = 0xFF000000;

    /// <summary>Legacy header with the given masks; pixels are packed according to them.</summary>
    private static byte[] Legacy(int bits, uint r, uint g, uint b, uint a)
    {
        var bpp = bits / 8;
        var file = new byte[128 + Pixels.Length * bpp];
        Header(file, flags: 0x40 | (a != 0 ? 0x1u : 0), fourCC: 0, bits, r, g, b, a);
        for (int i = 0; i < Pixels.Length; i++)
        {
            var (pr, pg, pb, pa) = Pixels[i];
            uint v = Place(pr, r) | Place(pg, g) | Place(pb, b) | Place(pa, a);
            for (int k = 0; k < bpp; k++) file[128 + i * bpp + k] = (byte)(v >> (8 * k));
        }
        return file;
    }

    private static byte[] Dx10(uint dxgiFormat, bool bgra)
    {
        var file = new byte[148 + Pixels.Length * 4];
        Header(file, flags: 0x4, fourCC: 0x30315844, 0, 0, 0, 0, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(128), dxgiFormat);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(132), 3); // TEXTURE2D
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(140), 1); // array size
        for (int i = 0; i < Pixels.Length; i++)
        {
            var (r, g, b, a) = Pixels[i];
            var px = bgra ? new[] { b, g, r, a } : new[] { r, g, b, a };
            px.CopyTo(file, 148 + i * 4);
        }
        return file;
    }

    private static uint Place(byte value, uint mask) => mask == 0 ? 0 : (uint)value << System.Numerics.BitOperations.TrailingZeroCount(mask);

    private static void Header(byte[] file, uint flags, uint fourCC, int bits, uint r, uint g, uint b, uint a)
    {
        void W(int offset, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(offset), v);
        "DDS "u8.CopyTo(file);
        W(4, 124);
        W(8, 0x1 | 0x2 | 0x4 | 0x1000); // caps, height, width, pixel format
        W(12, 1);                       // height
        W(16, (uint)Pixels.Length);     // width
        W(76, 32);
        W(80, flags);
        W(84, fourCC);
        W(88, (uint)bits);
        W(92, r); W(96, g); W(100, b); W(104, a);
        W(108, 0x1000);
    }

    private static List<(byte R, byte G, byte B, byte A)> ConvertAndRead(byte[] dds)
    {
        using var dir = new TempDir();
        var source = dir.File("in.dds");
        File.WriteAllBytes(source, dds);
        Converter.Convert(source, dir.File("out.png"), new ConvertOptions { Format = OutputFormat.Png });
        using var image = new MagickImage(dir.File("out.png"));
        Assert.Equal(((uint)Pixels.Length, 1u), (image.Width, image.Height));
        var pixels = image.GetPixels();
        return Enumerable.Range(0, Pixels.Length).Select(x =>
        {
            var c = pixels.GetPixel(x, 0).ToColor()!;
            return ((byte)c.R, (byte)c.G, (byte)c.B, image.HasAlpha ? (byte)c.A : (byte)255);
        }).ToList();
    }

    [Fact]
    public void RgbaOrderedLegacyDdsKeepsItsColours()
    {
        // The layout of textures dumped from KCD2: R in the low byte. ImageMagick alone turns these blue.
        Assert.Equal(Pixels, ConvertAndRead(Legacy(32, RgbaR, RgbaG, RgbaB, RgbaA)));
    }

    [Fact]
    public void BgraOrderedLegacyDdsKeepsItsColours()
    {
        Assert.Equal(Pixels, ConvertAndRead(Legacy(32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000)));
    }

    [Fact]
    public void TwentyFourBitDdsIsOpaque()
    {
        var expected = Pixels.Select(p => (p.R, p.G, p.B, (byte)255)).ToList();
        Assert.Equal(expected, ConvertAndRead(Legacy(24, 0x00FF0000, 0x0000FF00, 0x000000FF, 0)));
    }

    [Theory]
    [InlineData(28u, false)] // R8G8B8A8_UNORM
    [InlineData(29u, false)] // R8G8B8A8_UNORM_SRGB
    [InlineData(87u, true)]  // B8G8R8A8_UNORM
    public void Dx10HeaderFormats(uint dxgi, bool bgra)
    {
        Assert.Equal(Pixels, ConvertAndRead(Dx10(dxgi, bgra)));
    }

    [Fact]
    public void CompressedDdsIsLeftToImageMagick()
    {
        using var dir = new TempDir();
        var source = dir.Image("in.png", 32, 32);
        Converter.Convert(source, dir.File("in.dds"), new ConvertOptions { Format = OutputFormat.Dds, DdsCompression = DdsCompression.Dxt1 });

        Assert.Null(DdsReader.TryRead(dir.File("in.dds")));
        using var image = Converter.Read(dir.File("in.dds"));
        Assert.Equal(32u, image.Width);
    }
}
