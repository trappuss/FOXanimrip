// SPDX-License-Identifier: MIT
using System.Numerics;

namespace FoxAnimRip.Preview;

/// <summary>
/// A small software renderer: triangles and lines into an ARGB buffer.
///
/// There is no OpenGL here on purpose. The alternative was a native GL binding
/// beside the executable, which costs the tool its "one file, copy it anywhere"
/// property and fails on the machines least able to diagnose it -- odd drivers,
/// Remote Desktop, a locked-down work laptop. A skinned character is on the
/// order of ten thousand triangles; a scanline rasteriser does that in a couple
/// of milliseconds per frame, which is more than enough for something whose job
/// is to answer "is this clip broken".
///
/// Depth is interpolated as 1/w, which is the quantity that varies linearly in
/// screen space; nearer is larger. Normals are carried through divided by w and
/// multiplied back afterwards, so they stay perspective-correct on the large
/// triangles you get when the camera is close to a face.
/// </summary>
public sealed class Rasterizer
{
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>0xAARRGGBB, top row first: blit straight to a bitmap.</summary>
    public int[] Colour { get; private set; } = Array.Empty<int>();

    private float[] _depth = Array.Empty<float>();
    private float[] _clip = Array.Empty<float>();     // x, y, z, w per vertex
    private float[] _viewNormal = Array.Empty<float>();

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height) return;
        Width = width;
        Height = height;
        Colour = new int[width * height];
        _depth = new float[width * height];
    }

    public void Clear(int top, int bottom)
    {
        // A vertical gradient reads as a backdrop rather than a void, which
        // makes it much easier to tell a dark model from empty space.
        for (var y = 0; y < Height; y++)
        {
            var t = Height > 1 ? (float)y / (Height - 1) : 0f;
            var colour = Lerp(top, bottom, t);
            var row = y * Width;
            for (var x = 0; x < Width; x++) Colour[row + x] = colour;
        }
        Array.Clear(_depth, 0, _depth.Length);
    }

    /// <summary>Draw a skinned mesh with matcap shading.</summary>
    public void DrawMesh(float[] positions, float[] normals, int[] triangles,
                         Matrix4x4 viewProjection, Matrix4x4 view, int[] matcap)
    {
        var vertexCount = positions.Length / 3;
        if (vertexCount == 0 || triangles.Length < 3) return;

        if (_clip.Length < vertexCount * 4) _clip = new float[vertexCount * 4];
        if (_viewNormal.Length < vertexCount * 3) _viewNormal = new float[vertexCount * 3];

        for (var v = 0; v < vertexCount; v++)
        {
            var p = new Vector4(positions[v * 3], positions[v * 3 + 1],
                                positions[v * 3 + 2], 1f);
            var c = Vector4.Transform(p, viewProjection);
            _clip[v * 4] = c.X; _clip[v * 4 + 1] = c.Y;
            _clip[v * 4 + 2] = c.Z; _clip[v * 4 + 3] = c.W;

            var n = new Vector3(normals[v * 3], normals[v * 3 + 1], normals[v * 3 + 2]);
            var vn = Vector3.TransformNormal(n, view);
            _viewNormal[v * 3] = vn.X; _viewNormal[v * 3 + 1] = vn.Y;
            _viewNormal[v * 3 + 2] = vn.Z;
        }

        Span<Vertex> polygon = stackalloc Vertex[8];
        Span<Vertex> clipped = stackalloc Vertex[8];

        for (var t = 0; t + 2 < triangles.Length; t += 3)
        {
            var a = triangles[t];
            var b = triangles[t + 1];
            var c = triangles[t + 2];
            if ((uint)a >= (uint)vertexCount || (uint)b >= (uint)vertexCount
                || (uint)c >= (uint)vertexCount) continue;

            polygon[0] = Load(a);
            polygon[1] = Load(b);
            polygon[2] = Load(c);

            var count = ClipNear(polygon, 3, clipped);
            for (var i = 1; i + 1 < count; i++)
                Triangle(clipped[0], clipped[i], clipped[i + 1], matcap);
        }
    }

    private Vertex Load(int index) => new(
        _clip[index * 4], _clip[index * 4 + 1], _clip[index * 4 + 2], _clip[index * 4 + 3],
        _viewNormal[index * 3], _viewNormal[index * 3 + 1], _viewNormal[index * 3 + 2]);

    private readonly record struct Vertex(float X, float Y, float Z, float W,
                                          float Nx, float Ny, float Nz)
    {
        public static Vertex Lerp(Vertex a, Vertex b, float t) => new(
            a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t, a.W + (b.W - a.W) * t,
            a.Nx + (b.Nx - a.Nx) * t, a.Ny + (b.Ny - a.Ny) * t,
            a.Nz + (b.Nz - a.Nz) * t);
    }

    private const float NearW = 1e-4f;

    /// <summary>Clip a polygon against w &gt; 0, the one plane that must not be skipped.</summary>
    private static int ClipNear(Span<Vertex> input, int count, Span<Vertex> output)
    {
        var written = 0;
        for (var i = 0; i < count; i++)
        {
            var current = input[i];
            var next = input[(i + 1) % count];
            var inCurrent = current.W > NearW;
            var inNext = next.W > NearW;

            if (inCurrent && written < output.Length) output[written++] = current;
            if (inCurrent != inNext && written < output.Length)
            {
                var t = (NearW - current.W) / (next.W - current.W);
                output[written++] = Vertex.Lerp(current, next, t);
            }
        }
        return written;
    }

    private void Triangle(Vertex a, Vertex b, Vertex c, int[] matcap)
    {
        var halfW = Width * 0.5f;
        var halfH = Height * 0.5f;

        var aw = 1f / a.W; var bw = 1f / b.W; var cw = 1f / c.W;
        var ax = (a.X * aw + 1f) * halfW; var ay = (1f - a.Y * aw) * halfH;
        var bx = (b.X * bw + 1f) * halfW; var by = (1f - b.Y * bw) * halfH;
        var cx = (c.X * cw + 1f) * halfW; var cy = (1f - c.Y * cw) * halfH;

        var area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        if (area == 0f) return;
        // Two-sided: Fox Engine meshes are not consistently wound, and a
        // one-sided preview turns a jacket inside out for no diagnostic gain.
        if (area < 0f)
        {
            (bx, cx) = (cx, bx);
            (by, cy) = (cy, by);
            (bw, cw) = (cw, bw);
            (b, c) = (c, b);
            area = -area;
        }

        var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx))));
        var maxX = Math.Min(Width - 1, (int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx))));
        var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(ay, MathF.Min(by, cy))));
        var maxY = Math.Min(Height - 1, (int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, cy))));
        if (minX > maxX || minY > maxY) return;

        var inverseArea = 1f / area;
        var anx = a.Nx * aw; var any = a.Ny * aw; var anz = a.Nz * aw;
        var bnx = b.Nx * bw; var bny = b.Ny * bw; var bnz = b.Nz * bw;
        var cnx = c.Nx * cw; var cny = c.Ny * cw; var cnz = c.Nz * cw;

        for (var y = minY; y <= maxY; y++)
        {
            var py = y + 0.5f;
            var row = y * Width;
            for (var x = minX; x <= maxX; x++)
            {
                var px = x + 0.5f;
                var w0 = ((bx - ax) * (py - ay) - (by - ay) * (px - ax)) * inverseArea;
                var w1 = ((cx - bx) * (py - by) - (cy - by) * (px - bx)) * inverseArea;
                if (w0 < 0f || w1 < 0f) continue;
                var w2 = 1f - w0 - w1;
                if (w2 < 0f) continue;

                // An edge function vanishes along its own edge, so it is the
                // weight of the vertex *opposite* it: w0 (edge ab) weights c,
                // w1 (edge bc) weights a, and the remainder weights b. Getting
                // this pairing wrong still produces a picture -- just one with
                // every triangle flat-shaded, which is how it was caught.
                var invW = w1 * aw + w2 * bw + w0 * cw;
                var index = row + x;
                if (invW <= _depth[index]) continue;

                var nx = w1 * anx + w2 * bnx + w0 * cnx;
                var ny = w1 * any + w2 * bny + w0 * cny;
                var nz = w1 * anz + w2 * bnz + w0 * cnz;
                var scale = 1f / invW;
                nx *= scale; ny *= scale; nz *= scale;
                var length = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                if (length > 1e-8f) { nx /= length; ny /= length; nz /= length; }

                // Face the normal towards the viewer: two-sided shading, so the
                // inside of an open sleeve is lit rather than black.
                if (nz < 0f) { nx = -nx; ny = -ny; nz = -nz; }

                _depth[index] = invW;
                Colour[index] = Sample(matcap, nx, ny);
            }
        }
    }

    private static int Sample(int[] matcap, float nx, float ny)
    {
        var u = (int)((nx * 0.5f + 0.5f) * (Matcap.Size - 1) + 0.5f);
        var v = (int)((0.5f - ny * 0.5f) * (Matcap.Size - 1) + 0.5f);
        if (u < 0) u = 0; else if (u >= Matcap.Size) u = Matcap.Size - 1;
        if (v < 0) v = 0; else if (v >= Matcap.Size) v = Matcap.Size - 1;
        return matcap[v * Matcap.Size + u];
    }

    /// <summary>
    /// A line in world space, drawn over the mesh regardless of depth.
    ///
    /// The skeleton overlay is a diagnostic: hiding the far side of it inside
    /// the body would defeat the point of turning it on.
    /// </summary>
    public void DrawLine(Vector3 from, Vector3 to, Matrix4x4 viewProjection, int colour)
    {
        var a = Vector4.Transform(new Vector4(from, 1f), viewProjection);
        var b = Vector4.Transform(new Vector4(to, 1f), viewProjection);
        if (a.W <= NearW && b.W <= NearW) return;
        if (a.W <= NearW) a = Vector4.Lerp(a, b, (NearW - a.W) / (b.W - a.W));
        if (b.W <= NearW) b = Vector4.Lerp(b, a, (NearW - b.W) / (a.W - b.W));

        var (x0, y0) = ToScreen(a);
        var (x1, y1) = ToScreen(b);
        var steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        if (steps <= 0) { Plot(x0, y0, colour); return; }

        for (var i = 0; i <= steps; i++)
        {
            var t = (float)i / steps;
            Plot((int)MathF.Round(x0 + (x1 - x0) * t),
                 (int)MathF.Round(y0 + (y1 - y0) * t), colour);
        }
    }

    public void DrawPoint(Vector3 at, Matrix4x4 viewProjection, int colour, int radius = 1)
    {
        var p = Vector4.Transform(new Vector4(at, 1f), viewProjection);
        if (p.W <= NearW) return;
        var (x, y) = ToScreen(p);
        for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
                Plot(x + dx, y + dy, colour);
    }

    private (int X, int Y) ToScreen(Vector4 clip)
    {
        var inverse = 1f / clip.W;
        return ((int)MathF.Round((clip.X * inverse + 1f) * Width * 0.5f),
                (int)MathF.Round((1f - clip.Y * inverse) * Height * 0.5f));
    }

    private void Plot(int x, int y, int colour)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
        Colour[y * Width + x] = colour;
    }

    private static int Lerp(int a, int b, float t)
    {
        int Channel(int shift) =>
            (int)(((a >> shift) & 0xFF) + (((b >> shift) & 0xFF) - ((a >> shift) & 0xFF)) * t);
        return unchecked((int)0xFF000000) | (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }
}
