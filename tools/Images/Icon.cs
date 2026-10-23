using System.IO.Compression;

namespace Herdr.Images;

// Renders package/metadata/Icon256x256.png: a 3x3 grid of status rings in the
// same visual language as the live tiles. Logitech requires a 256x256 PNG whose
// graphic fits inside a centred 192x192 area with transparent padding, and it
// must read on both black and white backgrounds.
static class Icon
{
    private const int Size = 256;
    private const int Pitch = 64;
    private const double Radius = 21;
    private const double Stroke = 7;
    private const int Supersample = 4;

    private static readonly Rgb Blocked = new(230, 40, 40);
    private static readonly Rgb Working = new(240, 180, 0);
    private static readonly Rgb Done = new(0, 200, 80);
    private static readonly Rgb Idle = new(0, 150, 60);
    private static readonly Rgb None = new(128, 128, 128);

    // Row by row, top to bottom.
    private static readonly (Rgb Rgb, RingStyle Style)[] Grid =
    [
        (Idle, RingStyle.Outline), (Working, RingStyle.Filled), (Idle, RingStyle.Outline),
        (Blocked, RingStyle.Filled), (Done, RingStyle.Filled), (None, RingStyle.Dotted),
        (Idle, RingStyle.Outline), (None, RingStyle.Dotted), (Working, RingStyle.Filled),
    ];

    public static void Write(string root)
    {
        var circles = Grid.Select((ring, i) => (
            Cx: Size / 2.0 + (i % 3 - 1) * Pitch,
            Cy: Size / 2.0 + (i / 3 - 1) * Pitch,
            ring.Rgb,
            ring.Style)).ToList();

        var rgba = new byte[Size * Size * 4];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var hits = 0;
                var rgb = new Rgb(0, 0, 0);
                for (var sy = 0; sy < Supersample; sy++)
                {
                    for (var sx = 0; sx < Supersample; sx++)
                    {
                        var px = x + (sx + 0.5) / Supersample;
                        var py = y + (sy + 0.5) / Supersample;
                        foreach (var c in circles)
                        {
                            if (!RingPng.Covers(c.Style, c.Cx, c.Cy, Radius, Stroke, px, py)) continue;
                            hits++;
                            rgb = c.Rgb;
                            break;
                        }
                    }
                }
                var o = (y * Size + x) * 4;
                rgba[o] = rgb.R;
                rgba[o + 1] = rgb.G;
                rgba[o + 2] = rgb.B;
                rgba[o + 3] = (byte)Math.Round(255.0 * hits / (Supersample * Supersample));
            }
        }

        var png = RingPng.Encode(Size, Size, rgba, CompressionLevel.SmallestSize);
        var output = Path.Combine(root, "package/metadata/Icon256x256.png");
        File.WriteAllBytes(output, png);
        Console.WriteLine($"wrote {Path.GetRelativePath(root, output)} ({png.Length} bytes)");
    }
}
