// SPDX-License-Identifier: MIT
using System.Numerics;

namespace FoxAnimRip.Preview;

/// <summary>An orbit camera: the one people expect from a model viewer.</summary>
public sealed class OrbitCamera
{
    public Vector3 Target;
    public float Distance = 3f;
    public float Yaw = 0.6f;
    public float Pitch = 0.15f;
    public float FieldOfView = 0.9f;      // radians, vertical
    public float Near = 0.02f;
    public float Far = 200f;

    private const float PitchLimit = 1.5533f;   // just under 89 degrees

    public void Orbit(float dYaw, float dPitch)
    {
        Yaw += dYaw;
        Pitch = Math.Clamp(Pitch + dPitch, -PitchLimit, PitchLimit);
    }

    public void Zoom(float factor) => Distance = Math.Clamp(Distance * factor, 0.05f, 500f);

    /// <summary>Pan in the camera's own plane, scaled so it feels the same at any zoom.</summary>
    public void Pan(float dx, float dy)
    {
        var (right, up, _) = Basis();
        Target += right * (-dx * Distance) + up * (dy * Distance);
    }

    public Vector3 Eye
    {
        get
        {
            var cosPitch = MathF.Cos(Pitch);
            return Target + new Vector3(MathF.Sin(Yaw) * cosPitch,
                                        MathF.Sin(Pitch),
                                        MathF.Cos(Yaw) * cosPitch) * Distance;
        }
    }

    private (Vector3 Right, Vector3 Up, Vector3 Forward) Basis()
    {
        var forward = Vector3.Normalize(Target - Eye);
        var right = Vector3.Cross(forward, Vector3.UnitY);
        right = right.LengthSquared() < 1e-8f
            ? Vector3.UnitX : Vector3.Normalize(right);
        return (right, Vector3.Normalize(Vector3.Cross(right, forward)), forward);
    }

    public Matrix4x4 View() => Matrix4x4.CreateLookAt(Eye, Target, Vector3.UnitY);

    public Matrix4x4 Projection(float aspect) =>
        Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, MathF.Max(0.01f, aspect),
                                               Near, Far);

    /// <summary>Point the camera at a model so the whole of it is on screen.</summary>
    public void Frame(Vector3 centre, float radius)
    {
        Target = centre;
        radius = MathF.Max(radius, 0.05f);
        Distance = radius / MathF.Tan(FieldOfView * 0.5f) * 1.5f;
        Near = MathF.Max(0.01f, radius * 0.01f);
        Far = Distance + radius * 20f;
    }
}

/// <summary>What to draw, and how.</summary>
public sealed class PreviewOptions
{
    public bool ShowMesh = true;
    public bool ShowSkeleton;
    public bool ShowGround = true;
    public bool WarmMatcap = true;

    public int BackgroundTop = unchecked((int)0xFF32363E);
    public int BackgroundBottom = unchecked((int)0xFF1A1C21);
    public int BoneColour = unchecked((int)0xFF6FD3FF);
    public int JointColour = unchecked((int)0xFFFFD27A);
    public int GroundColour = unchecked((int)0xFF3E434D);
}

/// <summary>
/// Draws a posed character into a pixel buffer.
///
/// Holds the two scratch arrays skinning writes into so playback allocates
/// nothing per frame; at sixty frames a second that is the difference between
/// smooth and a stutter every time the collector runs.
/// </summary>
public sealed class PreviewRenderer
{
    private readonly Rasterizer _raster = new();
    private float[] _positions = Array.Empty<float>();
    private float[] _normals = Array.Empty<float>();

    public OrbitCamera Camera { get; } = new();
    public PreviewOptions Options { get; } = new();
    public int Width => _raster.Width;
    public int Height => _raster.Height;
    public int[] Pixels => _raster.Colour;

    /// <summary>Milliseconds spent in the last <see cref="Render"/>, for the status line.</summary>
    public double LastFrameMs { get; private set; }

    public void Resize(int width, int height) => _raster.Resize(width, height);

    public void Render(PreviewModel model, PreviewClip clip, int frame)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        _raster.Clear(Options.BackgroundTop, Options.BackgroundBottom);

        if (model is null)
        {
            LastFrameMs = 0;
            return;
        }

        var view = Camera.View();
        var viewProjection = view * Camera.Projection((float)Width / Math.Max(1, Height));
        var palette = clip?.Palette(frame);

        if (Options.ShowGround) DrawGround(model, viewProjection);

        if (Options.ShowMesh && model.VertexCount > 0)
        {
            var needed = model.VertexCount * 3;
            if (_positions.Length < needed) _positions = new float[needed];
            if (_normals.Length < needed) _normals = new float[needed];
            model.Skin(palette, _positions, _normals);
            _raster.DrawMesh(_positions, _normals, model.Triangles, viewProjection, view,
                             Options.WarmMatcap ? Matcap.Clay : Matcap.Grey);
        }

        if (Options.ShowSkeleton) DrawSkeleton(model, clip, frame, viewProjection);

        LastFrameMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started)
                      * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    private void DrawSkeleton(PreviewModel model, PreviewClip clip, int frame,
                              Matrix4x4 viewProjection)
    {
        var positions = clip?.BonePositions(frame);
        if (positions is null || positions.Length < model.BoneCount) positions = model.BoneRest;
        if (positions.Length == 0) return;

        for (var i = 0; i < model.BoneCount && i < positions.Length; i++)
        {
            var parent = i < model.BoneParents.Length ? model.BoneParents[i] : -1;
            if (parent >= 0 && parent < positions.Length)
                _raster.DrawLine(positions[parent], positions[i], viewProjection,
                                 Options.BoneColour);
            _raster.DrawPoint(positions[i], viewProjection, Options.JointColour);
        }
    }

    /// <summary>
    /// A grid at the character's feet.
    ///
    /// Clips are baked in place -- the root's travel is deliberately dropped, so
    /// a walk cycle walks on the spot -- and without a fixed reference it is
    /// genuinely hard to tell whether the body is drifting or the camera is.
    /// </summary>
    private void DrawGround(PreviewModel model, Matrix4x4 viewProjection)
    {
        var y = model.Min.Y;
        var centre = model.Centre;
        var extent = MathF.Max(model.Radius, 0.5f) * 1.6f;
        var step = extent / 5f;

        for (var i = -5; i <= 5; i++)
        {
            var offset = i * step;
            _raster.DrawLine(new Vector3(centre.X - extent, y, centre.Z + offset),
                             new Vector3(centre.X + extent, y, centre.Z + offset),
                             viewProjection, Options.GroundColour);
            _raster.DrawLine(new Vector3(centre.X + offset, y, centre.Z - extent),
                             new Vector3(centre.X + offset, y, centre.Z + extent),
                             viewProjection, Options.GroundColour);
        }
    }
}
