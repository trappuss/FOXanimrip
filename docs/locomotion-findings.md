# What MGSV's locomotion data actually says

Findings from measuring the movement animation of *Metal Gear Solid V: The
Phantom Pain*, *Ground Zeroes* and *Metal Gear Survive* directly out of the
shipped game files.

The goal at the time was a 1:1 recreation of MGSV's player movement. That build
(a standalone test bench) has been scrapped, but the *measurements* stand on
their own and are reproducible with the tools still in this repo. Everything
below is measured or decoded from the games; nothing is estimated, and where
the data has no answer it says so.

---

## 1. The motion graph: referenced everywhere, located nowhere

Fox Engine drives locomotion from a motion graph — the state machine deciding
which clip plays, when it blends, and how far the character travels. Parsing it
was the obvious plan.

**The asset tree is real.** The path dictionary holds **989 paths** under
`/Assets/tpp/motion/motion_graph/`, organised per character
(`snak`, `player2`, `soldier2`, `quiet`, `animal`, …), and the names mirror the
`.mtar` animation sets exactly — `TppGzPlayer_layers` appears both as
`/Assets/tpp/motion/motion_graph/snak/TppGzPlayer_layers` and as
`/Assets/tpp/motion/mtar/player/TppGzPlayer_layers`.

**No file carrying the `.mog` extension code exists in any of the three games.**
`--dump-mog` finds zero across TPP, GZ and Survive. That test is sound: the same
extension-code bucketing simultaneously locates 19,138 `.fmdl` and 1,236 `.mtar`
in TPP alone, so the mechanism demonstrably works through the FPK containers.

**What this does and does not prove.** It proves nothing ships under extension
code 4752. It does *not* prove the graph is absent — the data may ship under a
different extension code, inside a container we do not descend, or be compiled
into the executable. An earlier version of this document claimed the stronger
result using `--ext-histogram` ("0 of ~963k files"); that argument was wrong,
because at the top level that histogram also reports zero `.fmdl` and zero
`.mtar` — those live inside FPK containers and do not carry usable extension
bits in that particular walk.

**The next experiment**, for anyone picking this up: take the known
`motion_graph` paths, compute `PathCode` for each against every one of the ~47
known extension codes (see the wiki's Archives and Hashing page), and probe the
archives for a hit. A match names the real extension in one run.

Until then the graph's *logic* is unavailable, and the reconstruction below
works from what the clips themselves reveal.

## 2. Gait cycles are authored in place — root travel is ~0

The first measurement pass baked each clip with its root translation kept and
recorded how far the root moved. For contact moves (throws, falls, grabs) this
gives real numbers. For **walking, running and dashing it gives almost
nothing**: a dash *start* covers 0.13 m across 43 frames, and the loops measure
essentially zero.

That is not a bug in the measurement. The cycles are authored on the spot and
the engine applies travel parametrically from the motion graph — the same graph
that does not ship. So root travel alone can never yield cruise speeds.

## 3. The authored speed is recoverable from the stance foot

The speed is still in the curves. While a foot is planted it is stationary in
the world, so in the character's own frame it sweeps backward under the root at
exactly the authored travel speed.

`--measure` therefore reports **`gait_mps`**: bake world positions, find the
`*LFOOT` / `*RFOOT` bones by their dictionary names, detect the stance phase by
foot height, and take the median backward sweep speed relative to the root.

Validation, in order of strength:

- **Synthetic**: a fabricated in-place walk built to slide its stance feet at
  exactly 1.2 m/s measures 1.2 m/s; travelling and in-place versions of the
  same cycle agree; idles report 0 rather than noise (`tests/gait`).
- **Cross-source**: TPP male and female are byte-identical, and GZ and Survive
  — separately authored games — land within 1–3 % of TPP on every shared gait.
- **Internal**: the `_rai` dash measures *faster* than the default dash, which
  matches the known faster-sprint outfit.
- **In an engine**: replayed in Blender against the same rig, in-scene stance
  speed matched the measured number to **0.35 % worst case** across four gaits,
  with travel distance exactly speed × time.

For clips whose root *does* travel, the same estimator returns the travel
speed, so it doubles as a cross-check.

## 4. The measured speed table

Median stance-foot speed of each flat forward loop. Family tokens verbatim:
`snapnon` = no weapon, `snaprdy…fre0` = weapon ready, `snapasr` = assault
rifle; stance letters `s` / `q` / `p` / `c` as the clips name them.

| gait | family, stance | m/s |
|------|----------------|-----|
| walk | snapnon_s (standing) | **1.117** |
| walk | snapnon_q (crouched) | 0.705 |
| walk | snapasr_p (prone) | 0.578 |
| walk | snapasr_c (crawl) | 0.354 |
| walk | snaprdy_s_fre0 (weapon ready) | 1.142 |
| jog  | snapnon_s | **2.308** |
| run  | snapnon_s (standing) | **3.955** |
| run  | snapnon_q (crouched) | 2.847 |
| run  | snaprdy_s_fre0 | 3.570 |
| dash | snapnon_s (sprint) | **6.394** |
| dash | snapnon_s_rai | 6.835 |

Directional loops hold speed nearly constant around the circle — standing walk
measures 1.08–1.16 across all eight directions, back-pedal 1.01 — so one cruise
speed per gait with per-direction clips is faithful; speed does not need to
vary by heading.

`--analyze-locomotion` regenerates the full table (`cruise-table.tsv`,
1,276 groups) from the measured TSVs, including the slope and stair sets
(`snapstr` / `snapslp`, 15° and 25°, up and down) and the turn loops with their
degrees per second.

## 5. The clip inventory *is* the motion graph's edge list

Fox names movement clips systematically:

```
snapnon_s_wk_st_l45_l
└ family ┘ │  │  │   └ lead foot
           │  │  └ angle
           │  └ phase: st start, lp loop, tn turn, ed stop
           └ verb: wk walk, rn run, jg jog, dh dash
```

The set of transition clips that exists tells you which state changes the game
actually performs, and each clip's measured distance, net yaw and duration pin
its timing. That is why a faithful reconstruction is possible without the
graph: the graph's *edges* are enumerable even though its logic is not.

Two corrections were needed to read it properly:

- The grammar accepts **`dh` (dash)** alongside wk/rn/jg — the player sets
  verifiably use it (`snapnon_s_dh_st_l0_l`).
- `--locomotion`'s token filter was **silently dropping the plain run and dash
  loops**: it matched `run`/`dsh` but the cycles are named `_rn_` / `_dh_` with
  `_lp_` for the loop, so `snapnon_s_rn_lp_...` — among the most important
  clips in the game — matched nothing.

## 6. Naming traps worth knowing

- **`skl0_main0_def` is not the player body.** It is the first-person arms
  model: `MESH_arm_0/1/2`, 1,378 vertices, one material named `view`, no
  textures. It carries the full 121-bone player skeleton, so it *looks* like
  the right pick and animates correctly — it simply has no body.
  Snake's body is `sna2_main0_def` (138 bones, 45 meshes, 31 textures).
- **`avf0_body0_def` is a torso piece**, not a whole character — the MGO avatar
  system assembles a body from parts.
- Long `_idl` clips are context animations, not idles: `snapnon_s_win_idl` runs
  540 frames. Selecting an idle by name or by length picks these; selecting by
  *pose* (head height against the walk cycle) picks a real standing idle.

## 7. Known tool defect this uncovered

`--all-sets` assigns each animation set to the skeleton that best fits it. In
phase 2 it accepted the **first** anchor clearing the 8-matched-bones bar
rather than the **best** one, so 209 TPP sets — `player2_resident` among them —
were bound to `Inf0_main0_def0`, a 15-bone skeleton whose generic core bones
clear that bar. Clips exported that way drive about six bones and the IK legs
never move.

The fix is to rank candidate anchors by matched-bone count (tie-break on rig
cache, then bone count) instead of taking the first over threshold, then re-rip.
Until then, **per-character rips are the trustworthy route for animation work**;
`test-rip-loco-loops.bat` rips the gait loops correctly against the real player
models.

## Reproducing

```
test-measure-locomotion.bat     measure every player clip, then build the table
test-rip-loco-loops.bat         rip the gait loops with a correctly solved rig
test-ext-histogram.bat          count file types per install (the .mog question)
```

Outputs land in `rips\locomotion-params\` (`locomotion-params.tsv` per source,
`cruise-table.tsv` overall) and `rips\loco-loops\`.
