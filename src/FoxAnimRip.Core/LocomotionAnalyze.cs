// SPDX-License-Identifier: MIT
using System.Globalization;

namespace FoxAnimRip;

/// <summary>
/// The cruise-speed table: <c>locomotion-params.tsv</c> files in, one grouped
/// summary out.
///
/// <c>--measure</c> writes one row per clip. This reads any number of those
/// tables (pass files, or a folder to search), groups the rows by what the clip
/// names say they are -- family, gait verb (wk/rn/jg/dh), phase (st/lp/tn/ed),
/// angle -- and reports the median measured speed and turn rate per group. The
/// output is <c>cruise-table.tsv</c>: the numeric backbone for rebuilding the
/// movement, and a cross-game consistency check (the same gait should land on
/// the same speed in TPP, GZ and Survive).
///
/// Everything reported is an aggregate of measured values. The one
/// interpretation this file makes is splitting a clip name into tokens; the
/// tokens themselves are reported verbatim.
/// </summary>
public static class LocomotionAnalyze
{
    private static readonly string[] Verbs = { "wk", "rn", "jg", "dh" };
    private static readonly string[] Phases = { "st", "lp", "tn", "ed" };

    private sealed record Row(string Label, string Mtar, string Clip,
                              float RootSpeed, float NetYaw, float TurnRate,
                              float Gait, int GaitSamples);

    private sealed record Key(string Label, string Family, string Stance,
                              string Verb, string Phase, int Angle);

    public static int Run(IReadOnlyList<string> inputs, string outDir,
                          Action<string> log)
    {
        // -- find the tables
        var files = new List<string>();
        foreach (var input in inputs)
        {
            if (File.Exists(input)) files.Add(input);
            else if (Directory.Exists(input))
                files.AddRange(Directory.GetFiles(input, "locomotion-params.tsv",
                                                  SearchOption.AllDirectories));
            else log($"! {input}: not a file or folder, skipped");
        }
        files = files.Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0)
        {
            log("! no locomotion-params.tsv found - run a --measure pass first "
                + "(test-measure-locomotion.bat)");
            return 66;
        }

        // -- read them
        var rows = new List<Row>();
        var noGaitColumn = 0;
        foreach (var file in files)
        {
            var label = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(file)))
                        ?? Path.GetFileNameWithoutExtension(file);
            var lines = File.ReadAllLines(file);
            if (lines.Length < 2) continue;
            var col = Columns(lines[0]);
            if (!col.ContainsKey("clip") || !col.ContainsKey("speed_mps"))
            {
                log($"! {file}: not a locomotion-params table, skipped");
                continue;
            }
            var hasGait = col.ContainsKey("gait_mps");
            if (!hasGait) noGaitColumn++;

            for (var i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split('\t');
                if (parts.Length <= col["speed_mps"]) continue;
                rows.Add(new Row(label,
                    Cell(parts, col, "mtar"), Cell(parts, col, "clip"),
                    Num(parts, col, "speed_mps"), Num(parts, col, "netyaw_deg"),
                    Num(parts, col, "turnrate_dps"),
                    hasGait ? Num(parts, col, "gait_mps") : 0f,
                    hasGait ? (int)Num(parts, col, "gaitsamples") : 0));
            }
            log($"{label}: {lines.Length - 1} row(s) from {file}"
                + (hasGait ? "" : "  (old table: no gait_mps column)"));
        }

        // -- group by what the names say
        var groups = new Dictionary<Key, List<Row>>();
        var unparsed = 0;
        foreach (var row in rows)
        {
            var key = Parse(row.Label, row.Clip);
            if (key is null) { unparsed++; continue; }
            if (!groups.TryGetValue(key, out var list))
                groups[key] = list = new List<Row>();
            list.Add(row);
        }

        // -- the table
        var outLines = new List<string>
        {
            "label\tfamily\tstance\tverb\tphase\tangle\tclips\tgaitMeasured\t"
            + "speed_mps\tspeed_min\tspeed_max\tturnRate_dps\tnetYaw_deg"
        };
        foreach (var (key, list) in groups
                     .OrderBy(g => g.Key.Label, StringComparer.Ordinal)
                     .ThenBy(g => g.Key.Family, StringComparer.Ordinal)
                     .ThenBy(g => g.Key.Verb, StringComparer.Ordinal)
                     .ThenBy(g => g.Key.Phase, StringComparer.Ordinal)
                     .ThenBy(g => g.Key.Angle))
        {
            // A cycle's authored speed is the stance-foot number when the
            // measure pass produced one; the root's travel otherwise (real for
            // travelling clips, ~0 for in-place cycles measured by an old exe).
            var speeds = list.Select(r => r.Gait > 0 ? r.Gait : r.RootSpeed)
                             .OrderBy(s => s).ToList();
            var gaitN = list.Count(r => r.Gait > 0);
            outLines.Add(string.Create(CultureInfo.InvariantCulture,
                $"{key.Label}\t{key.Family}\t{key.Stance}\t{key.Verb}\t"
                + $"{key.Phase}\t{key.Angle}\t{list.Count}\t{gaitN}\t"
                + $"{Median(speeds):0.###}\t{speeds[0]:0.###}\t{speeds[^1]:0.###}\t"
                + $"{Median(list.Select(r => r.TurnRate).OrderBy(v => v).ToList()):0.##}\t"
                + $"{Median(list.Select(r => r.NetYaw).OrderBy(v => v).ToList()):0.##}"));
        }

        if (outDir.Length == 0)
            outDir = Path.GetDirectoryName(Path.GetFullPath(files[0])) ?? ".";
        Directory.CreateDirectory(outDir);
        var outFile = Path.Combine(outDir, "cruise-table.tsv");
        File.WriteAllLines(outFile, outLines);

        // -- the headline: forward loop speed per gait, the blendspace anchors
        log("");
        log("cruise speeds (forward loops, median measured m/s):");
        var headline = groups
            .Where(g => g.Key.Phase == "lp" && Math.Abs(g.Key.Angle) == 0)
            .OrderBy(g => g.Key.Label, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Verb, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Family, StringComparer.Ordinal)
            .ToList();
        if (headline.Count == 0)
            log("  (no forward loop rows - the inputs may predate gait "
                + "measurement, or loops were filtered out of the measure run)");
        foreach (var (key, list) in headline)
        {
            var gait = list.Where(r => r.Gait > 0).Select(r => r.Gait)
                           .OrderBy(v => v).ToList();
            log(gait.Count > 0
                ? string.Create(CultureInfo.InvariantCulture,
                    $"  {key.Label}  {key.Family}  {key.Verb}: "
                    + $"{Median(gait):0.###} m/s  (clips {list.Count}, "
                    + $"gait-measured {gait.Count})")
                : $"  {key.Label}  {key.Family}  {key.Verb}: no gait data - "
                  + "re-run the measure with a 1.19+ exe");
        }

        log("");
        log($"{rows.Count} row(s) read, {groups.Count} group(s), "
            + $"{unparsed} clip(s) outside the wk/rn/jg/dh grammar (not gait "
            + $"cycles; listed in no group) -> {outFile}");
        if (noGaitColumn > 0)
            log($"! {noGaitColumn} table(s) have no gait_mps column: in-place "
                + "cycles in them show speed 0. Re-run test-measure-locomotion "
                + "with the current exe, then run this again.");
        return rows.Count > 0 ? 0 : 3;
    }

    /// <summary>
    /// Split a clip name on the gait grammar: <c>..._&lt;verb&gt;_..._&lt;phase&gt;_&lt;tail&gt;</c>
    /// with verb in wk/rn/jg/dh and phase in st/lp/tn/ed. Modifier tokens
    /// between verb and phase (slope grade, u/d, weapon state) stay in the
    /// family so unlike cycles never share a group. Null when the name has no
    /// verb+phase pair -- that clip is not a gait cycle.
    /// </summary>
    private static Key Parse(string label, string clip)
    {
        var (family, stance, verb, phase, angle) = ParseClip(clip);
        if (verb.Length == 0) return null;
        return new Key(label, family, stance, verb, phase, angle);
    }

    /// <summary>
    /// The gait-grammar split on its own, for anything that needs to know what
    /// a clip name says it is (the analyzer, the runtime-pack exporter).
    /// Returns empty-verb when the name has no verb+phase pair.
    /// </summary>
    public static (string Family, string Stance, string Verb, string Phase,
                   int Angle) ParseClip(string clip)
    {
        var parts = clip.ToLowerInvariant().Split('_');
        var vi = Array.FindIndex(parts, p => Verbs.Contains(p));
        var pi = vi < 0 ? -1
               : Array.FindIndex(parts, vi + 1, p => Phases.Contains(p));
        if (vi < 0 || pi < 0) return ("", "", "", "", 0);

        var family = string.Join("_",
            parts.Take(vi).Concat(parts.Skip(vi + 1).Take(pi - vi - 1)));
        var stance = parts.Length > 1 && parts[1].Length == 1
                     && "sqpc".Contains(parts[1][0]) ? parts[1] : "";

        var angle = 0;
        for (var i = pi + 1; i < parts.Length; i++)
        {
            var token = parts[i];
            if (token.Length > 1 && (token[0] == 'l' || token[0] == 'r')
                && int.TryParse(token.AsSpan(1), out var a1)) { angle = a1; break; }
            if (int.TryParse(token, out var a0)) { angle = a0; break; }
        }
        return (family, stance, parts[vi], parts[pi], angle);
    }

    private static Dictionary<string, int> Columns(string header)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var parts = header.Split('\t');
        for (var i = 0; i < parts.Length; i++) map[parts[i].Trim()] = i;
        return map;
    }

    private static string Cell(string[] parts, Dictionary<string, int> col, string name)
        => col.TryGetValue(name, out var i) && i < parts.Length ? parts[i] : "";

    private static float Num(string[] parts, Dictionary<string, int> col, string name)
        => float.TryParse(Cell(parts, col, name), NumberStyles.Float,
                          CultureInfo.InvariantCulture, out var v) ? v : 0f;

    private static float Median(List<float> sorted)
        => sorted.Count == 0 ? 0f
         : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2]
         : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2f;
}
