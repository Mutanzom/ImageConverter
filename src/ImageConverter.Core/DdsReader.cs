using System.Buffers.Binary;
using System.Numerics;
using ImageMagick;

namespace ImageConverter.Core;

/// <summary>
/// Reads uncompressed 24/32-bit DDS files using the channel layout the header declares.
/// ImageMagick assumes BGRA for these and ignores the masks, so RGBA-ordered files (common for
/// textures dumped from Direct3D 11/12 games) come out with red and blue swapped.
/// Compressed (DXT/BC) and other layouts return null and are left to ImageMagick, which reads them fine.
/// </summary>
public static class DdsReader
{
    private const int HeaderSize = 128;     // "DDS " + DDS_HEADER
    private const int Dx10HeaderSize = 20;  // DDS_HEADER_DXT10, present when FourCC is "DX10"

    private const uint FlagAlphaPixels = 0x1, FlagFourCC = 0x4, FlagRgb = 0x40;

    private readonly record struct Layout(int BytesPerPixel, uint R, uint G, uint B, uint A);

    public static MagickImage? TryRead(string path)
    {
        using var stream = File.OpenRead(path);
        var header = new byte[HeaderSize + Dx10HeaderSize];
        int got = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        if (got < HeaderSize || header[0] != 'D' || header[1] != 'D' || header[2] != 'S' || header[3] != ' ') return null;

        uint U(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(offset, 4));
        uint height = U(12), width = U(16);
        if (width == 0 || height == 0 || width > 32768 || height > 32768) return null;

        var (layout, dataOffset) = ReadLayout(header, got, U);
        if (layout is not { } l) return null;

        // The top mip level comes first, rows tightly packed.
        long size = (long)width * height * l.BytesPerPixel;
        if (stream.Length < dataOffset + size) return null;
        var data = new byte[size];
        stream.Position = dataOffset;
        stream.ReadExactly(data);

        var rgba = ToRgba(data, width * height, l);
        var image = new MagickImage();
        image.ReadPixels(rgba, new PixelReadSettings(width, height, StorageType.Char, PixelMapping.RGBA));
        if (l.A == 0) image.Alpha(AlphaAction.Off);
        return image;
    }

    private static (Layout?, int DataOffset) ReadLayout(byte[] header, int got, Func<int, uint> u)
    {
        uint flags = u(80);
        if ((flags & FlagFourCC) != 0)
        {
            if (u(84) != 0x30315844 || got < HeaderSize + Dx10HeaderSize) return (null, 0); // "DX10"
            // DXGI_FORMAT values for the plain 8-bit formats; everything else (BC*, float…) goes to ImageMagick.
            Layout? dx10 = u(128) switch
            {
                28 or 29 => new Layout(4, 0x000000FF, 0x0000FF00, 0x00FF0000, 0xFF000000), // R8G8B8A8 (UNORM / SRGB)
                87 or 91 => new Layout(4, 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000), // B8G8R8A8
                88 or 93 => new Layout(4, 0x00FF0000, 0x0000FF00, 0x000000FF, 0),          // B8G8R8X8
                _ => null,
            };
            return (dx10, HeaderSize + Dx10HeaderSize);
        }

        if ((flags & FlagRgb) == 0) return (null, 0); // luminance, YUV…
        uint bits = u(88);
        if (bits is not (24 or 32)) return (null, 0);
        uint r = u(92), g = u(96), b = u(100), a = (flags & FlagAlphaPixels) != 0 ? u(104) : 0;
        // Only 8-bit channels; 10:10:10:2 and similar stay with ImageMagick.
        foreach (var mask in new[] { r, g, b })
            if (BitOperations.PopCount(mask) != 8) return (null, 0);
        if (a != 0 && BitOperations.PopCount(a) != 8) return (null, 0);
        return (new Layout((int)bits / 8, r, g, b, a), HeaderSize);
    }

    private static byte[] ToRgba(byte[] data, long pixels, Layout l)
    {
        int rs = BitOperations.TrailingZeroCount(l.R), gs = BitOperations.TrailingZeroCount(l.G),
            bs = BitOperations.TrailingZeroCount(l.B), as_ = l.A == 0 ? 0 : BitOperations.TrailingZeroCount(l.A);
        var rgba = new byte[pixels * 4];
        for (long i = 0, src = 0, dst = 0; i < pixels; i++, src += l.BytesPerPixel, dst += 4)
        {
            uint v = data[src] | (uint)data[src + 1] << 8 | (uint)data[src + 2] << 16;
            if (l.BytesPerPixel == 4) v |= (uint)data[src + 3] << 24;
            rgba[dst] = (byte)((v & l.R) >> rs);
            rgba[dst + 1] = (byte)((v & l.G) >> gs);
            rgba[dst + 2] = (byte)((v & l.B) >> bs);
            rgba[dst + 3] = l.A == 0 ? (byte)255 : (byte)((v & l.A) >> as_);
        }
        return rgba;
    }
}
