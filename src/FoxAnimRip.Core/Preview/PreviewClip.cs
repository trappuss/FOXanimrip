// SPDX-License-Identifier: MIT
using System.Numerics;
using FoxBrowser.Models.Anim;
using FoxBrowser.Models.Fmdl;

namespace FoxAnimRip.Preview;

/// <summary>
/// One clip, posed on demand.
///
/// This deliberately does not go anywhere near the FBX path. It calls the same
/// solve the game does -- <c>AnimSkinner.BuildPalette</c>, with the character's
/// own rig drives, IK jobs and help-bone operators -- and hands back the bone
/// palette. So what the preview shows is the animation as the engine resolves
/// it, not as an exporter re-encoded it. When the two disagree, the preview is
/// the one to believe.
///
/// Poses are cached per frame. A clip is a few hundred frames of a hundred-odd
/// 64-byte matrices -- a couple of megabytes at the very worst -- and caching
/// means scrubbing the timeline backwards costs nothing.
/// </summary>
public sealed class PreviewClip
{
    private readonly FmdlModel _model;
    private readonly GaniAnimation _anim;
    private readonly IReadOnlyDictionary<int, FrigFile.BoneDrive> _drives;
    private readonly IReadOnlyList<FrigFile.IkJob> _ikJobs;
    private readonly FrigFile _frig;
    private readonly IReadOnlyList<FrdvFile.Op> _help;
    private readonly Matrix4x4[][] _cache;
    private readonly Vector3[][] _bonePositions;

    public string Name { get; }
    public int FrameCount { get; }
    public float Fps { get; }
    public int MatchedBones { get; }

    /// <summary>
    /// Let the character travel instead of running on the spot.
    ///
    /// Switching this clears the cache, because every pose in it was solved the
    /// other way.
    /// </summary>
    public bool RootMotion
    {
        get => _rootMotion;
        set
        {
            if (_rootMotion == value) return;
            _rootMotion = value;
            Array.Clear(_cache);
            Array.Clear(_bonePositions);
        }
    }

    private bool _rootMotion;

    public PreviewClip(string name, FmdlModel model, GaniAnimation anim, int[] boneNameIndex,
                       FrigFile frig, IReadOnlyList<FrdvFile.Op> help, float fps = 59.94f)
    {
        Name = name;
        _model = model;
        _anim = anim;
        _frig = frig;
        _help = help;
        Fps = fps <= 0 ? 59.94f : fps;
        FrameCount = Math.Max(1, anim.FrameCount);

        MatchedBones = RipJob.ResolveBest(anim, model, boneNameIndex, frig,
                                          out var drives, out var ikJobs);
        _drives = drives;
        _ikJobs = ikJobs;
        _cache = new Matrix4x4[FrameCount][];
        _bonePositions = new Vector3[FrameCount][];
    }

    /// <summary>The bone palette at a frame: bind-inverse times animated world.</summary>
    public Matrix4x4[] Palette(int frame)
    {
        frame = Wrap(frame);
        return _cache[frame] ?? Solve(frame).Palette;
    }

    /// <summary>Animated bone positions at a frame, for the skeleton overlay.</summary>
    public Vector3[] BonePositions(int frame)
    {
        frame = Wrap(frame);
        return _bonePositions[frame] ?? Solve(frame).Bones;
    }

    private int Wrap(int frame)
    {
        if (FrameCount <= 0) return 0;
        frame %= FrameCount;
        return frame < 0 ? frame + FrameCount : frame;
    }

    private (Matrix4x4[] Palette, Vector3[] Bones) Solve(int frame)
    {
        var solved = PoseGate.Run(() =>
        {
            var palette = AnimSkinner.BuildPalette(_model, _anim, frame, _drives,
                                                   applyGaniTranslation: _rootMotion,
                                                   _ikJobs, _frig, _help);
            // LastBonePos is written by the same call and is only valid until
            // the next one, so copy it while the gate is still held.
            var bones = AnimSkinner.LastBonePos is { } positions
                ? (Vector3[])positions.Clone()
                : Array.Empty<Vector3>();
            return (palette, bones);
        });

        _cache[frame] = solved.palette;
        _bonePositions[frame] = solved.bones;
        return solved;
    }
}
