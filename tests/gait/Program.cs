// SPDX-License-Identifier: MIT
// Synthetic checks for RootBake.GaitSpeed: a fabricated in-place walk whose
// stance feet slide back at a known speed must measure as that speed, and the
// degenerate cases (idle, missing feet) must come back zero.
using System.Numerics;
using FoxAnimRip;

var failures = 0;

void Check(string name, bool ok)
{
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}");
    if (!ok) failures++;
}

// -- an in-place walk: root pinned at origin, feet alternating. During stance
// the foot is on the ground (y=0) moving backward at `speed`; during swing it
// arcs forward above ground. Y-up, travel along Z.
Vector3[][] Walk(float speed, float fps, int frames, bool travellingRoot = false)
{
    const int Bones = 4;                 // 0 root, 2 lfoot, 3 rfoot
    var world = new Vector3[Bones][];
    for (var b = 0; b < Bones; b++) world[b] = new Vector3[frames];
    var half = 0.5f;                     // half a cycle per stance
    var cycleFrames = (int)(fps);        // 1 s cycle
    for (var f = 0; f < frames; f++)
    {
        var t = f / fps;
        var rootZ = travellingRoot ? speed * t : 0f;
        world[0][f] = new Vector3(0, 0.9f, rootZ);
        for (var foot = 0; foot < 2; foot++)
        {
            var phase = (f + foot * cycleFrames / 2) % cycleFrames
                        / (float)cycleFrames;
            Vector3 p;
            if (phase < half)
            {
                // stance: planted in world while the body passes over it. In a
                // travelling walk the world position is constant; in-place it
                // slides backward under the pinned root at -speed.
                var s = phase / half;                       // 0..1 through stance
                var zRel = speed * half * (0.5f - s);       // +ahead -> -behind
                p = new Vector3(foot == 0 ? 0.1f : -0.1f, 0f, rootZ + zRel);
            }
            else
            {
                // swing: forward above the ground.
                var s = (phase - half) / half;
                var zRel = speed * half * (s - 0.5f);
                p = new Vector3(foot == 0 ? 0.1f : -0.1f,
                                0.15f * MathF.Sin(s * MathF.PI), rootZ + zRel);
            }
            world[2 + foot][f] = p;
        }
    }
    return world;
}

// A 1.2 m/s in-place walk measures as 1.2 m/s (10% tolerance: the synthetic
// stance has discrete frames and the estimator takes a median).
var (v1, n1) = RootBake.GaitSpeed(Walk(1.2f, 60, 180), 2, 3, 60);
Check($"in-place walk 1.2 -> {v1:0.###} ({n1} samples)", MathF.Abs(v1 - 1.2f) < 0.12f && n1 > 20);

// The same walk with the root actually travelling measures the same.
var (v2, n2) = RootBake.GaitSpeed(Walk(1.2f, 60, 180, travellingRoot: true), 2, 3, 60);
Check($"travelling walk 1.2 -> {v2:0.###} ({n2} samples)", MathF.Abs(v2 - 1.2f) < 0.12f);

// Faster gait, faster answer, and ordering holds.
var (v3, _) = RootBake.GaitSpeed(Walk(3.4f, 60, 180), 2, 3, 60);
Check($"dash 3.4 -> {v3:0.###}", MathF.Abs(v3 - 3.4f) < 0.34f && v3 > v1);

// An idle (feet never lift) is 0, not noise.
var idle = Walk(0f, 60, 120);
var (v4, _) = RootBake.GaitSpeed(idle, 2, 3, 60);
Check($"idle -> {v4:0.###}", v4 == 0f);

// Feet not found: 0, no crash.
var (v5, n5) = RootBake.GaitSpeed(Walk(1.2f, 60, 180), -1, -1, 60);
Check($"no feet -> {v5:0.###}/{n5}", v5 == 0f && n5 == 0);

// Too short to say anything: 0.
var (v6, _) = RootBake.GaitSpeed(Walk(1.2f, 60, 4), 2, 3, 60);
Check($"4 frames -> {v6:0.###}", v6 == 0f);

Console.WriteLine(failures == 0 ? "all passed" : $"{failures} FAILED");
return failures;
