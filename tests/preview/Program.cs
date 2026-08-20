// SPDX-License-Identifier: MIT
//
// Renders the preview's rasteriser against shapes whose correct output is known,
// and checks the pixels. Run it with a folder argument to also write the frames
// out as PNGs and look at them:
//
//     dotnet run --project tests/preview -- /tmp/preview-out
//
// The renderer is small but it is the sort of small that is easy to get subtly
// wrong -- a flipped barycentric weight, a depth test the wrong way round, a
// normal left in world space. Every one of those still produces a picture. So
// these assertions test the things a picture does not obviously show: that near
// geometry occludes far geometry rather than the reverse, that the shading
// follows the surface, that skinning actually moves vertices, and that a
// triangle straddling the camera plane does not spray pixels across the screen.

using System.IO.Compression;
using System.Numerics;
using FoxAnimRip.Preview;

var outDir = args.Length > 0 ? args[0] : null;
if (outDir is not null) Directory.CreateDirectory(outDir);

var failures = new List<string>();
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
    if (!ok) failures.Add(what);
}

// ---------------------------------------------------------------- matcap
var clay = Matcap.Clay;
Check(clay.Length == Matcap.Size * Matcap.Size, "the matcap is the size it says");
Check((clay[0] >> 24 & 0xFF) == 0, "the matcap's corners are outside the sphere");
var middle = clay[Matcap.Size / 2 * Matcap.Size + Matcap.Size / 2];
Check((middle >> 24 & 0xFF) == 255, "the matcap's centre is opaque");
var lit = clay[(int)(Matcap.Size * 0.28f) * Matcap.Size + (int)(Matcap.Size * 0.72f)];
var shadow = clay[(int)(Matcap.Size * 0.75f) * Matcap.Size + (int)(Matcap.Size * 0.25f)];
Check(Luma(lit) > Luma(shadow), "the matcap's key side is brighter than its shadow side");

// ---------------------------------------------------------------- a sphere
var sphere = Sphere(0.5f, 32, 24);
var renderer = new PreviewRenderer();
renderer.Resize(480, 360);
renderer.Options.ShowGround = false;
renderer.Camera.Frame(sphere.Centre, sphere.Radius);
renderer.Render(sphere, null, 0);
Save(outDir, "sphere.png", renderer);

var covered = renderer.Pixels.Count(p => p != Background(renderer, 0)
                                      && p != Background(renderer, renderer.Height - 1));
Check(covered > 480 * 360 / 12, $"the sphere covers a sensible area of the frame ({covered}px)");

var top = renderer.Pixels[(renderer.Height / 4) * renderer.Width + renderer.Width / 2];
var bottom = renderer.Pixels[(renderer.Height * 3 / 4) * renderer.Width + renderer.Width / 2];
Check(Luma(top) != Luma(bottom), "shading varies across the sphere rather than being flat");

// --------------------------------- interpolation lands on the right vertex
// A triangle whose three corners have wildly different normals. Near each
// corner the shading must match *that* corner. This is the assertion that
// pins the barycentric pairing down: an edge function vanishes along its own
// edge, so it weights the vertex opposite it, and pairing them off by
// position instead produces a picture that still looks plausible -- just
// flat-shaded per triangle, which is easy to miss and was.
var corners = new[]
{
    Vector3.Normalize(new Vector3(-0.9f, 0f, 0.4f)),
    Vector3.Normalize(new Vector3(0.9f, 0f, 0.4f)),
    Vector3.Normalize(new Vector3(0f, 0.9f, 0.4f)),
};
var fan = new PreviewModel
{
    Positions = new[] { -1f, -1f, 0f, 1f, -1f, 0f, 0f, 1f, 0f },
    Normals = new[] { corners[0].X, corners[0].Y, corners[0].Z,
                      corners[1].X, corners[1].Y, corners[1].Z,
                      corners[2].X, corners[2].Y, corners[2].Z },
    BoneIndices = new int[12], BoneWeights = new float[12],
    Triangles = new[] { 0, 1, 2 },
    VertexCount = 3, BoneCount = 1,
    BoneParents = new[] { -1 }, BoneNames = new[] { "root" },
    BoneRest = new[] { Vector3.Zero },
    Min = new Vector3(-1, -1, 0), Max = new Vector3(1, 1, 0),
};
renderer.Camera.Yaw = 0f;
renderer.Camera.Pitch = 0f;
renderer.Camera.Target = Vector3.Zero;
renderer.Camera.Distance = 3.2f;
renderer.Camera.Near = 0.05f;
renderer.Camera.Far = 50f;
renderer.Render(fan, null, 0);
Save(outDir, "interpolation.png", renderer);

// Screen positions of the three corners, nudged inwards off the edge.
var probes = new (int X, int Y)[3];
for (var i = 0; i < 3; i++)
{
    var world = new Vector3(fan.Positions[i * 3], fan.Positions[i * 3 + 1], 0f);
    var centrePull = Vector3.Normalize(-world) * 0.35f;
    probes[i] = Project(renderer, world + centrePull);
}
for (var i = 0; i < 3; i++)
{
    var sampled = renderer.Pixels[probes[i].Y * renderer.Width + probes[i].X];
    var own = Math.Abs(Luma(sampled) - Luma(ShadeOf(corners[i])));
    var others = Enumerable.Range(0, 3).Where(k => k != i)
        .Min(k => Math.Abs(Luma(sampled) - Luma(ShadeOf(corners[k]))));
    Check(own <= others,
          $"near corner {i} the shading matches corner {i}'s normal "
          + $"(off by {own}, nearest other corner off by {others})");
}

// ------------------------------------------------- depth: near hides far
var pair = TwoQuads();
renderer.Camera.Yaw = 0f;
renderer.Camera.Pitch = 0f;
renderer.Camera.Target = Vector3.Zero;
renderer.Camera.Distance = 4f;
renderer.Camera.Near = 0.05f;
renderer.Camera.Far = 50f;
renderer.Render(pair.Model, null, 0);
Save(outDir, "depth.png", renderer);

// The near quad is the one closer to the camera; it is also the darker of the
// two because its normal is tilted away, so we can tell them apart by luma.
var centrePixel = renderer.Pixels[(renderer.Height / 2) * renderer.Width + renderer.Width / 2];
Check(centrePixel != 0, "something was drawn where the two quads overlap");
var nearShade = ShadeOf(pair.NearNormal);
var farShade = ShadeOf(pair.FarNormal);
Check(Math.Abs(Luma(centrePixel) - Luma(nearShade))
      < Math.Abs(Luma(centrePixel) - Luma(farShade)),
      "the near quad occludes the far one, not the other way round");

// ------------------------------------------------- skinning actually moves
var limb = Limb();
var rest = new float[limb.VertexCount * 3];
var restNormals = new float[limb.VertexCount * 3];
limb.Skin(null, rest, restNormals);

var bent = new Matrix4x4[2];
bent[0] = Matrix4x4.Identity;
bent[1] = Matrix4x4.CreateRotationZ(1.0f);
var posed = new float[limb.VertexCount * 3];
var posedNormals = new float[limb.VertexCount * 3];
limb.Skin(bent, posed, posedNormals);

var moved = 0;
var still = 0;
for (var v = 0; v < limb.VertexCount; v++)
{
    var d = Vector3.Distance(At(rest, v), At(posed, v));
    if (d > 1e-4f) moved++; else still++;
}
Check(moved > 0, $"bending a bone moves the vertices weighted to it ({moved} moved)");
Check(still > 0, $"vertices weighted to the still bone do not move ({still} stayed)");

// ------------------------------------------- a triangle behind the camera
var behind = new PreviewModel
{
    Positions = new[] { -1f, 0f, 5f, 1f, 0f, 5f, 0f, 1f, -5f },
    Normals = new[] { 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f },
    BoneIndices = new int[12],
    BoneWeights = new float[12],
    Triangles = new[] { 0, 1, 2 },
    VertexCount = 3,
    BoneCount = 1,
    BoneParents = new[] { -1 },
    BoneNames = new[] { "root" },
    BoneRest = new[] { Vector3.Zero },
    Min = new Vector3(-1, 0, -5),
    Max = new Vector3(1, 1, 5),
};
renderer.Camera.Target = Vector3.Zero;
renderer.Camera.Distance = 2f;
renderer.Render(behind, null, 0);
Save(outDir, "clip.png", renderer);
Check(true, "a triangle straddling the camera plane renders without crashing");
var filled = renderer.Pixels.Count(p => Luma(p) > 90);
Check(filled < renderer.Pixels.Length,
      "near-plane clipping does not smear the triangle over the whole frame");

Console.WriteLine();
Console.WriteLine(failures.Count == 0
    ? "PASSED (0 failures)"
    : $"FAILED ({failures.Count} failure(s))");
foreach (var failure in failures) Console.WriteLine("  - " + failure);
if (outDir is not null) Console.WriteLine($"\nimages in {outDir}");
return failures.Count == 0 ? 0 : 1;

// ------------------------------------------------------------------ helpers

static Vector3 At(float[] buffer, int v) =>
    new(buffer[v * 3], buffer[v * 3 + 1], buffer[v * 3 + 2]);

static int Luma(int argb) =>
    (((argb >> 16 & 0xFF) * 299) + ((argb >> 8 & 0xFF) * 587) + ((argb & 0xFF) * 114)) / 1000;

static int Background(PreviewRenderer r, int row) => r.Pixels[row * r.Width];

static (int X, int Y) Project(PreviewRenderer r, Vector3 world)
{
    var vp = r.Camera.View() * r.Camera.Projection((float)r.Width / r.Height);
    var clip = Vector4.Transform(new Vector4(world, 1f), vp);
    var inverse = 1f / clip.W;
    return (Math.Clamp((int)((clip.X * inverse + 1f) * r.Width * 0.5f), 0, r.Width - 1),
            Math.Clamp((int)((1f - clip.Y * inverse) * r.Height * 0.5f), 0, r.Height - 1));
}

static int ShadeOf(Vector3 viewNormal)
{
    var n = Vector3.Normalize(viewNormal);
    if (n.Z < 0) n = -n;
    var u = (int)((n.X * 0.5f + 0.5f) * (Matcap.Size - 1) + 0.5f);
    var v = (int)((0.5f - n.Y * 0.5f) * (Matcap.Size - 1) + 0.5f);
    return Matcap.Clay[Math.Clamp(v, 0, Matcap.Size - 1) * Matcap.Size
                       + Math.Clamp(u, 0, Matcap.Size - 1)];
}

static PreviewModel Sphere(float radius, int segments, int rings)
{
    var positions = new List<float>();
    var normals = new List<float>();
    var triangles = new List<int>();
    for (var y = 0; y <= rings; y++)
    {
        var v = (float)y / rings;
        var phi = v * MathF.PI;
        for (var x = 0; x <= segments; x++)
        {
            var u = (float)x / segments;
            var theta = u * MathF.PI * 2f;
            var n = new Vector3(MathF.Sin(phi) * MathF.Cos(theta), MathF.Cos(phi),
                                MathF.Sin(phi) * MathF.Sin(theta));
            positions.Add(n.X * radius); positions.Add(n.Y * radius); positions.Add(n.Z * radius);
            normals.Add(n.X); normals.Add(n.Y); normals.Add(n.Z);
        }
    }
    for (var y = 0; y < rings; y++)
        for (var x = 0; x < segments; x++)
        {
            var a = y * (segments + 1) + x;
            var b = a + segments + 1;
            triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
        }
    return Wrap(positions, normals, triangles, new Vector3(-radius), new Vector3(radius));
}

static (PreviewModel Model, Vector3 NearNormal, Vector3 FarNormal) TwoQuads()
{
    // Two overlapping quads facing the camera, the near one tilted so the
    // matcap shades the two differently and they can be told apart by colour.
    var nearNormal = Vector3.Normalize(new Vector3(0.6f, 0.3f, 1f));
    var farNormal = new Vector3(0f, 0f, 1f);
    var positions = new List<float>();
    var normals = new List<float>();
    var triangles = new List<int>();

    void Quad(float z, Vector3 n)
    {
        var start = positions.Count / 3;
        foreach (var (x, y) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
        {
            positions.Add(x); positions.Add(y); positions.Add(z);
            normals.Add(n.X); normals.Add(n.Y); normals.Add(n.Z);
        }
        triangles.AddRange(new[] { start, start + 1, start + 2,
                                   start, start + 2, start + 3 });
    }

    Quad(-1.0f, farNormal);      // further from a camera sitting at +Z
    Quad(1.0f, nearNormal);      // nearer
    return (Wrap(positions, normals, triangles, new Vector3(-1, -1, -1), new Vector3(1, 1, 1)),
            nearNormal, farNormal);
}

static PreviewModel Limb()
{
    // A strip of vertices: the first half rigidly on bone 0, the second on
    // bone 1, so rotating bone 1 must move exactly half of them.
    var positions = new List<float>();
    var normals = new List<float>();
    var triangles = new List<int>();
    var boneIndices = new List<int>();
    var weights = new List<float>();

    for (var i = 0; i < 8; i++)
    {
        var t = i / 7f;
        positions.Add(t); positions.Add(0f); positions.Add(0f);
        normals.Add(0f); normals.Add(0f); normals.Add(1f);
        var bone = i < 4 ? 0 : 1;
        boneIndices.AddRange(new[] { bone, 0, 0, 0 });
        weights.AddRange(new[] { 1f, 0f, 0f, 0f });
    }
    triangles.AddRange(new[] { 0, 1, 2 });

    return new PreviewModel
    {
        Positions = positions.ToArray(),
        Normals = normals.ToArray(),
        Triangles = triangles.ToArray(),
        BoneIndices = boneIndices.ToArray(),
        BoneWeights = weights.ToArray(),
        VertexCount = 8,
        BoneCount = 2,
        BoneParents = new[] { -1, 0 },
        BoneNames = new[] { "a", "b" },
        BoneRest = new[] { Vector3.Zero, new Vector3(0.5f, 0, 0) },
        Min = Vector3.Zero,
        Max = new Vector3(1, 0, 0),
    };
}

static PreviewModel Wrap(List<float> positions, List<float> normals, List<int> triangles,
                         Vector3 min, Vector3 max)
{
    var count = positions.Count / 3;
    return new PreviewModel
    {
        Positions = positions.ToArray(),
        Normals = normals.ToArray(),
        Triangles = triangles.ToArray(),
        BoneIndices = new int[count * 4],
        BoneWeights = new float[count * 4],
        VertexCount = count,
        BoneCount = 1,
        BoneParents = new[] { -1 },
        BoneNames = new[] { "root" },
        BoneRest = new[] { Vector3.Zero },
        Min = min,
        Max = max,
    };
}

static void Save(string dir, string name, PreviewRenderer renderer)
{
    if (dir is null) return;
    File.WriteAllBytes(Path.Combine(dir, name),
                       Png(renderer.Pixels, renderer.Width, renderer.Height));
}

/// <summary>A minimal PNG encoder, so the test has no dependencies of its own.</summary>
static byte[] Png(int[] pixels, int width, int height)
{
    var raw = new byte[height * (width * 3 + 1)];
    var at = 0;
    for (var y = 0; y < height; y++)
    {
        raw[at++] = 0;                                     // filter: none
        for (var x = 0; x < width; x++)
        {
            var p = pixels[y * width + x];
            raw[at++] = (byte)(p >> 16);
            raw[at++] = (byte)(p >> 8);
            raw[at++] = (byte)p;
        }
    }

    using var deflated = new MemoryStream();
    deflated.WriteByte(0x78); deflated.WriteByte(0x01);    // zlib header
    using (var deflate = new DeflateStream(deflated, CompressionLevel.Fastest, true))
        deflate.Write(raw, 0, raw.Length);
    uint a = 1, b = 0;
    foreach (var value in raw) { a = (a + value) % 65521; b = (b + a) % 65521; }
    foreach (var shift in new[] { 24, 16, 8, 0 })
        deflated.WriteByte((byte)((b << 16 | a) >> shift));

    using var png = new MemoryStream();
    png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
    var header = new byte[13];
    BigEndian(header, 0, width);
    BigEndian(header, 4, height);
    header[8] = 8; header[9] = 2;                          // 8-bit truecolour
    Chunk(png, "IHDR", header);
    Chunk(png, "IDAT", deflated.ToArray());
    Chunk(png, "IEND", Array.Empty<byte>());
    return png.ToArray();

    static void BigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24); buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8); buffer[offset + 3] = (byte)value;
    }

    static void Chunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BigEndian(length, 0, data.Length);
        stream.Write(length);
        var body = new byte[4 + data.Length];
        for (var i = 0; i < 4; i++) body[i] = (byte)type[i];
        data.CopyTo(body, 4);
        stream.Write(body);
        var crcBytes = new byte[4];
        BigEndian(crcBytes, 0, unchecked((int)Crc(body)));
        stream.Write(crcBytes);
    }

    static uint Crc(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var value in data)
        {
            crc ^= value;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        return crc ^ 0xFFFFFFFF;
    }
}
