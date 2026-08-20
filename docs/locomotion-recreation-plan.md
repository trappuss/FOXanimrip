# MGSV locomotion recreation — build plan

Companion to `MGSV-locomotion-1to1-feasibility.md`. That document established
*what is recoverable* from the game files (measured motion, not the engine's
compiled motion-graph). This document is the concrete build plan for the two
prep tasks that turn the measured true-data into a moving character.

Everything here is data we already have on disk — no reference footage, no
guessing. The `.mog` motion-graph does not ship in any of the three games
(confirmed exhaustively with `--ext-histogram`: 0 of ~963k files), so the blend
logic is reconstructed from the measured clips rather than parsed.

## Inputs already produced (all true-data)

- **`locomotion-params.tsv`** — one row per clip, written by the `--measure`
  pass. Columns include distance, speed, net-yaw, and turn-rate, computed from a
  travelling root-motion bake (`RootBake.FromGani` → `RootBake.Travel`). Four
  files exist, one per measured source.
- **The locomotion clips themselves** — ripped FBX/animation for player and
  character movement sets across TPP / GZ / Survive.
- **The imported rigs** — already load in Blender via the add-on, so the
  blendspace can be authored against a real skeleton.

## Task 1 — locomotion blendspace scaffold

Goal: a character that moves the way MGSV's does, driven by the measured cruise
speeds, with root motion supplying translation.

Planned approach (Blender-side first, since the rigs already import there):

1. Import a base rig + its locomotion clip set through the add-on.
2. Bake each clip's root motion so in-place pose and world translation are
   separable (the measure pass already proves the travelling bake is sound).
3. Build a 1D blendspace per stance keyed on forward speed (walk → run →
   sprint), using the measured cruise speeds from the analyzer table (Task 2) as
   the axis anchor points, not eyeballed values.
4. Add a 2D layer for strafe/turn using net-yaw and turn-rate.
5. Drive translation from the blended root motion so distance-per-second matches
   the measured speed at each blendspace sample.

Deferred pending the cruise-speed table (Task 2), which supplies the numeric
axis anchors this scaffold needs.

## Task 2 — `--analyze-locomotion` cruise-speed table

Goal: the numeric backbone for Task 1, and a cross-game consistency check.

Planned approach:

1. Read the four `locomotion-params.tsv` files.
2. Group clips by gait/stance (parsed from clip stem / set naming).
3. Emit a table: cruise speed (units/s) for walk/run/sprint per stance, plus
   turn radii derived from turn-rate ÷ speed.
4. Flag clips whose measured speed is an outlier within its group, and report
   whether the same gait lands on consistent speeds across TPP / GZ / Survive
   (they share the Fox locomotion model, so they should agree — disagreement
   means a mis-grouped clip or a game-specific scale).

Can be a CLI subcommand (`--analyze-locomotion <tsv...>`) or a standalone Python
script; CLI is preferred so it lives with the other measure tooling.

## Order of work

Task 2 before Task 1 — the table's anchor speeds are a hard input to the
blendspace. Both are deferred; this document is the spec to resume from.
