// SPDX-License-Identifier: MIT
using System.Numerics;
using FoxBrowser.Models.Anim;
using FoxBrowser.Models.Export;
using FoxBrowser.Models.Fmdl;

namespace FoxAnimRip;

/// <summary>
/// Baking a clip with its root travel left in.
///
/// FoxBrowser's own <c>ExportBake.FromGani</c> passes
/// <c>applyGaniTranslation: false</c> as a constant, so every clip it produces
/// runs on the spot. That is the right default for a library of Actions -- a
/// walk cycle that wanders off is a nuisance to retarget -- but it throws away
/// the one number that says how fast the clip was authored to move, and it means
/// a character in Blender moonwalks instead of walking.
///
/// This is the same bake with that one argument flipped. The Euler extraction
/// matches FoxBrowser's exactly, normalising the basis before reading angles, so
/// a clip baked here and one baked there differ only by the root's travel.
/// </summary>
public static class RootBake
{
    /// <summary>Frame count the bake will produce -- for preallocating
    /// <c>worldOut</c> in <see cref="FromGani"/>.</summary>
    public static int Frames(GaniAnimation anim, int step)
        => Math.Max(1, anim.FrameCount / Math.Max(1, step));

    public static ExportClip FromGani(FmdlModel model, GaniAnimation anim, string name,
                                      IReadOnlyDictionary<int, FrigFile.BoneDrive> drives,
                                      IReadOnlyList<FrigFile.IkJob> ikJobs, FrigFile frig,
                                      IReadOnlyList<FrdvFile.Op> helpBones,
                                      float fps = 59.94f, int step = 1,
                                      Vector3[][] worldOut = null)
    {
        step = Math.Max(1, step);
        var frames = Math.Max(1, anim.FrameCount / step);
        var boneCount = model.Bones.Count;

        var clip = new ExportClip
        {
            Name = name,
            Fps = fps / step,
            FrameCount = frames,
            Translation = new Vector3[boneCount][],
            RotationEuler = new Vector3[boneCount][],
        };
        for (var i = 0; i < boneCount; i++)
        {
            clip.Translation[i] = new Vector3[frames];
            clip.RotationEuler[i] = new Vector3[frames];
        }

        for (var f = 0; f < frames; f++)
        {
            var world = Preview.PoseGate.Run(() =>
            {
                AnimSkinner.BuildPalette(model, anim, f * step, drives,
                                         applyGaniTranslation: true,
                                         ikJobs, frig, helpBones);
                return AnimSkinner.LastAnimWorld;
            });
            if (world is null) break;

            for (var b = 0; b < boneCount && b < world.Length; b++)
            {
                if (worldOut is not null && b < worldOut.Length
                    && f < worldOut[b].Length)
                    worldOut[b][f] = world[b].Translation;

                var parent = model.Bones[b].ParentIndex;
                var local = world[b];
                if (parent >= 0 && parent < world.Length
                    && Matrix4x4.Invert(world[parent], out var inverse))
                    local = world[b] * inverse;

                clip.Translation[b][f] = local.Translation;
                clip.RotationEuler[b][f] = EulerXyzDegrees(local);
            }
        }
        return clip;
    }

    /// <summary>
    /// XYZ Euler in degrees from a matrix whose basis may carry scale.
    ///
    /// Normalising first is what FoxBrowser does, and it matters: without it any
    /// scale in the solve leaks into the angles.
    /// </summary>
    private static Vector3 EulerXyzDegrees(Matrix4x4 m)
    {
        var x = Vector3.Normalize(new Vector3(m.M11, m.M12, m.M13));
        var y = Vector3.Normalize(new Vector3(m.M21, m.M22, m.M23));
        var z = Vector3.Normalize(new Vector3(m.M31, m.M32, m.M33));

        var sin = -x.Z;
        float pitch, roll, yaw;
        if (sin is < 0.99999f and > -0.99999f)
        {
            pitch = MathF.Asin(sin);
            roll = MathF.Atan2(y.Z, z.Z);
            yaw = MathF.Atan2(x.Y, x.X);
        }
        else
        {
            pitch = sin > 0 ? MathF.PI / 2f : -MathF.PI / 2f;
            roll = MathF.Atan2(-z.Y, y.Y);
            yaw = 0f;
        }

        const float ToDegrees = 180f / MathF.PI;
        return new Vector3(roll * ToDegrees, pitch * ToDegrees, yaw * ToDegrees);
    }

    /// <summary>
    /// How far the root travels over a clip, and how fast.
    ///
    /// This is the number the in-place bake discards, and it is what tells you
    /// the speed a walk or run cycle was authored for.
    /// </summary>
    public static (float Distance, float Speed) Travel(ExportClip clip)
    {
        if (clip.FrameCount < 2 || clip.Translation.Length == 0) return (0, 0);
        var root = clip.Translation[0];
        if (root.Length < 2) return (0, 0);
        var distance = Vector3.Distance(root[0], root[^1]);
        var seconds = (clip.FrameCount - 1) / MathF.Max(1e-3f, clip.Fps);
        return (distance, distance / MathF.Max(1e-3f, seconds));
    }

    /// <summary>
    /// The ground speed a cycle was authored for, read off the planted foot.
    ///
    /// MGSV's gait cycles -- walk, run, dash loops, even the starts -- are
    /// authored in place: the root barely translates, because the engine applies
    /// travel parametrically from its compiled motion graph. <see cref="Travel"/>
    /// therefore reports ~0 for exactly the clips whose speed matters most.
    ///
    /// The speed is still in the animation data. While a foot is planted it is
    /// stationary in the world, so in the character's frame it sweeps backward
    /// under the root at precisely the authored travel speed. Measuring the
    /// stance foot's horizontal speed relative to the root recovers that number
    /// from the curves alone -- true data, nothing assumed. For clips whose root
    /// does travel, the same measurement returns the travel speed, so it doubles
    /// as a cross-check.
    /// </summary>
    /// <param name="world">Per-bone, per-frame world positions from
    /// <see cref="FromGani"/>'s <c>worldOut</c>. Bone 0 is the root.</param>
    /// <returns>Median stance-phase speed in m/s and the number of stance
    /// samples behind it. (0, n) when there is no usable stance -- an idle,
    /// airborne, or non-biped clip.</returns>
    public static (float Speed, int Samples) GaitSpeed(Vector3[][] world,
                                                       int leftFoot, int rightFoot,
                                                       float fps)
    {
        if (world is null || world.Length == 0 || world[0] is null) return (0, 0);
        var frames = world[0].Length;
        if (frames < 6 || fps <= 0) return (0, 0);

        var feet = new List<int>(2);
        if (leftFoot > 0 && leftFoot < world.Length) feet.Add(leftFoot);
        if (rightFoot > 0 && rightFoot < world.Length) feet.Add(rightFoot);
        if (feet.Count == 0) return (0, 0);
        var root = world[0];

        // Which axis is up? The one along which the root sits furthest from the
        // feet. Read from the data rather than assumed, so a differently
        // oriented rig cannot silently give heights measured sideways.
        var mean = Vector3.Zero;
        foreach (var foot in feet)
            for (var t = 0; t < frames; t++)
                mean += root[t] - world[foot][t];
        mean /= frames * feet.Count;
        var up = MathF.Abs(mean.Y) >= MathF.Abs(mean.X)
            ? (MathF.Abs(mean.Z) > MathF.Abs(mean.Y) ? 2 : 1)
            : (MathF.Abs(mean.Z) > MathF.Abs(mean.X) ? 2 : 0);

        static float H(Vector3 v, int axis)
            => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
        static Vector3 Flat(Vector3 v, int axis) => axis switch
        {
            0 => new Vector3(0, v.Y, v.Z),
            1 => new Vector3(v.X, 0, v.Z),
            _ => new Vector3(v.X, v.Y, 0),
        };

        var samples = new List<float>();
        foreach (var footIndex in feet)
        {
            var foot = world[footIndex];
            float hmin = float.MaxValue, hmax = float.MinValue;
            for (var t = 0; t < frames; t++)
            {
                var h = H(foot[t], up);
                if (h < hmin) hmin = h;
                if (h > hmax) hmax = h;
            }
            // A foot that never lifts more than 2 cm is not stepping -- an
            // idle or a pose -- and would only contribute noise.
            if (hmax - hmin < 0.02f) continue;

            var contact = hmin + 0.25f * (hmax - hmin);
            for (var t = 0; t + 1 < frames; t++)
            {
                if (H(foot[t], up) > contact || H(foot[t + 1], up) > contact)
                    continue;
                var a = foot[t] - root[t];
                var b = foot[t + 1] - root[t + 1];
                samples.Add(Flat(b - a, up).Length() * fps);
            }
        }

        if (samples.Count < 4) return (0, samples.Count);
        samples.Sort();
        return (samples[samples.Count / 2], samples.Count);
    }
}
