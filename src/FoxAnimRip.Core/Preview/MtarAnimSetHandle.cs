// SPDX-License-Identifier: MIT
using FoxBrowser.Models.Anim;

namespace FoxAnimRip.Preview;

/// <summary>
/// An animation archive held open for browsing.
///
/// The export path loads a set, walks it once and drops it. Previewing is the
/// opposite shape: the set stays open while someone steps up and down the list,
/// and only the clip they land on is decoded. Naming the clips is cheap --
/// that comes from the archive's directory -- so the list appears immediately
/// and the cost of a clip is paid when it is actually looked at.
///
/// Not thread-safe on purpose: one of these belongs to one preview window.
/// </summary>
public sealed class MtarAnimSetHandle
{
    private readonly MtarAnimSet _set;
    private readonly Dictionary<int, PreviewClip> _decoded = new();

    public string Name { get; }
    public IReadOnlyList<string> ClipNames { get; }

    /// <summary>The full path the game's hash dictionary resolves, where it can.</summary>
    public IReadOnlyList<string> ClipPaths { get; }

    private MtarAnimSetHandle(string name, MtarAnimSet set)
    {
        Name = name;
        _set = set;
        var names = new List<string>(set.Entries.Count);
        var paths = new List<string>(set.Entries.Count);
        foreach (var entry in set.Entries)
        {
            names.Add(RipJob.StripExt(entry.Name));
            paths.Add(entry.FullPath ?? "");
        }
        ClipNames = names;
        ClipPaths = paths;
    }

    public static MtarAnimSetHandle Open(MtarSource source) =>
        new(source.Name, MtarAnimSet.Load(source.Open()));

    /// <summary>Decode a clip and bind it to a character, once.</summary>
    public PreviewClip Clip(int index, ModelContext context)
    {
        if (_decoded.TryGetValue(index, out var cached)) return cached;
        if (index < 0 || index >= _set.Entries.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var entry = _set.Entries[index];
        var clip = new PreviewClip(ClipNames[index], context.Model, entry.Animation.Value,
                                   context.BoneNameIndex, context.Frig, context.HelpBoneOps);

        // Poses are cached per frame inside a clip, so a big set browsed for a
        // long time would otherwise only ever grow. Keep the recent ones.
        if (_decoded.Count > 24) _decoded.Clear();
        _decoded[index] = clip;
        return clip;
    }
}
