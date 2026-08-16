// SPDX-License-Identifier: MIT
using System.Security.Cryptography;
using FoxBrowser.Models.Export;

namespace FoxAnimRip;

/// <summary>
/// Spots clips that are, for practical purposes, the same animation.
///
/// Fox Engine ships a lot of near-identical variants -- the same motion at
/// eight facing angles, with and without an ``_ed`` tail, mirrored left and
/// right. Some of those really are distinct; plenty are byte-for-byte the same
/// pose data under different names, and a 4,000-clip library is a lot smaller
/// once they are folded together.
///
/// The test is a quantised signature over the baked pose: sample a fixed number
/// of frames, round every bone's rotation and translation to the tolerance, and
/// hash the result. To stop two clips that differ by a hair from landing either
/// side of a rounding boundary, every clip is hashed twice on grids offset by
/// half a bucket, and a hit on either counts.
/// </summary>
public sealed class ClipDedupe
{
    private const int SampleFrames = 16;

    private readonly float _rotStep;
    private readonly float _posStep;
    private readonly Dictionary<string, string> _seen = new(StringComparer.Ordinal);

    public int Dropped { get; private set; }

    /// <param name="rotToleranceDeg">Rotation difference treated as identical.</param>
    /// <param name="posTolerance">Translation difference treated as identical.</param>
    public ClipDedupe(float rotToleranceDeg = 0.5f, float posTolerance = 0.001f)
    {
        _rotStep = MathF.Max(0.001f, rotToleranceDeg);
        _posStep = MathF.Max(0.00001f, posTolerance);
    }

    /// <summary>
    /// The name of the clip this one duplicates, or null when it is new.
    /// A new clip is remembered under <paramref name="name"/>.
    /// </summary>
    public string DuplicateOf(string name, ExportClip clip)
    {
        var a = Signature(clip, 0f);
        var b = Signature(clip, 0.5f);

        if (_seen.TryGetValue(a, out var first) || _seen.TryGetValue(b, out first))
        {
            Dropped++;
            return first;
        }

        _seen[a] = name;
        _seen[b] = name;
        return null;
    }

    private string Signature(ExportClip clip, float offset)
    {
        var frames = Math.Max(1, clip.FrameCount);
        var bones = clip.RotationEuler?.Length ?? 0;
        var take = Math.Min(SampleFrames, frames);

        var buffer = new byte[8 + take * bones * 6 * 4];
        BitConverter.GetBytes(frames).CopyTo(buffer, 0);
        BitConverter.GetBytes(bones).CopyTo(buffer, 4);

        var at = 8;
        for (var s = 0; s < take; s++)
        {
            // Even spread across the clip, always including first and last.
            var frame = take == 1 ? 0 : (int)((long)s * (frames - 1) / (take - 1));
            for (var b = 0; b < bones; b++)
            {
                var rot = clip.RotationEuler[b];
                var pos = clip.Translation?[b];
                var f = rot is not null && frame < rot.Length ? frame : 0;

                if (rot is { Length: > 0 })
                {
                    var v = rot[f];
                    BitConverter.GetBytes(Quantise(v.X, _rotStep, offset)).CopyTo(buffer, at);
                    BitConverter.GetBytes(Quantise(v.Y, _rotStep, offset)).CopyTo(buffer, at + 4);
                    BitConverter.GetBytes(Quantise(v.Z, _rotStep, offset)).CopyTo(buffer, at + 8);
                }
                at += 12;

                if (pos is { Length: > 0 })
                {
                    var v = pos[Math.Min(f, pos.Length - 1)];
                    BitConverter.GetBytes(Quantise(v.X, _posStep, offset)).CopyTo(buffer, at);
                    BitConverter.GetBytes(Quantise(v.Y, _posStep, offset)).CopyTo(buffer, at + 4);
                    BitConverter.GetBytes(Quantise(v.Z, _posStep, offset)).CopyTo(buffer, at + 8);
                }
                at += 12;
            }
        }

        return Convert.ToHexString(SHA256.HashData(buffer));
    }

    private static int Quantise(float value, float step, float offset) =>
        (int)MathF.Floor(value / step + offset);
}
