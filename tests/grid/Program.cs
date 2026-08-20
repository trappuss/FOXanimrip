// SPDX-License-Identifier: MIT
//
// Locomotion grid detection, against real clip names.
//
//     dotnet run --project tests/grid -- clips.txt
//
// With no argument it runs a small built-in sample. Pass a file of clip names
// (one per line, as --list-clips prints them) to check a real archive.
//
// The point of this over the old name-fragment filter: a fragment list can only
// be judged by eye, whereas a grid either closes or it does not. The assertions
// below are about structure -- eight directions, starts matched by stops, turns
// carrying a lead foot -- so a parser that quietly drops half a family fails
// here instead of silently exporting half a movement set.

using FoxAnimRip;

var failures = new List<string>();
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
    if (!ok) failures.Add(what);
}

// A complete family, written out the way the game names them.
var sample = new List<string>();
int[] angles = { 0, 45, 90, 135, 180, -45, -90, -135 };
foreach (var a in angles)
{
    var foot = a > 0 ? "l" : "r";
    sample.Add($"snaprdy_s_fre0_wk_st_{foot}{a}");
    sample.Add($"snaprdy_s_fre0_wk_lp_{a}");
    sample.Add($"snaprdy_s_fre0_wk_ed_{a}");
    sample.Add($"snaprdy_s_fre0_wk_ed_{a}_rev");
    foreach (var f in new[] { "l", "r" })
    {
        sample.Add($"snaprdy_s_fre0_wk_tn_{a}_{f}");
        sample.Add($"snaprdy_s_fre0_wk_tn_{a}_{f}_ed");
    }
}
// Things that must NOT be swept up.
sample.AddRange(new[]
{
    "snapdam_q_fls_lp", "snapcqc_s_kck_l", "snapnon_q_pat_dog_bl",
    "snapc4_s_set_cal", "snapidr_s_jsc_u", "snapnon_s_idl_l",
});

var grids = LocomotionGrids.Find(sample);
Console.WriteLine($"built-in sample: {sample.Count} names -> {grids.Count} grid(s)");
foreach (var g in grids) Console.WriteLine("   " + g);

Check(grids.Count == 1, "one family produces one grid");
if (grids.Count > 0)
{
    var g = grids[0];
    Check(g.Complete, "the family is recognised as complete");
    Check(g.Count("st") == 8, $"8 starts ({g.Count("st")})");
    Check(g.Count("lp") == 8, $"8 loops ({g.Count("lp")})");
    Check(g.Count("tn") == 32, $"32 turns ({g.Count("tn")})");
    Check(g.Count("ed") == 16, $"16 stops ({g.Count("ed")})");
    Check(g.Angles.Count() == 8, "8 distinct directions");
    Check(g.Clips.Where(c => c.Phase == "tn").All(c => c.Foot is "l" or "r"),
          "every turn carries a lead foot");
    Check(g.Clips.Count(c => c.Phase == "ed" && c.Reverse) == 8,
          "half the stops are the reverse variant");
    Check(g.Clips.All(c => c.Verb == "wk"), "the grid holds one verb only");
}

var members = LocomotionGrids.Members(sample);
Check(!members.Contains("snapcqc_s_kck_l") && !members.Contains("snapnon_q_pat_dog_bl"),
      "non-locomotion clips are not swept in");
Check(!members.Contains("snapnon_s_idl_l"),
      "an idle with no direction is not called locomotion");

// -- a real archive, if one was handed over
var argv = Environment.GetCommandLineArgs();
var file = argv.FirstOrDefault(a => a.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
if (file is not null && File.Exists(file))
{
    var names = File.ReadAllLines(file)
        .Select(l => l.Split('\t').Length > 1 ? l.Split('\t')[1] : l.Trim())
        .Where(l => l.Length > 0 && l != "clip")
        .ToList();
    var real = LocomotionGrids.Find(names);
    Console.WriteLine($"\n{file}: {names.Count} names -> {real.Count} grid(s)");
    foreach (var g in real) Console.WriteLine("   " + g);
    var covered = real.Sum(g => g.Clips.Count);
    Console.WriteLine($"   {covered} of {names.Count} clips are in a grid "
                      + $"({100.0 * covered / Math.Max(1, names.Count):0.#}%)");
    Check(real.Any(g => g.Complete), "at least one complete grid found in the real archive");
    Check(covered < names.Count,
          "not everything is claimed as locomotion (that would mean the parser is too loose)");
}

Console.WriteLine();
Console.WriteLine(failures.Count == 0
    ? "PASSED (0 failures)" : $"FAILED ({failures.Count} failure(s))");
foreach (var f in failures) Console.WriteLine("  - " + f);
return failures.Count == 0 ? 0 : 1;
