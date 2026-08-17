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
    public static ExportClip FromGani(FmdlModel model, GaniAnimation anim, string name,
                                      IReadOnlyDictionary<int, FrigFile.BoneDrive> drives,
                                      IReadOnlyList<FrigFile.IkJob> ikJobs, FrigFile frig,
                                      IReadOnlyList<FrdvFile.Op> helpBones,
                                      float fps = 59.94f, int step = 1)
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
}
