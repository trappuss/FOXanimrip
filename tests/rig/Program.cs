// SPDX-License-Identifier: MIT
//
// Which .frig belongs to a skeleton.
//
//     dotnet run --project tests/rig
//
// Two real cases, from two opposite failures, and the rule has to satisfy both.
// Neither needs game files: the numbers are what matters, and they were measured
// from actual rips.
//
//   1. A 144-bone rig was handed to a 94-bone soldier because it was large.
//      It contains every one of that soldier's standard SKL_ names, so it looks
//      perfect by "how much of the model does this cover" -- 100% -- and its
//      foreign rig units then dragged the neck and help bones out of the body.
//
//   2. Fixing that with a coverage floor broke the opposite case. A rig only
//      describes the bones it *drives*; the real rig for the 120-bone player
//      model names 53. Coverage 44%, under any sensible floor, so the correct
//      rig was rejected and the character was left with none -- and the player's
//      animation archives are entirely rig-driven, so nothing played at all.
//
// The rule that satisfies both is precision: what share of the *rig's* bones
// this skeleton has. Case 1 is 65%, case 2 is 100%.

using FoxAnimRip;

var failures = new List<string>();
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
    if (!ok) failures.Add(what);
}

static Sources.FrigChoice Rig(int matched, int rigBones, int modelBones,
                              double affinity = 0) =>
    new(Array.Empty<byte>(), "", matched, rigBones, modelBones, affinity);

// -- case 2: the player's own rig, measured from a FoxBrowser rip of
//    skl0_main0_def_f -- 18 units, 56 segments, 53 rig bones, 120-bone model.
var playerRig = Rig(matched: 53, rigBones: 53, modelBones: 120);
Console.WriteLine($"player rig      : precision {playerRig.Precision:P0}, "
                  + $"coverage {playerRig.Coverage:P0}");
Check(playerRig.Precision > 0.999, "the player's own rig is wholly on its skeleton");
Check(playerRig.Coverage < 0.5, "...while driving under half the model's bones");
Check(Sources.Believable(playerRig),
      "a rig that drives only part of a skeleton is still accepted");

// -- case 1: the foreign rig that caused the stretching.
var foreignRig = Rig(matched: 94, rigBones: 144, modelBones: 94);
Console.WriteLine($"foreign big rig : precision {foreignRig.Precision:P0}, "
                  + $"coverage {foreignRig.Coverage:P0}");
Check(foreignRig.Coverage > 0.999, "the foreign rig covers the whole model");
Check(foreignRig.Precision < 0.7, "...but a third of it is bones this model lacks");
Check(!Sources.Believable(foreignRig), "the foreign rig is rejected");

// The soldier's own rig, competing against that foreign one.
var soldierRig = Rig(matched: 50, rigBones: 50, modelBones: 94);
Check(Sources.Believable(soldierRig), "the soldier's own rig is accepted");
Check(Sources.Prefers(soldierRig, null), "...and is taken when nothing else is");

// -- a rig can be wholly contained and still useless.
var tinyRig = Rig(matched: 5, rigBones: 5, modelBones: 120);
Check(Sources.Believable(tinyRig), "a small fully-contained rig is believable");
Check(Sources.Prefers(playerRig, tinyRig),
      "but the rig driving more of the skeleton wins");
Check(!Sources.Prefers(tinyRig, playerRig), "and the comparison is not symmetric");

// -- ties break on precision, then on the file name looking right.
var equalA = Rig(matched: 40, rigBones: 40, modelBones: 100);
var equalB = Rig(matched: 40, rigBones: 44, modelBones: 100);
Check(Sources.Prefers(equalA, equalB), "at equal reach, the tighter rig wins");

var namedA = Rig(matched: 40, rigBones: 40, modelBones: 100, affinity: 1.0);
var namedB = Rig(matched: 40, rigBones: 40, modelBones: 100, affinity: 0.0);
Check(Sources.Prefers(namedA, namedB),
      "and otherwise the one named after the model wins");

// -- guards
Check(!Sources.Believable(Rig(2, 2, 120)),
      "two bones is not enough to call a rig a match");
Check(!Sources.Believable(null), "a missing rig is not believable");

Console.WriteLine();
Console.WriteLine(failures.Count == 0
    ? "PASSED (0 failures)"
    : $"FAILED ({failures.Count} failure(s))");
foreach (var failure in failures) Console.WriteLine("  - " + failure);
return failures.Count == 0 ? 0 : 1;
