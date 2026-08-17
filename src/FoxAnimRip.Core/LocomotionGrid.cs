// SPDX-License-Identifier: MIT
using System.Text.RegularExpressions;

namespace FoxAnimRip;

/// <summary>One clip's place in a locomotion grid.</summary>
public sealed record GridClip(string Name, string Family, string Verb, string Phase,
                              int Angle, string Foot, bool Reverse);

/// <summary>
/// A complete movement set: one family, one speed, every direction.
/// </summary>
public sealed class LocomotionGrid
{
    public string Family = "";              // everything before the verb
    public string Verb = "";                // wk, rn, jg...
    public readonly List<GridClip> Clips = new();

    public IEnumerable<int> Angles => Clips.Select(c => c.Angle).Distinct().OrderBy(a => a);
    public int Count(string phase) => Clips.Count(c => c.Phase == phase);

    /// <summary>Eight directions with starts, loops, turns and stops: the whole graph.</summary>
    public bool Complete => Count("st") >= 8 && Count("lp") >= 8
                            && Count("tn") >= 16 && Count("ed") >= 8;

    public override string ToString() =>
        $"{Family}_{Verb}: {Clips.Count} clips "
        + $"(st {Count("st")}, lp {Count("lp")}, tn {Count("tn")}, ed {Count("ed")})"
        + (Complete ? "  COMPLETE" : "");
}

/// <summary>
/// Finding locomotion by structure instead of by guessing at abbreviations.
///
/// <see cref="Locomotion"/> matches name fragments -- "wal", "run", "crc" -- which
/// both over- and under-matches, and there is nothing in the game files marking a
/// clip as locomotion, so that was the honest best a filter could do.
///
/// It is not the best a *reader* can do. Fox Engine names movement clips
/// systematically: a verb (wk, rn), a phase (st, lp, tn, ed), an angle, and for
/// starts and turns the foot that leads. Group by everything before the verb and
/// the grids fall out whole -- for the Phantom Pain player, four of them, 8
/// directions each, 290 clips with no gaps and nothing spurious. That is not a
/// heuristic about what a name means; it is the shape the names actually have,
/// and a grid that comes back incomplete says so rather than pretending.
/// </summary>
public static class LocomotionGrids
{
    private static readonly Regex Pattern = new(
        @"^(?<family>.+?)_(?<verb>wk|rn|jg)_(?<phase>st|lp|tn|ed)_(?<tail>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Tails look like: "l45", "-135", "0_r", "90_l_ed", "45_rev", "180"
    private static readonly Regex Tail = new(
        @"^(?<foot>[lr])?(?<angle>-?\d+)(?:_(?<extra>.+))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static GridClip Parse(string name)
    {
        var m = Pattern.Match(name);
        if (!m.Success) return null;
        var t = Tail.Match(m.Groups["tail"].Value);
        if (!t.Success) return null;
        if (!int.TryParse(t.Groups["angle"].Value, out var angle)) return null;

        var extra = t.Groups["extra"].Success ? t.Groups["extra"].Value.ToLowerInvariant() : "";
        var foot = t.Groups["foot"].Success ? t.Groups["foot"].Value.ToLowerInvariant() : "";
        if (foot.Length == 0)
        {
            // Turns carry the lead foot after the angle rather than before it.
            if (extra.StartsWith("l")) foot = "l";
            else if (extra.StartsWith("r") && !extra.StartsWith("rev")) foot = "r";
        }

        return new GridClip(name, m.Groups["family"].Value.ToLowerInvariant(),
                            m.Groups["verb"].Value.ToLowerInvariant(),
                            m.Groups["phase"].Value.ToLowerInvariant(),
                            angle, foot, extra.Contains("rev"));
    }

    /// <summary>Every locomotion grid in a set of clip names, largest first.</summary>
    public static List<LocomotionGrid> Find(IEnumerable<string> names, int minClips = 8)
    {
        var grids = new Dictionary<(string, string), LocomotionGrid>();
        foreach (var name in names)
        {
            var clip = Parse(name);
            if (clip is null) continue;
            var key = (clip.Family, clip.Verb);
            if (!grids.TryGetValue(key, out var grid))
                grids[key] = grid = new LocomotionGrid { Family = clip.Family, Verb = clip.Verb };
            grid.Clips.Add(clip);
        }

        return grids.Values
            .Where(g => g.Clips.Count >= minClips)
            .OrderByDescending(g => g.Complete)
            .ThenByDescending(g => g.Clips.Count)
            .ToList();
    }

    /// <summary>The clip names belonging to any detected grid.</summary>
    public static HashSet<string> Members(IEnumerable<string> names, bool completeOnly = false)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var grid in Find(names))
        {
            if (completeOnly && !grid.Complete) continue;
            foreach (var clip in grid.Clips) set.Add(clip.Name);
        }
        return set;
    }
}
