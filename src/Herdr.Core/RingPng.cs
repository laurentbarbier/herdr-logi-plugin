using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Herdr;

public enum RingStyle { Filled, Outline, Dotted }

public readonly record struct Rgb(byte R, byte G, byte B);

// A dependency-free PNG encoder for the status rings, so the key faces, the
// plugin icon and the README pictures all come from the same pixels.
public static class RingPng
{
    private const int Dots = 10;

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var c = 0xffffffff;
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xff] ^ (c >> 8);
        return c ^ 0xffffffff;
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(word, (uint)data.Length);
        output.Write(word);
        var body = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, body);
        data.CopyTo(body.AsSpan(4));
        output.Write(body);
        BinaryPrimitives.WriteUInt32BigEndian(word, Crc32(body));
        output.Write(word);
    }

    // Encodes non-interlaced 8-bit RGBA pixels, `width * 4` bytes per row.
    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba, CompressionLevel level = CompressionLevel.Optimal)
    {
        var stride = width * 4;
        var raw = new byte[(stride + 1) * height];
        for (var y = 0; y < height; y++) rgba.Slice(y * stride, stride).CopyTo(raw.AsSpan(y * (stride + 1) + 1));

        using var idat = new MemoryStream();
        using (var zlib = new ZLibStream(idat, level, leaveOpen: true)) zlib.Write(raw);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // RGBA

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
        WriteChunk(png, "IHDR", ihdr);
        WriteChunk(png, "IDAT", idat.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    // Whether (x, y) falls on a ring of radius r and the given stroke around the
    // centre (cx, cy).
    public static bool Covers(RingStyle style, double cx, double cy, double r, double stroke, double x, double y)
    {
        var dx = x - cx;
        var dy = y - cy;
        var d = Math.Sqrt(dx * dx + dy * dy);
        if (style == RingStyle.Filled) return d <= r;
        var onRing = Math.Abs(d - r) <= stroke / 2;
        if (style == RingStyle.Outline) return onRing;
        // Each dot covers the first half of its slice of the circumference.
        var phase = (Math.Atan2(dy, dx) / (2 * Math.PI) * Dots + Dots) % 1;
        return onRing && phase < 0.5;
    }

    public static byte[] Circle(int size, Rgb rgb, RingStyle style)
    {
        var rgba = new byte[size * size * 4];
        double c = size / 2.0, r = size * 0.35, stroke = size * 0.08;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var o = (y * size + x) * 4;
                rgba[o] = rgb.R;
                rgba[o + 1] = rgb.G;
                rgba[o + 2] = rgb.B;
                rgba[o + 3] = Covers(style, c, c, r, stroke, x + 0.5, y + 0.5) ? (byte)255 : (byte)0;
            }
        }
        return Encode(size, size, rgba);
    }
}
