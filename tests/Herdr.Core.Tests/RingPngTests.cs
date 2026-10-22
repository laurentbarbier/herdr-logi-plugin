using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Herdr.Tests;

public class RingPngTests
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    // Bitwise, unlike the encoder's table, so the two cannot share a mistake.
    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var c = 0xffffffff;
        foreach (var b in data)
        {
            c ^= b;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320 ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xffffffff;
    }

    private static List<(string Type, byte[] Data)> Chunks(byte[] png)
    {
        var chunks = new List<(string, byte[])>();
        for (var offset = Signature.Length; offset < png.Length;)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
            var type = Encoding.ASCII.GetString(png, offset + 4, 4);
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length));
            Assert.Equal(Crc32(png.AsSpan(offset + 4, 4 + length)), crc);
            chunks.Add((type, png.AsSpan(offset + 8, length).ToArray()));
            offset += 12 + length;
        }
        return chunks;
    }

    private static Func<int, int, byte[]> Pixels(byte[] png, int size)
    {
        var idat = Chunks(png).First(c => c.Type == "IDAT").Data;
        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(new MemoryStream(idat), CompressionMode.Decompress)) zlib.CopyTo(inflated);
        var raw = inflated.ToArray();
        var stride = size * 4 + 1;
        Assert.Equal(stride * size, raw.Length);
        return (x, y) =>
        {
            Assert.Equal(0, raw[y * stride]); // scanline filter byte
            var o = y * stride + 1 + x * 4;
            return raw[o..(o + 4)];
        };
    }

    [Fact]
    public void EncodesAWellFormedRgbaPng()
    {
        var png = RingPng.Circle(80, new Rgb(1, 2, 3), RingStyle.Filled);
        Assert.Equal(Signature, png[..8]);
        var chunks = Chunks(png);
        Assert.Equal(["IHDR", "IDAT", "IEND"], chunks.Select(c => c.Type));
        var ihdr = chunks[0].Data;
        Assert.Equal(80u, BinaryPrimitives.ReadUInt32BigEndian(ihdr));
        Assert.Equal(80u, BinaryPrimitives.ReadUInt32BigEndian(ihdr.AsSpan(4)));
        Assert.Equal(8, ihdr[8]); // bit depth
        Assert.Equal(6, ihdr[9]); // RGBA colour type
    }

    [Fact]
    public void FilledCircleCoversTheCentreAndLeavesTheCornersTransparent()
    {
        var at = Pixels(RingPng.Circle(80, new Rgb(230, 40, 40), RingStyle.Filled), 80);
        Assert.Equal([230, 40, 40, 255], at(40, 40));
        Assert.Equal(0, at(0, 0)[3]);
        Assert.Equal(0, at(79, 79)[3]);
    }

    [Fact]
    public void OutlineCircleIsHollow()
    {
        var at = Pixels(RingPng.Circle(80, new Rgb(0, 120, 48), RingStyle.Outline), 80);
        Assert.Equal(0, at(40, 40)[3]); // centre is transparent
        Assert.Equal(255, at(40, 12)[3]); // ring is opaque at the top
    }

    [Fact]
    public void DottedCircleHasGapsAlongTheRing()
    {
        var at = Pixels(RingPng.Circle(80, new Rgb(70, 70, 70), RingStyle.Dotted), 80);
        var ring = Enumerable.Range(0, 180)
            .Select(i => i * 2 * Math.PI / 180)
            .Select(a => at((int)Math.Round(40 + 28 * Math.Cos(a)), (int)Math.Round(40 + 28 * Math.Sin(a)))[3])
            .ToList();
        Assert.Contains((byte)255, ring); // some of the ring is drawn
        Assert.Contains((byte)0, ring); // some of the ring is a gap
    }
}
