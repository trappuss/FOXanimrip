// SPDX-License-Identifier: MIT
namespace FoxAnimRip.Preview;

/// <summary>
/// The shading sphere, generated rather than shipped.
///
/// A matcap is a picture of a lit sphere: look up a pixel by the surface's
/// view-space normal and the surface takes on that sphere's lighting. It costs
/// one texture fetch, needs no light setup, no tangents and no texture maps, and
/// it reads form far better than flat shading -- which is exactly the trade for
/// something whose only job is to let you see whether an arm is bending the way
/// it should.
///
/// Building it in code keeps the tool a single file with nothing beside it, and
/// costs about a millisecond once.
/// </summary>
public static class Matcap
{
    public const int Size = 256;

    private static int[] _clay;
    private static int[] _grey;

    /// <summary>A warm clay sphere: neutral, matte, reads shape well.</summary>
    public static int[] Clay => _clay ??= Build(
        key: (0.45f, 0.55f, 0.70f), keyColour: (1.00f, 0.96f, 0.90f),
        fill: (-0.55f, 0.10f, 0.55f), fillColour: (0.42f, 0.47f, 0.58f),
        ambient: (0.10f, 0.10f, 0.12f), baseColour: (0.55f, 0.50f, 0.46f),
        rim: 0.30f, gloss: 26f, specular: 0.22f);

    /// <summary>A cooler, flatter grey for when shape matters more than looks.</summary>
    public static int[] Grey => _grey ??= Build(
        key: (0.35f, 0.60f, 0.72f), keyColour: (0.95f, 0.97f, 1.00f),
        fill: (-0.60f, -0.20f, 0.50f), fillColour: (0.34f, 0.36f, 0.42f),
        ambient: (0.22f, 0.23f, 0.25f), baseColour: (0.66f, 0.67f, 0.70f),
        rim: 0.22f, gloss: 12f, specular: 0.12f);

    private static int[] Build((float X, float Y, float Z) key,
                               (float R, float G, float B) keyColour,
                               (float X, float Y, float Z) fill,
                               (float R, float G, float B) fillColour,
                               (float R, float G, float B) ambient,
                               (float R, float G, float B) baseColour,
                               float rim, float gloss, float specular)
    {
        var pixels = new int[Size * Size];
        var keyLen = MathF.Sqrt(key.X * key.X + key.Y * key.Y + key.Z * key.Z);
        var fillLen = MathF.Sqrt(fill.X * fill.X + fill.Y * fill.Y + fill.Z * fill.Z);
        var kx = key.X / keyLen; var ky = key.Y / keyLen; var kz = key.Z / keyLen;
        var fx = fill.X / fillLen; var fy = fill.Y / fillLen; var fz = fill.Z / fillLen;

        for (var y = 0; y < Size; y++)
        {
            var ny = 1f - 2f * (y + 0.5f) / Size;
            for (var x = 0; x < Size; x++)
            {
                var nx = 2f * (x + 0.5f) / Size - 1f;
                var r2 = nx * nx + ny * ny;
                if (r2 > 1f)
                {
                    pixels[y * Size + x] = 0;      // outside the sphere
                    continue;
                }
                var nz = MathF.Sqrt(1f - r2);

                var kd = MathF.Max(0f, nx * kx + ny * ky + nz * kz);
                var fd = MathF.Max(0f, nx * fx + ny * fy + nz * fz);

                // Half-vector against a viewer down -Z, so the highlight sits
                // where a real one would rather than on the silhouette.
                var hx = kx; var hy = ky; var hz = kz + 1f;
                var hl = MathF.Sqrt(hx * hx + hy * hy + hz * hz);
                var spec = MathF.Pow(MathF.Max(0f, (nx * hx + ny * hy + nz * hz) / hl), gloss)
                           * specular;

                var edge = MathF.Pow(1f - nz, 3f) * rim;

                var r = baseColour.R * (ambient.R + keyColour.R * kd + fillColour.R * fd)
                        + spec + edge;
                var g = baseColour.G * (ambient.G + keyColour.G * kd + fillColour.G * fd)
                        + spec + edge;
                var b = baseColour.B * (ambient.B + keyColour.B * kd + fillColour.B * fd)
                        + spec + edge * 1.15f;

                pixels[y * Size + x] = unchecked((int)0xFF000000)
                                     | (Clamp(r) << 16) | (Clamp(g) << 8) | Clamp(b);
            }
        }
        return pixels;
    }

    private static int Clamp(float v)
    {
        // A touch of gamma, so midtones do not read as muddy as linear light does.
        var g = MathF.Pow(Math.Clamp(v, 0f, 1f), 1f / 2.2f);
        var i = (int)(g * 255f + 0.5f);
        return i < 0 ? 0 : (i > 255 ? 255 : i);
    }
}
