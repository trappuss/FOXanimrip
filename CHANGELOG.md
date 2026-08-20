# Changelog

All notable changes to this project are documented here.
This project follows [Semantic Versioning](https://semver.org/).

## [1.24.0] - 2026-08-20

Handbook update: the wiki now builds on the other public Fox Engine work
instead of stopping where our own measurements stopped.

### Added

- **Prior Art and Tools** wiki page — what each existing project knows and
  which to reach for: FoxBrowser (the decoder this tool drives), **FoxKit-3**
  (Joey35233), **fox_engine_mtar_tools_blender** (mctrollin),
  **mgsv-lookup-strings** (kapuragu) and **Fox_Parser** (Frostyman758). Also
  disambiguates PathFileNameCode from StrCode32/64, which is a recurring source
  of wasted effort.
- **The GANI track format, documented** (Animation System §3), from FoxKit-3's
  `TrackData.cs`: the `TrackData`/`TrackMiniData` headers, the `SegmentType`
  enum (`Quat, Float, Vector2, Vector3, Vector4, QuatDiff, VectorDiff`),
  per-track `ComponentBitSize` with unaligned bit reading, the quaternion
  encoding and half-precision decode. Explains why precision is a per-track
  property of the file, and why round-tripping degrades data.
- **GANI1 vs GANI2** (Animation System §2), from mctrollin's MTAR tools: GANI1
  in Ground Zeroes and TPP facial animation, GANI2 newer with improved framing
  and track sectioning plus a **Layout Track in the negative frame range**. Also
  that clips carry motion events, motion points and shader parameters — data a
  bone-curve-only exporter silently drops.
- **Independent confirmation of 59.94 fps**: FoxKit's
  `PlaybackRate = 1/(60000/1001)` matches the rate measured across every clip.

### Changed

- **Dictionary coverage is no longer stated as a hard limit.** The "roughly a
  third of textures resolve to no name" figure describes FoxBrowser's bundled
  dictionary; mgsv-lookup-strings is a larger validated corpus and is now the
  recommended first stop. Open question 7 downgraded to *partly answered*.
- Open Questions notes that none of the surveyed projects handles a motion
  graph format either — evidence that it is genuinely unlocated, not merely
  unknown to us — and points shader/material work at Fox_Parser, which already
  reaches `.fsop`.
- New open question: the GANI track types mctrollin's tools do not support
  (animal legs, twist-bone reconstruction) map where the format's remaining
  corners are.

## [1.23.0] - 2026-08-20

Documentation release. The tooling is unchanged; what changed is that the
research is now written down properly and publishing is one command.

### Added

- **The Fox Engine Asset Handbook** (`wiki/`, 8 pages, ~1,400 lines) — an
  in-depth account of the internals of Ground Zeroes, The Phantom Pain / MGO 3
  and Metal Gear Survive, published to the repository's GitHub wiki. Archives
  and the 64-bit path hash with the full extension-code table; FMDL, bone
  naming and the shared 53-bone rig; FTEX streamed mips, the map-suffix
  vocabulary and DXT5nm; MTAR/GANI and the clip-name grammar; the MGO avatar
  part system and FOVA form variation; the verification invariants; and the
  open questions with a concrete next experiment for each. The centrepiece is a
  **Locomotion Deep Dive**: why root travel is zero, how the authored speed is
  recovered from the stance foot, the four independent validations of that
  method, the complete measured speed table, and how the clip inventory encodes
  the state machine.
- **`scripts/push-github.bat`** — builds the CLI, GUI and add-on, commits and
  pushes `main`, creates or updates the GitHub release with all three zips, and
  syncs `wiki\` into the GitHub wiki. `--dry-run`, `--no-build` and
  `--wiki-only` for the awkward cases; it reads the version from
  `Directory.Build.props` so there is nothing to keep in step by hand.
- README rewritten around the handbook, with the measured speed table on the
  front page and the `--all-sets` caveat stated where it will be seen.

### Corrected

- **The claim that the motion graph does not ship was overstated**, and is now
  stated precisely. The asset tree is real — 989 dictionary paths under
  `/Assets/tpp/motion/motion_graph/`, names mirroring the `.mtar` sets exactly.
  What is established is narrower: no file carries extension code 4752
  (`.mog`), a sound test since the same mechanism finds 19,138 `.fmdl` and
  1,236 `.mtar` in TPP. The original argument rested on a top-level extension
  histogram, which also reports zero `.fmdl` — those live inside FPK containers
  — so that reasoning was invalid even though the conclusion survived a better
  test. The next experiment is specified in the wiki's Open Questions.

## [1.22.0] - 2026-08-20

The locomotion test bench is removed. What it was built to learn is written
down instead.

### Removed

- **foxbench** (`src/FoxBench`, `bench\`, test-bench-pack.bat,
  run-foxbench.bat, bench-shot.bat) and **`--export-runtime`**, which existed
  only to feed it.
- **The Locomotion Lab** panel from the Blender add-on (add-on 1.5.0). The
  Model Browser, Animation Library and character assembly are untouched.

### Added

- **docs/locomotion-findings.md** -- what the game data says about MGSV's
  movement: the motion-graph question (see the correction in 1.23.0), that gait
  cycles are authored in place so root travel measures ~0, that the authored speed is recoverable from the stance foot,
  and the measured speed table for every gait and stance, with how each claim
  was validated. Also the clip-name grammar as the motion graph's edge list,
  the naming traps (`skl0_main0_def` is the first-person arms model), and the
  `--all-sets` anchor defect still outstanding.
- **docs/engineering-notes.md** -- the technical specifics that cost time:
  FoxBrowser's export scene appending a synthetic `[Root]` at the END of the
  bone list (non-topological parents), bind poses carrying no rotation, cheap
  invariants for validating a pose without a renderer, why DXT textures are
  worth decoding by hand (a compressed-with-mips upload killed one NVIDIA
  driver outright while Mesa accepted the identical bytes), how to debug a
  process that dies without unwinding, headless GL rendering for verification,
  HiDPI stretching, and the .NET 10 / FoxBrowserRefDir build requirements.

### Kept

- `--measure` (including `gait_mps`), `--analyze-locomotion`, the locomotion
  token and grid grammar, and test-measure-locomotion.bat /
  test-rip-loco-loops.bat: these are ripper features that produced the measured
  data the findings rest on, and they reproduce it.

## [1.21.1] - 2026-08-20

### Fixed

- **foxbench died on the NVIDIA driver, in `LoadTextureFromImage`.** Handing
  raylib a compressed DXT image with a full mip chain makes it upload every
  level with `glCompressedTexImage2D`, and that call took the process down with
  no GL error and no exception -- so nothing was caught and the buffered log
  lost its tail, which sent the first two investigations at the wrong line. The
  data was never at fault: all 31 textures were verified byte-exact against
  their own headers, and the identical bytes upload happily under Mesa (checked
  by running the real pack here under Xvfb + llvmpipe).
  foxbench now decodes BC1/BC2/BC3 itself and uploads plain RGBA8, which every
  driver treats the same. The decoder is verified against the real game
  textures -- normal maps land on the flat-normal value, the face albedo on
  skin tone, the `_alp` maps on low alpha -- and `--selftest` now checks it.
- A **flushed trace** (`bench-logs\trace.log`) records each step straight to
  disk, since a native crash unwinds nothing and takes the buffered log with
  it. That is what finally located this one.

### Added

- foxbench `--safe` (untextured, no MSAA), plus `--notex`, `--nomsaa`,
  `--max-meshes N` and `--frames N` for bisecting a driver problem.

## [1.21.0] - 2026-08-20

### Added

- **foxbench drive mode (phase C): WASD at the authored speeds.** The bench
  boots into a character controller -- WASD to move (camera-relative), Shift to
  run, Space to sprint, Ctrl to crouch, Tab for the clip browser. Every number
  comes from the pack: each gait travels at the stance-foot speed measured off
  its own loop, so planted feet stay planted by construction; turn rates are
  the median measured rate of that gait's own `tn` loops; the model's forward
  axis and its ground offset are measured from the walk cycle rather than
  assumed. Gait changes cross-fade on local transforms (slerp, never world
  matrices). Authored start/stop/turn clips are phase D.
- Gait-loop selection is scored so the plain `snapnon` families win: the first
  build picked `snapstr_s_25_wk_u_lp_l` for "walk" -- the 25-degree stair set
  at 0.356 m/s instead of flat walking at 1.117. `--selftest` now asserts the
  chosen walk is a flat cycle.

## [1.20.1] - 2026-08-20

First real-data bench run. Three findings, all fixed.

### Fixed

- **`--export-runtime` baked clips one bone short of what it published.**
  `ExportScene` appends a synthetic `[Root]` bone that the mesh's skin indices
  and the manifest both count, but the model has no transform for -- so clips
  were written with 121 bones per frame while the manifest declared 122, and
  every frame the bench read drifted by one bone. Clips are now baked over the
  scene's bone list, mapped back to the model by name, with identity for the
  synthetic root, so stride and manifest agree by construction. The manifest
  also publishes `boneCount`/`bytesPerFrame`, and the bench now verifies its
  stride against the file's actual length before rendering a frame.
- **`[Root]` is appended LAST, so scene order is not topological**: bone 0
  (the waist) has parent 121. The bench chained bones in array order, reading a
  parent matrix that had not been computed yet -- which flattened the character
  onto the floor. It now evaluates parents-before-children by bone depth.
- **`--out` was still required on the `--export-runtime` path**, from an
  earlier check in the character branch that ran before the new guard.

### Changed

- **The bench bakes a real body by default.** `skl0_main0_def` -- the obvious
  name for "the TPP player" -- is the **first-person arms model**
  (`MESH_arm_0/1/2`, 1,378 verts, no textures), which is what the first bench
  build rendered. test-bench-pack.bat now bakes `avf0_body0_def` and takes a
  model name as an optional argument.
- foxbench reads clip frames on demand instead of loading clips.bin whole: a
  full transition inventory is ~500 MB and one frame is a few KB.

## [1.20.0] - 2026-08-20

The first test-bench release. The end goal is a standalone game-like .exe with
a character controller replicating MGSV's movement completely; this ships the
data path and the viewer (phases A and B of docs/locomotion-findings.md).

### Added

- **`--export-runtime <folder>`**: bake one character into a foxbench runtime
  pack -- skeleton, skinned mesh, textures (same decode as the model rip), and
  every selected clip as rig-solved per-frame bone transforms with the
  travelling root kept. Rotations are stored as quaternions taken straight off
  the solved matrices, so no Euler-convention drift can exist between exporter
  and playback. Every clip is measured on the way (root travel, net yaw, gait
  speed via the stance feet) and tagged with the gait grammar
  (family/stance/verb/phase/angle/foot), so the pack carries the motion-graph
  edge list the controller will walk. Use with --character and --mtar /
  --filter-any / --locomotion exactly like a rip.
- **`foxbench.exe`** (new, lives in `bench\`): the test bench, phase B --
  renders the pack's character with CPU skinning over the baked transforms and
  plays any clip at its authored 59.94 fps. Orbit camera, gait-loop cycling,
  frame stepping, HUD showing each clip's measured speed/turn numbers, F12
  screenshots into bench-logs\ for the unattended read-loop, and a
  `--selftest` mode that loads a pack and verdicts it with no window. Pack
  loading and pose evaluation are verified against a synthetic-pack round
  trip (caught a real span-length bug before it ever ran on game data).
- **`test-bench-pack.bat`** bakes the TPP player pack into
  `bench\packs\tpp-player` and self-tests it; **`bench\run-foxbench.bat`**
  opens the viewer on it. Everything bench-related lives under `bench\`.

## add-on [1.4.0] - 2026-08-20

### Added

- **Locomotion Lab (MGSV 1:1)**: a new sidebar panel that plays the player's
  measured gait cycles at their authored speeds. Point it at a rips folder and
  the measured params, select the player armature, Build: it imports the
  walk / jog / run / sprint / crouch loops (loose files or packed rips, via the
  exporter's index.tsv), re-measures the stance feet *inside Blender* -- same
  estimator as `--measure`, now through the whole import pipeline -- and drives
  the armature's travel so the planted foot stays planted. A gait switcher
  solos the loop and sets the speed; a Check Foot Slide operator reports the
  residual. Headless-verified against the real Survive rip: in-scene speeds
  match the game data to 0.35% worst-case across four gaits, travel is exactly
  speed x time, and planted-foot slide is 8-14% of travel -- which is the
  cycle's own authored variance around cruise, the same micro-slide the
  engine's constant parametric drive produces in game (tests/test_locolab.py).

### Discovered (tool bug, fix pending)

- **The all-anims tree exported humanoid sets through a stub skeleton.**
  `--all-sets` phase 2 assigned 209 TPP sets -- player2_resident among them --
  to `Inf0_main0_def0`, a 15-bone skeleton whose generic core bones clear the
  8-matched-bones bar, so those clips drive ~6 bones and the IK legs never
  move. The anchor choice must rank candidates by matched-bone count, not
  first-over-threshold. Until the fix + re-rip, use per-character rips for
  animation work; test-rip-loco-loops.bat (new) rips the gait loops correctly
  against the real player models.

## [1.19.0] - 2026-08-20

The locomotion-recreation release: the numbers a 1:1 movement rebuild needs,
measured, grouped and cross-checked. See docs/locomotion-findings.md.

### Added

- **`gait_mps`: the authored speed of in-place cycles.** The first measure pass
  proved MGSV's gait cycles -- walk, run and dash loops, even the starts -- are
  authored in place (root travel ~0; the engine applies travel parametrically
  from its compiled motion graph), so root travel reports nothing for exactly
  the clips that matter most. The speed is still in the curves: while a foot is
  planted it is stationary in the world, so in the character's frame it sweeps
  under the root at precisely the authored travel speed. `--measure` now bakes
  world positions, finds the `*LFOOT`/`*RFOOT` bones by their dictionary names,
  and reports the median stance-phase foot speed as `gait_mps` (plus
  `gaitSamples`, the evidence count). For clips whose root does travel the same
  measurement returns the travel speed, so it doubles as a cross-check. Verified
  against synthetic walks (tests/gait): a fabricated 1.2 m/s in-place walk
  measures 1.2 m/s exactly, travelling and in-place agree, idles report 0.
- **`--analyze-locomotion <path>`** (repeatable; files or folders): reads
  locomotion-params.tsv tables, groups rows by the clip-name grammar
  (family / stance / verb wk-rn-jg-dh / phase st-lp-tn-ed / angle) and writes
  `cruise-table.tsv` -- median measured speed and turn rate per group -- with
  the forward-loop cruise speeds (the blendspace anchor numbers) printed to the
  console. Runs on the tables alone; no game or FoxBrowser needed. Old tables
  without `gait_mps` are accepted and flagged.

### Fixed

- **`--locomotion` silently dropped the plain run and dash loops.** The token
  list had `run`/`dsh` but the player sets name their cycles `_rn_`/`_dh_` with
  `_lp_` for the loop -- so `snapnon_s_rn_lp_...`, the most important clips in
  the game, matched nothing. Added `_rn`, `_dh`, `_jg`, `_lp` (and `_wk` was
  already there). Re-run test-measure-locomotion to pick them up.
- **Locomotion grids now recognise dash**: the grid grammar accepted wk/rn/jg
  but not `dh`, which the player sets verifiably use (`snapnon_s_dh_st_l0_l`).

### Notes

- test-measure-locomotion.bat now runs the analyzer as its final step, so one
  double-click produces both the per-clip tables and cruise-table.tsv.

## add-on [1.3.15] - 2026-08-20

### Changed

- **Model Browser list: restored the two-column layout** — icon + model name
  flush left, description right-justified. The row had been built with
  `align=True`, which joins the two sub-columns and packs them together on the
  left, bunching the description up behind the name; a plain distributing row
  puts each column back at its intended edge. Tool binaries are unchanged
  (1.18.6); the add-on versions independently.

## [1.18.6] - 2026-08-20

### Added

- **`--ext-histogram`: what file types an install actually holds**, counted by
  extension code (the top bits of each entry's path hash), no dictionary needed.
  Built to settle the motion-graph hunt. **This entry's original conclusion was
  wrong and is corrected in 1.23.0**: at the top level the histogram also reports
  zero `.fmdl` and zero `.mtar`, which plainly exist inside FPK containers, so it
  cannot establish that a type is absent. The narrower sound result is that no
  file carries extension code 4752. The `.fsm` present are cutscene data, not
  locomotion.
- **`--dump-mog`** (1.18.4) stays as a general tool: it finds motion-graph files
  by extension code where a Fox game does ship them.

## [1.18.5] - 2026-08-20

### Fixed

- **`--measure`, `--locomotion`/`--grid` now actually apply in a batch.** The
  per-character options the batch runner builds didn't copy the clip `Filters`,
  `GridOnly` or the new `Measure` flag, so `--measure` fell through to a normal
  FBX rip and `--locomotion`/`--grid` were ignored (every clip exported). All
  three now carry through, so measuring writes `locomotion-params.tsv` and the
  locomotion filters select what they say.

## [1.18.4] - 2026-08-20

### Added

- **`--dump-mog`: extract the motion-graph (.mog) files** — the blend/state logic
  behind player locomotion, and the flagship piece for a 1:1 movement rebuild.
  `.mog` files are hash-named and in no dictionary, so the catalogue now finds
  them by extension code (the top bits of each archive entry's path hash) rather
  than by name. The command saves the raw files and writes `mogs.tsv` with a
  header summary per file. The graph body format is only partly documented, so
  this deliberately extracts and summarises rather than over-interpreting — the
  real files are what the full parser gets built against. Catalogue schema bumped
  to 3 (first run rescans).

## [1.18.3] - 2026-08-20

### Added

- **`--measure`: authored locomotion parameters, no FBX.** For each clip it bakes
  the travelling root and writes `locomotion-params.tsv` — per clip the root's
  travel distance (m), speed (m/s), net turn (deg) and turn rate (deg/s), plus
  frames/fps. These are the real, measured numbers a 1:1 movement rebuild needs
  (walk/run/crouch/sprint speeds, turn rates), read straight off the animation
  data rather than guessed. Pair it with `--character <base> --mtar <set>` or
  `--all`, and `--locomotion`/`--grid` to focus on the movement clips.

## [1.18.2] - 2026-08-19

### Fixed

- **catalog.html now categorises heads correctly.** `hd[fm]*` is worn headgear
  (helmets, masks, hats), not the head itself — it was filed under "Head." The
  real head/face models are the avatar presets `av[mf]N_typeN_def` (eyes, mouth,
  skin, bandanna; type0–7 are different faces), and `av[mf]N_bodyN` is the base
  body. The catalog now classifies from the model name — Head, Headgear, Base
  body, Arms, Legs, Body, Chest rig, Hair — instead of only the rip folder, so
  searching a category returns what it says. (The Blender add-on's Model Browser
  gets the same fix in add-on 1.3.9.)

## [1.18.1] - 2026-08-18

### Fixed

- **`--all-sets` now finds rig-driven animation sets — the player and character
  motion.** 1.18.0 assigned each set to a model by intersecting the set's bone
  table with the model's, which is empty for a rig-driven archive: those name rig
  units, not bones. So the biggest sets in the game — `player2_resident` (1,285
  clips), `mgoplayer_resident` (2,407) and every other player/character motion
  set — matched nothing and were reported UNCOVERED, and a full three-game sweep
  came out at a few hundred clips instead of tens of thousands. The sweep now runs
  in two phases: the fast bone-overlap pass places sets that carry a skeleton
  list, then a second pass rig-resolves the skeletons the most models share (the
  player/human rig ranks first) and asks each rigged model whether it can play the
  deferred sets — the same decode-and-resolve the normal `--all` path uses. The
  shared rig cache means the cold rig searches are paid once across all six game/
  motion runs. `all-sets-report.tsv` marks rig-placed sets `assigned (rig)`.

## [1.18.0] - 2026-08-18

### Added

- **`--all-sets`: rip every animation archive in the game in one pass.** No
  character needed. Every model's skeleton is read once, then each animation
  archive is bound to the single skeleton that best fits it (ties go to the
  leaner skeleton, the one built for that animation rather than a superset).
  `--all-models` widens the candidate skeletons past character models to
  everything the game ships — vehicles, gear, creatures — so nothing is left out
  for want of a hand-picked base model. A set that shares no bones with any model
  (typically one addressing rig units rather than bones) is written to the
  coverage report as UNCOVERED rather than dropped silently.
- **`--tree`: lay clips out mirroring their origin path.** Instead of a flat
  `<out>/<mtar>/<clip>.fbx`, clips land under the folder chain the set lives in
  inside the archives: `<out>/Assets/.../<mtar>/<clip>.fbx`. Implied by
  `--all-sets`. `index.tsv` gains a `sourcePath` column in every mode, and
  `--all-sets` also writes `all-sets-report.tsv` — one row per set with the model
  used, bones matched, coverage, and placement status.
- **`test-rip-all-anims.bat`**: rips all three games, both motions (in-place and
  root-motion), one FBX per clip, into an origin-mirrored tree with a rolled-up
  coverage summary. Logs to `test-logs\8*-allanims-*.log`.

### Fixed

- **Root motion was silently dropped from every command-line rip.** The per-model
  options the batch runner built copied most settings but not `RootMotion`, so a
  `--root-motion` run baked in place like any other — the "travelling" folders
  the export bats produce were identical to the in-place ones. The batch now
  carries root motion (and the new tree/append options) through.

## [1.17.4] - 2026-08-18

### Fixed

- **Hash-named textures (avatar heads, hair, some Survive parts) now export at
  full resolution too.** 1.17.3 assembled streamed mips only when a texture's
  path resolved, because the assembler was fed the file through its resolved
  directory. Hex/hash-named textures — the ones whose paths don't resolve, which
  is exactly the head and hair textures on MGO avatars — fell straight through to
  the 512 px inline image. The full-res path now pulls a texture's source files
  by hash (`FtexSourceFiles`), including every streamed `.ftexs` companion, and
  assembles them from memory with no dependence on the path resolving. The old
  path-based assembly stays as a fallback. Delete the output folder and re-run to
  pick up the higher-resolution heads (the `(up to Npx)` log line shows the
  largest edge written).

## [1.17.3] - 2026-08-18

### Fixed

- **The full-resolution texture fix now actually has the data to work with.**
  1.17.2 assembled streamed mips through FoxBrowser's `FtexAssembleCore`, but the
  `texture*` archives that hold those streamed mips were being excluded from the
  archive set (they are skipped to keep the index fast, since they hold no models
  or animation). So there was nothing to assemble and textures stayed at 512 or
  less. The model, batch and variation texture paths now add the texture archives
  back on top of the useful set before decoding, and each model logs the largest
  texture edge it wrote (`... (up to 2048px)`) so a run shows whether the high
  mips came through. Animation indexing still skips the texture archives, so it is
  no slower.

  To pick up the higher-resolution textures on models already ripped, delete the
  output folder and re-run — the models have their sidecar now, so `--skip-existing`
  would otherwise leave them as they are.

## [1.17.2] - 2026-08-18

### Fixed

- **Textures now export at full resolution.** A Fox Engine `.ftex` holds only the
  lower mips inline; the high-resolution mips are streamed in numbered `.ftexs`
  companion files. The exporter was decoding the inline part only, so Survive
  characters came out at 512 px or smaller. It now assembles the streamed mips
  through FoxBrowser's own `FtexAssembleCore`, reading the companions out of the
  archives, and falls back to the inline image only when a texture has no
  streamed part or its path is unresolved (hash-named). Re-rip to pick up the
  higher-resolution textures.

## [1.17.1] - 2026-08-18

### Fixed

- **Model export now writes a texture-role sidecar (`<name>_maps.tsv`)** so the
  Blender add-on can wire the spec/roughness map and hash-named normals. Fox
  Engine only references the base and normal maps in the FBX, and a texture whose
  source path was unresolved comes out hash-named — so a material built from
  shared, unresolved textures (common on Survive gear) lost the roles of its
  normal and spec maps at import. The sidecar records base → normal → spec per
  material, keyed by the base file both sides can see.
- **`--skip-existing` re-exports a model that predates the sidecar** — one with an
  FBX and a textures folder but no `_maps.tsv` — so re-running a rip picks up the
  sidecar without a full from-scratch redo. Models with no textures are still
  skipped.

## [1.17.0] - 2026-08-18

### Added

- **`--inventory` now writes `catalog.html`** — a single searchable page of every
  model and animation archive in the game, grouped by category with a
  plain-language description for each, plus a built-in "How to use" tab covering
  the assemble-a-character workflow, locomotion export, and customisation-texture
  ripping. It is generated from the same inventory data, so the navigable
  catalogue is a normal output of the tool rather than something built by hand.
  Character codes are translated where known (Snake, Quiet, Miller, the Survive
  cast, the MGO classes); gear and parts are described by function.

## Blender add-on 1.3.7 - 2026-08-18

### Fixed

- **Add to Active Character no longer breaks a part's materials.** The assembler
  imported parts with a plain FBX import, so an added part got flat materials with
  no srm and no DXT5nm normal handling — while the *same* model imported on its
  own looked right. Added parts now get the full material rebuild (normals, srm
  split, role sidecar), so an assembled character matches a standalone import.

### Added

- **Category and Gender filters in the Model Browser**, beside the game filter.
  Category is built from what the scan finds (Head, Hair, Arms, Legs, Upper
  armor, Chest, Base body, Headgear, Eyewear, …) so heads, hair and eyewear are
  one dropdown away instead of buried in hundreds of models. Gender is Male /
  Female / Unisex, read from the model name.
- Hair is its own category now, and base bodies (`bsm`/`bsf`/`skl`) group under
  "Base body". The browser's folder field is worded for any folder — it never
  needed to be called "rips"; the scan walks whatever folder you point it at.

### Fixed

- **Normal and spec maps now load correctly on Survive gear.** A material whose
  textures came out hash-named (shared, unresolved textures) had its normal map
  loaded as **sRGB** instead of Non-Color — wrecking the normals — because colour
  space was decided from the file name's `_nrm`/`_srm` suffix, which a hash name
  does not have. Colour space now follows the map's role, so a normal identified
  through the FBX link loads Non-Color regardless of its name.
- **Spec/roughness (srm) maps now wire up** even when hash-named. The add-on reads
  the tool's `<name>_maps.tsv` sidecar to find each material's normal and spec by
  its base texture, so the roughness and specular that were silently missing on
  hash-named materials come through. Re-rip Survive models (1.17.1) to generate
  the sidecar; the colour-space fix needs no re-rip.

## Blender add-on 1.3.5 - 2026-08-18

### Added

- **Model Browser** — the catalogue, inside Blender. Point it at a rips folder,
  hit Scan, and every model becomes a searchable list with a plain-language
  description, so "quiet", "arm female", "hat", "respirator" surfaces the right
  file out of a folder of terse names. Filter by game, then **Import Selected**
  (full FoxBrowser treatment) or **Add to Active Character** (onto the current
  rig via the assembler). Animation packs are skipped; descriptions use the same
  translation the tool's catalogue does, so no data file is needed.
- **How to Assemble a Character** panel — the workflow steps in the sidebar, so
  finding and combining models does not need the docs open.

## Blender add-on 1.3.4 - 2026-08-18

### Added

- **Add Part(s) to Active Character** — the incremental assembly workflow. Import
  a base body with Model(s), leave its armature selected, then add parts from any
  folder, one file or many, running it again to add more. Each part's meshes move
  onto the active rig. This replaces needing every file in one folder for a single
  multi-select (the all-in-one *Assemble Character* is still there for when they
  are together). Adding a part also un-hides the base body's own meshes, so a base
  imported outside the add-on still shows up.

## Blender add-on 1.3.3 - 2026-08-18

### Added

- **Assemble Character** (sidebar and File ▸ Import). Fox Engine builds a created
  soldier — MGO's avatar, Survive's survivor — from a minimal base body plus
  interchangeable parts (head, arms, legs, chest, armour, hats, hair), every one
  rigged to the same player skeleton. This does the same: select a base and its
  parts and it moves every part's meshes onto the base's rig, so the result is
  one character on one armature, the way the game assembles it. The base is
  detected by name (`bsm0`/`bsf0` for Survive, `skl0` for MGO) or chosen in the
  dialog. Parts that carry bones the base lacks — a head's face bones — have
  those bones merged across at their exact rest position (verified to 0.00 mm).
  Ground Zeroes characters are single complete models, so selecting one just
  imports it.

  Works for all three games; audited against real rips: every part's vertex
  groups map to a master-armature bone (nothing left un-deformable), no
  duplicate bones, and a rendered survivor shows head, vest, arms and legs
  landing in their correct places.

- `scripts/assemble-character.py` — the same assembler from the command line, for
  building characters in a loop: `blender --background --python
  assemble-character.py -- --base bsm0…fbx --part hdm0…fbx --out char.blend`.

## Blender add-on 1.3.2 - 2026-08-18

### Fixed

- **Imported models could show only their skeleton.** Fox Engine flags a model's
  default-hidden mesh groups as invisible in the file — the created-soldier base
  body (`bsm0_main0_def` / `bsf0_main0_def`) is the clearest case: in game it is
  hidden and swapped out by equipped gear, so every one of its mesh groups ships
  with visibility off. Blender's FBX importer honoured that and imported the mesh
  hidden, so the viewport showed only bones while the geometry sat there unseen
  (FoxBrowser ignores the flag and draws everything). The add-on now un-hides
  every imported mesh, matching FoxBrowser. The geometry was never missing — only
  hidden.

## [1.16.3] - 2026-08-18

### Fixed

- **`--rip-variations` on Survive reported 662 files "unreadable"** where nothing
  was actually lost. Survive's assembly variations (`arf0_main0_v00`) reference a
  part model through a packed copy whose inner hash is not separately indexed, so
  the raw read fails — but the part itself (`arf0_main0_def`) is a top-level model
  exported by name. Those are now recognised against the catalogue and reported
  as **part-model references**, with the real model named in `ripped-files.tsv`,
  instead of counted as failures. The customisation textures — the actual point
  of the rip — always decoded: 176 unique skin/detail textures across 1,306
  references, zero of them affected.

## [1.16.2] - 2026-08-18

### Added

- `scripts/test-survive-locomotion.bat` — the base player's locomotion for both
  genders, in place and travelling. Built from the audit: the base skeletons are
  `bsm0_main0_def` (male) and `bsf0_main0_def` (female) under
  `Assets/ssd/chara/base`, and the player motion archive is `SsdPlayer_layers`
  (3,220 clips), Survive's equivalent of TPP's `player2_resident`.
- `scripts/test-rip-survive-chars.bat` — all 419 `Assets/ssd/chara` part and
  character models (arm, leg, head, up_armor, chest_rig, body, hats, plus
  bosses, zombies, kaiju, walker gear, NPCs), resumable and rig-skipping like
  the MGO gear script, followed by the Survive customisation textures from
  `ssd/fova/chara` — 8,287 variation rows, a far deeper character creator than
  MGO's.

### Fixed

- **A Survive install read as "custom".** No executable in its Steam folder
  matched, so detection fell through. `Identify` now falls back to the folder
  name before giving up, so `METAL GEAR SURVIVE` is recognised as Survive.
  Explicit `--game survive` always worked; this only fixes auto-detection and
  the game picker label.

## [1.16.1] - 2026-08-18

### Changed

- **Metal Gear Survive profile hardened for a real install.** Added the
  all-caps `METAL GEAR SURVIVE` Steam folder name and the `MGSurvive.exe`
  executable, and pointed the archive search at `master\` and `pack\` alongside
  the root. The decoder was never the question — the tool drives FoxBrowser's
  own `MtarAnimSet`, so anything FoxBrowser can browse in Survive, this can
  export — but the profile now finds the install without a manual `--root`.
  Survive's character models live under `Assets/ssd/chara`, which the existing
  `/chara/` filter already treats as characters, so no code path assumed TPP
  naming.
- `scripts/test-survive-audit.bat` — a read-only pass that proves the tool
  reads a Survive install and enumerates its models and animation archives, so
  the rip scripts can name the real base skeleton and locomotion archive rather
  than guess them.

## [1.16.0] - 2026-08-17

### Added

- **`--no-rig`** — skip the rig search entirely. A model-only export never plays
  a clip, yet every model the cache had not seen paid a full archive walk to
  learn it has no rig — the slowest step of the gear rip by far, for an answer
  gear was always going to give.
- **`--skip-existing`** — with `--export-model`, characters whose FBX is already
  in the output folder are skipped, so an interrupted batch resumes where it
  stopped instead of repeating finished work.
- `scripts/test-convert-png.bat` — a PNG beside every DDS, leaving the DDS
  untouched as the authentic game data. Uses Microsoft's texconv (downloaded
  once from the official DirectXTex releases if absent), which decodes every
  format Fox Engine uses rather than guessing. Two-channel BC5 normal maps get
  their blue channel reconstructed so they view correctly; everything else
  converts plainly in its own colour space. Already-converted files are
  skipped on re-runs.

### Changed

- Batch model exports open the game archives **once per batch** instead of once
  per model — building that index cost more than ripping one model's textures,
  and a 20-model batch was paying it 20 times.
- `scripts/test-rip-mgo-gear.bat` regenerated to use all of the above; it is now
  safe to stop and re-run at any point.

## [1.15.1] - 2026-08-17

### Fixed

- **`--rip-variations` raw files overwrote each other.** A variation's model and
  its physics file share a stem, and raw copies were named by stem alone — so
  `tes1_main0_def.fmdl` and its `.sim` both became `tes1_main0_def.bin` and one
  survived. The first real run lost about 40 files this way; the manifest's
  distinct codes against fewer files on disk is what gave it away. Raw copies
  now keep the real extension when the name resolves, and any remaining
  collision gets a short code suffix rather than a silent overwrite.

  The first run's counts were otherwise right: 626 avatar variations, 659 unique
  files, 420 textures decoded to DDS, 0 unreadable — the multi-flavour code
  handling held up against the real archives.

### Added

- `scripts/test-rip-mgo-gear.bat` — the other half of the avatar wardrobe: all
  177 MGO equipment models (hats, chest gear, heads, glasses, suits and outfits
  for every class and both genders, plus the three DLC gear sets), generated
  from the inventory tables rather than picked by hand, followed by the DLC
  gear's customisation textures from `tpp/fova/chara/dl*` — a folder the
  avatar run's `mgo/fova/chara` filter did not cover.

## [1.15.0] - 2026-08-17

### Added

- **`--rip-variations <filter>`** — extract the files form variations point at,
  which is the missing half of the inventory. `variations.tsv` could say "this
  skin tone swaps material slot X to texture #0" but nothing could pull texture
  #0 out: model export only rips what a model's own materials reference, and a
  variation's files are by definition not among those.

  The stored references are 64-bit codes of more than one flavour (plain path
  code, path code with the extension folded into the top bits, Ground Zeroes'
  own name hash), so every entry is tried each way FoxBrowser can read a file:
  as an ftex decoded to DDS first, then as a raw copy. `ripped-files.tsv` maps
  every variation to its files — including the failures, because "this code
  could not be read" is an answer too.

  The reason this exists: the MGO avatar customisation set
  (`Assets/mgo/fova/chara`) builds its five skin tones per garment per gender
  entirely from texture swaps — real files, zero shader-value rows — and those
  files were unreachable.

- `scripts/test-rip-avatars.bat` — every MGO avatar asset unattended: all 45
  player-relevant models (both avatar genders with all heads and hair, the skl0
  base skeletons, the DLC characters, the customisation stage) plus the
  customisation textures via `--rip-variations`, logged for reading.

## [1.14.2] - 2026-08-17

### Fixed

- **`--why-mtar` demanded `--out`** and quit — after printing its rig line, so
  the run looked half-successful. It and `--list-mtars` are diagnostics that
  write nothing; the export-folder requirement now applies only to commands that
  export. Caught by reading the logs of the first unattended `test-gather.bat`
  run rather than by anyone at a keyboard.
- **`--for-mtar` on a rig-driven archive could grind for hours in silence.**
  When name-matching finds nothing it resolves candidates through their rigs,
  and "checking the first 60" of 600 was still hours on a cold cache — each
  unresolved rig means searching the archives. Over the cap it now checks only
  models whose rigs are already remembered, names the count it skipped, and
  tells you to narrow with `--model-filter` — an instant honest answer instead
  of a bounded-looking one that never arrives.

### Changed

- `scripts/test-gather.bat` announces each step on the console as it goes. Its
  first version redirected everything to the logs, so a run that was working
  through fourteen slow steps was indistinguishable from a hung one.

## [1.14.1] - 2026-08-17

### Fixed

- **`--inventory` reported `0 form-variation file(s) to read`** on a game full of
  them. The schema guard added in 1.14.0 — whose entire job was to rescan an
  index written before `.fv2` files were collected — did not work.

  `public int Schema { get; set; } = CurrentSchema;` reads like a sensible
  default. It is a trap: `System.Text.Json` leaves a property that is absent from
  the JSON at whatever the declaration initialises it to. Every old cache has no
  `Schema` property, so every old cache deserialised claiming to be current,
  passed the guard, and was reused — which is precisely the staleness the field
  was added to catch. The field now defaults to 0 and only a scan stamps it.

  Anyone who ran 1.14.0 and saw no variations should just re-run; the index
  rebuilds once, and says so.

### Added

- **`-V` / `--version`**, and the build number as the first line of every console
  run. The window has shown its version since 1.11.0; the console tool showed
  nothing, so pasted output could not be tied to a build.
- `tests/index` — the cache-staleness rules, including the exact bug above. It
  fails on the old declaration and passes on the new one, which is the only
  reason to trust it.

### Changed

- One version number for the whole build, in `src/Directory.Build.props`. It was
  previously set per project, which is how 1.12.1 shipped a console tool still
  reporting 1.11.0.

## [1.14.0] - 2026-08-17

### Added

- **`--inventory <dir>`** — everything the game has, written down. Three tables
  plus a script:

  - `models.tsv` — every model with bone, mesh, material and mesh-group counts,
    the archive it came from and its patch layer.
  - `textures.tsv` — every texture each material references, by role.
  - `variations.tsv` — what each **form variation** (`.fv2`) does: mesh groups
    hidden and shown, texture swaps, material parameters, attached sub-models,
    and the files those point at.
  - `rip-all-models.bat` — exports every model listed, in batches, since one
    process per character spends longer starting up than working.

  This is the answer to "list every player model and its customisation options".
  Fox Engine does not store a character as a finished thing: a model has named
  mesh groups, and a form variation hides some, shows others, swaps individual
  textures, sets shader values and bolts extra models onto bones. One `.fmdl`
  plus a folder of `.fv2` files is how a few files become hundreds of
  appearances — so an option is an instruction, not a file, and this reads the
  instructions out.

  It also settles the skin-tone question rather than guessing at it: a
  `textureSwap` row means the option genuinely points at a different texture, a
  `materialParameter` row means it only changes a shader value. Both mechanisms
  exist and the file says which one an option uses.

- `.fv2` files are now indexed. **Animation archives (`.mtar`) have nothing to do
  with models** — they hold only `.gani` clips — so nothing about customisation
  was reachable before this.

### Changed

- The archive index carries a schema number, and an index written before a file
  type was collected is rescanned rather than reused. The archive fingerprint
  cannot notice this on its own: the game has not changed, only what is being
  looked for in it — so without the schema, adding `.fv2` would have loaded an
  old index, seen it marked complete, and reported no variations at all.

  **The Phantom Pain and Ground Zeroes both re-index once** on the next run.

## [1.13.0] - 2026-08-17

### Added

- **Keep root motion** in the window's options, beside *Also export the character
  model*. Root motion was reachable from the command line and the preview but not
  from the interface most people use.
- One export script per game, each covering both root-motion variants:
  - `scripts/export-mgsv-player.bat` — Phantom Pain, `skl0_main0_def` and
    `skl0_main0_def_f`, in place and travelling. Reports both models' bone counts
    first, since if they share a skeleton their clips come out identical and one
    set will do.
  - `scripts/export-gz-player.bat` — Ground Zeroes, `sna2_main0_def`, in place and
    travelling, plus the facial set. Facial is exported once rather than in both,
    since face bones carry no root travel and the copies would be identical.

  These replace `export-locomotion-rootmotion.bat`, which covered a subset of the
  same ground with root motion only.

## [1.12.1] - 2026-08-17

### Fixed

- `--grid` on an archive with no locomotion grids selected nothing and wrote an
  empty folder, which looks exactly like an export that ran. It now says so and
  points at `--locomotion` or dropping the flag.

### Added

- `scripts/export-locomotion-rootmotion.bat`: the whole player export, both
  characters and both games, with root motion on. Runs the compatibility checks
  first and stops for you to read them, because an export against a model that
  matches nothing produces empty folders rather than an error.

## [1.12.0] - 2026-08-17

### Fixed

- **`--for-mtar` gave a wrong answer for the archives it was written for.** It
  ranked models by intersecting an archive's hash table with a skeleton's bone
  hashes — but a v2 archive's table lists **rig units**, not bones, so for
  `player2_resident` and `mgoplayer_resident` every model scored zero and the
  ranking was meaningless. Silently, since an empty result looks like "nothing
  fits" rather than "wrong question asked". When the cheap comparison finds
  nothing, it now resolves candidates through their own rigs, the way the game
  does. That is slower, so it is bounded — and the bound is printed rather than
  quietly truncating.

### Added

- **`--root-motion`**, and a **Root motion** toggle in the preview. Clips are
  baked on the spot by default, which is right for an Action library but means a
  character animated from them never leaves the origin. FoxBrowser's own bake
  hardcodes translation off, so this is a parallel bake with that one argument
  flipped; the Euler extraction matches it exactly, so the only difference is the
  travel. The preview toggle re-solves live.
- **Locomotion grids, detected rather than guessed.** `--list-grids <set>` finds
  complete movement sets by structure — verb, phase, angle, lead foot — and
  `--grid` exports only clips that belong to one. On `player2_resident` this
  finds four complete 8-direction grids (walk and run, standing and crouched, 64
  clips each) and claims 402 of 1,253 clips rather than sweeping the archive.
  `--locomotion` remains as the fuzzy name-fragment filter; `--grid` is the exact
  one, and reports any grid that comes back incomplete instead of pretending.
- `tests/grid`: grid detection against a synthesised complete family and, if you
  pass a clip list, a real archive. Asserts structure — eight directions, stops
  matching starts, every turn carrying a lead foot — and that non-locomotion
  clips are *not* swept in, which is the failure a fragment filter cannot detect.

## [1.11.0] - 2026-08-17

### Fixed

- **The newest copy of a file now wins.** The Phantom Pain ships the same file in
  more than one archive and loads the later one:
  `player2_resident.mtar` is in `master\chunk0.dat` with 1,253 clips and again in
  `master\0\00.dat` with **1,285**. Whichever copy the scan happened to reach
  first was being used, so an export could silently be of a version of the game
  nobody is playing. Copies are now ranked by patch layer, and when they disagree
  the log says which was chosen and what it beat.

### Changed

- **The chosen rig is remembered.** Finding one walks every archive and parses
  every `.frig` inside — thirteen archives and tens of gigabytes on Phantom Pain,
  repeated on every run before anything you asked for. `--why-mtar`, which prints
  six lines, appeared to hang for minutes with nothing on screen. Rigs are now
  cached beside the archive index, keyed the same way, and the search says what
  it is doing while it runs.
- `--list-sets` calls its third column **tracks**, not bones. A v2 archive's table
  lists the rig units its clips address, not skeleton bones — labelling 18 rig
  units as "18 bones" invites exactly the wrong conclusion about a 120-bone
  character.

## [1.10.0] - 2026-08-17

### Added

- **Clips are listed with their packing index and resolved path.** The index is
  the clip's position in the archive, which is the key the community's
  hand-written GANI description lists are numbered by — so those lists and this
  tool's output line up row for row, and the abbreviated names stop needing to be
  guessed at. `--list-clips` prints `#`, name and path; the browser gains the same
  two columns.

  The path is whatever the game's own hash dictionary resolves for that clip. It
  is not an interpretation of the name — it is the name, spelled out, from the
  game's data.

## [1.9.1] - 2026-08-17

### Fixed

- **The browser's search boxes ate most of the alphabet.** The window takes
  single-letter shortcuts — s for skeleton, m for mesh, r to re-frame, space to
  pause — and `KeyPreview` routes every keystroke through them first, so typing
  any of those into a search box triggered the shortcut instead of the letter.
  Typing now wins wherever text can be typed.
- **Dark mode broke on the new lists.** Owner-drawing the column header while
  leaving rows to `DrawDefault` is unreliable in Details view — rows come back
  unpainted, checkboxes go missing. The header is now handed to the shell's own
  dark theme, the same one Explorer uses, and nothing is drawn by hand. Context
  menus get a proper dark renderer rather than system grey.

### Changed

- The browser opens on **the sets ticked in the main window**, with a tick box to
  widen it to everything in the game. A ticked set is a decision already made;
  the escape hatch stays one click away because the automatic fit judgement is
  the thing that cannot be trusted.

## [1.9.0] - 2026-08-17

### Added

- **The animation set list takes a selection.** Shift or Ctrl picks several rows,
  and ticking any one of them ticks the whole selection — twenty sets is one
  click rather than twenty. Ctrl+A selects everything shown.
- **Right-click menu** on that list: tick or untick the selection, tick or untick
  everything currently shown, invert, select all. The counts are in the menu
  labels, and "shown" means what the search box and *Show every set* are letting
  through rather than silently meaning everything.

### Changed

- That list is now a table — set, clips, bones, verdict in their own columns —
  instead of one run-on line per row. WinForms' `CheckedListBox` cannot do any of
  the above: it supports exactly one selected row and throws if asked for more,
  so the control had to change for the feature to exist at all.
- Dark mode reaches `ListView` and context menus, which it previously did not.
  The new lists would otherwise have come out bright white with a system-drawn
  header, which reads as a rendering fault rather than a style.

## [1.8.0] - 2026-08-17

### Fixed

- **Player animations played nothing, and the fault was 1.4.0's rig fix.**
  Choosing a rig by how much of the *model* it covered looked reasonable and was
  wrong in both directions. A `.frig` describes only the bones it **drives** —
  help bones and IK chains — never the whole skeleton. The real rig for the
  120-bone player model `skl0_main0_def_f` names 53 bones: 44% coverage, under
  any floor worth having. So the correct rig was rejected, the character was left
  with no rig at all, and because the player's archives are rig-driven — their
  clip tracks addressed by rig channel rather than by bone name — nothing moved.
  `player2_resident` reported 0 matching bones on a character FoxBrowser animates
  without complaint.

  The test that works is **precision**: what share of the *rig's* bones this
  skeleton has. The player's own rig scores 100% while covering 44% of the model.
  The foreign 144-bone rig that caused the original stretching scores 65% while
  covering 100%. One number separates both failures; coverage separates neither.
  Among believable rigs, the one driving the most of the skeleton wins.

  A plausible rig is also never discarded now. 1.4.0 preferred no rig to a
  doubtful one, on the grounds that a wrong rig tears a character apart while a
  missing one only looks stiff. That was true of the case in front of me and
  false in general: for rig-driven archives a missing rig means no animation at
  all. The closest rig is used and the log says plainly how well it fits.

- **The browser's Matched column read zero for rig-driven archives.** It compared
  bone hashes directly, which cannot work when an archive's tracks are indexed by
  rig channel. It now asks the same question the player does.

### Added

- `tests/rig`: the rig-choice rule against both real failures — the 144-bone rig
  that stretched a soldier and the 53-bone rig that was wrongly rejected for a
  120-bone player. Neither needs game files; the numbers were measured from real
  rips, and a rule that satisfies one and not the other fails.

## [1.7.0] - 2026-08-17

### Changed

- **The animation browser no longer hides anything, and no longer asks you to
  find sets first.** *Browse & preview animations…* opens straight onto every
  `.mtar` in the game — searchable, with clip count, bone count, matched bones
  and the archive path — and the clips inside whichever one you select. Pick a
  character from the dropdown at the top and switch between them without
  reopening.

  The compatibility filter is gone from this path entirely. Matching an archive
  to a skeleton automatically is not reliable: an `.mtar` only carries a bone
  table when its header sets `HAS_SKEL_LIST`, and where it does not, both this
  tool and FoxBrowser's own dialog will report that nothing fits a character
  whose animations play perfectly the moment you name the archive by hand. A
  filter built on that guess makes the thing you are looking for vanish with no
  explanation. The match count is now a **column**, never a gate — a hint you can
  settle in one second by pressing play.

  Reported by a user who saw exactly 404 animations offered for every Phantom
  Pain character, while FoxBrowser played `player2_resident.mtar` on all of them
  without complaint.

### Added

- **The version is on screen** — in the window title, the browser title and the
  first line of the log. "Is the fix in the copy I am running?" should not
  require diffing binaries.

## [1.6.0] - 2026-08-17

### Fixed

- **The animation-set list hid what it could not use.** A set was dropped from
  the window silently if it fell under the matching-bone threshold, and equally
  silently if it could not be read or decoded at all. Three different problems,
  one indistinguishable symptom: the archive you were looking for is simply not
  there, with nothing to say whether it was missing, broken, or merely judged a
  poor fit.

  Every set is now kept and labelled — `does not fit (3 bones matched)`, or
  `could not be read (...)` — with a **Show every set** tick and a search box
  beside the character list. Ticking a set by hand overrides the fit check on
  export, because ticking it is a decision; "everything that fits" still filters.
  A forced set that does not fit says so in the log, and says what to change.

### Added

- **`--why-mtar <set>`**: why one archive is or is not offered for a character —
  whether it is indexed, whether it reads, whether it carries a skeleton list,
  and how many bones it matches against the threshold. "It is not in the list"
  has four causes needing four different responses; this says which one it is.
- Ticks in the set list survive filtering, so narrowing the view cannot quietly
  discard a choice that scrolled out of sight.

## [1.5.0] - 2026-08-17

### Fixed

- **Metal Gear Online's archives were never read.** The Phantom Pain profile
  swept `master\` and the game root, but MGSV installs a second tree at `mgo\`,
  and that is the only place the male and female avatar models and their motions
  live. Everything under it was invisible to the index — not mis-ranked, not
  filtered out, simply never looked at. `mgo\` is now swept alongside `master\`.
  Adding it changes the archive fingerprint, so Phantom Pain re-indexes once on
  the next run; the scan is resumable, so an interrupted one picks up.

### Added

- **Animation-first commands**, for when you know which animations you want and
  are looking for a model to hang them on. Fox Engine binds animation to
  *skeletons*, so that question has a real answer.

  - `--list-sets [text]` — every animation archive in the game with its clip and
    bone counts, no character needed. Also reports whether the archive carries a
    skeleton list, which is why FoxBrowser's own compatibility check comes up
    empty on some of them.
  - `--list-clips <set>` — the clip names inside one archive.
  - `--for-mtar <set>` — the models that can play an archive, ranked by how much
    of the *animation's* skeleton each one has. This is how you find a base model
    for a whole locomotion set.
  - `--model-filter <text>` and `--all-models` to widen or narrow that search.

- `--filter-any a,b,c` — keep clips matching any of several fragments, rather
  than one substring.
- `--locomotion` — shorthand for the standard walk / run / crouch / turn / idle
  name fragments. A starting point, not a truth: check it with `--list-clips`
  and extend it, since nothing in the game files marks a clip as locomotion.

## [1.4.0] - 2026-08-16

### Fixed

- **The wrong rig was chosen, and characters came out stretched.** A candidate
  `.frig` was scored as `min(SegmentCount, boneCount)` — its own size, with no
  test that it had anything to do with the character — and the first one to clear
  the model's bone count won. Ground Zeroes has few enough rigs that this was
  usually right by accident. The Phantom Pain has thousands, so a foreign rig
  would win: a 144-bone rig for a 94-bone soldier, in the case that turned this
  up. Bone drives resolve by name hash and every soldier shares the standard
  `SKL_` names, so the wrong rig's units and segments were then applied to
  whichever bones the two skeletons had in common. The visible result was the
  neck and the help bones sliding out of the body with the mesh stretching after
  them, on nearly every clip.

  Rigs are now scored by real overlap with the skeleton — matched bones against
  the union of both bone sets, so a rig that merely *contains* the character's
  bones no longer beats the one that *is* the character's rig — with a rig named
  or filed like the model preferred as a tie-break. Below a plausibility floor
  nothing is returned at all: an unsolved help bone looks slightly stiff, while a
  wrong rig tears the character apart and does it silently.

  On a run that matters, the log now says how many of the skeleton's bones the
  chosen rig actually covers, and warns when the best available is a poor fit.

### Added

- **A preview window.** Pick a character, press *Preview…*, and step down the
  clip list with the arrow keys while each one loops. Orbit, pan and zoom with
  the mouse; matcap and skeleton toggle independently; the transport scrubs and
  plays at quarter to double speed.

  It renders through a software rasteriser — no OpenGL, no native libraries, no
  extra files beside the executable, and it works over Remote Desktop. The pose
  comes from the game's own animation solve rather than from anything an exporter
  wrote, so a clip that looks right here and wrong in Blender means the export is
  at fault, and one that looks wrong here does not fit the character.

- **`--list-rigs`**: the rigs that fit a character, best first, with the matched
  bone count and overlap for each. The top row is the rig that will be used.
- `tests/preview`: renders known shapes and checks the pixels — that near
  geometry occludes far geometry, that shading near a vertex matches that
  vertex's normal, that skinning moves the weighted vertices and only those, and
  that a triangle straddling the camera plane does not smear across the frame.

### Changed

- Every caller of FoxBrowser's animation solve now holds one lock. It returns
  intermediate results through static fields, so two threads in it at once do not
  throw — they hand each other the wrong skeleton. With only the export loop
  calling it this was theoretical; playing a clip while an export runs makes it
  real.

## [1.3.1] - 2026-08-16

### Fixed

- **Blender add-on: clips did nothing on Blender 4.4 and later.** The character
  stayed in its rest pose — a T-pose — while the Action Editor showed a full set
  of keyframes. Blender 4.4 gave Actions *slots*, and F-curves only animate once
  a slot is bound to the object. The FBX importer names a clip's slot after the
  temporary armature it builds for the clip file, which the add-on then throws
  away, so assigning the leftover Action to the model's armature bound nothing
  and nothing moved. Slots are now renamed after the armature they belong to at
  import time, which makes Blender's own name-based binding work everywhere —
  this add-on's panel, the Action Editor dropdown, the NLA, a linked file — not
  only in the one code path the add-on controls.

  Blender 4.2 and 4.3 were never affected. Scenes already imported with an older
  version of the add-on are repairable in place: select the armature and use
  **Animation Library ▸ Repair Slots**.

### Added

- Blender add-on: **Repair Slots** in the Animation Library panel, and a warning
  box that appears when an Action is assigned but not bound, which was
  previously a silent failure with no clue in the interface.
- `tests/test_slots.py`: imports a model and a clip, assigns it, and evaluates
  the deformed mesh at two frames. Every cheaper check — F-curves exist, bone
  names match, modifiers and weights are present — passed while the bug was
  live; only comparing evaluated vertices catches it.

## [1.3.0] - 2026-08-16

### Added

- **`--export-model`** (and *Also export the character model* in the GUI): rips
  the character itself — mesh, skeleton, materials and textures — in the layout
  the Blender add-on expects, so one run produces everything. Verified against
  Ground Zeroes: 45 meshes, 138 bones, 22 materials, 31 textures, matching
  FoxBrowser's own rip.
- **`--dedupe`**: folds clips whose baked motion matches one already written.
  Each clip gets a quantised signature over its pose data, hashed on two offset
  grids so a near-match cannot fall either side of a rounding boundary.
- **`--pack <n>`**: writes n clips per FBX as separate takes instead of one file
  per clip, cutting Blender's per-file import overhead by a factor of n and
  storing the skeleton once instead of n times.
- Blender add-on: **Animation Library** panel — a searchable, filterable list of
  imported clips showing length and bone coverage, with assign / stash / delete
  and a bulk delete for the current filter.
- Blender add-on: multi-take files import as one Action per take, named after
  the take, tagged with the clip's set, frame count and bone coverage.

- **Resumable game index.** A full Phantom Pain scan is 13 archives and tens of
  gigabytes; the index is now written after each archive, so a scan that is
  interrupted picks up where it stopped instead of starting over.

### Fixed

- Phantom Pain patch layers were missed. Archives now get a two-level sweep
  under a named data folder, so `master\0\00.dat` and
  `master\1\MGSVTUPDATEV0110\*.dat` are found alongside `master\chunk*.dat`.
  The game root itself is still read flat, so unrelated `.dat` files elsewhere
  in an install are not dragged in.

### Verified

- Phantom Pain, against a real install: detection from `mgsvtpp.exe`, all 13
  archives located across `master\`, `master\0\` and `master\1\`, texture
  archives excluded, and 3,339 models / 568 animation sets indexed from the
  real data before the test harness's time limit cut the scan short.

## [1.2.0] - 2026-08-16

### Added

- **Several characters in one run.** Ctrl-click in the GUI's character list, or
  repeat `--character` (comma-separated lists work too). Each character gets its
  own rig, its own compatibility check and its own output folder,
  `<out>/<character>/<set>/<clip>.fbx`. `--limit` applies per character.
- **Dark theme**, on by default, with Light and System options that persist.
  Includes the title bar via `DwmSetWindowAttribute`.
- **Portable by default.** Unpacked assemblies, name dictionaries, archive index
  and settings all live in `data\` beside the executable. Falls back to
  `%LOCALAPPDATA%` only when that folder is not writable. New `--portable`,
  `--no-portable` and `--where`.
- Blender add-on: bulk animation import now recognises the per-character folder
  layout and scopes itself to the selected armature's own folder, and can filter
  clips by frame count and matched-bone count using the exporter's `index.tsv`
  before importing anything.
- Blender add-on: "Select all shown" / "Clear" for the character list.

## [1.1.0] - 2026-08-16

### Added

- **Graphical interface.** `foxanimrip.exe` now opens a window when
  double-clicked: pick a game, pick a character by name, pick animation sets,
  choose a folder, export. It still behaves as a command-line tool when given
  arguments.
- **Game detection.** Finds installs through Steam's library folders, the
  Windows registry, common install paths and FoxBrowser's own saved folder.
  Profiles for Ground Zeroes, The Phantom Pain and Metal Gear Survive, plus a
  custom-folder option for any other Fox Engine title.
- **Archive catalogue.** Indexes the models and animation sets in a game once
  and caches the result, so characters can be picked by name instead of by
  hunting for an `.fmdl`. New `--character`, `--list-games`, `--list-models`,
  `--list-mtars` and `--rescan` options.
- **Bulk animation import** in the Blender add-on: a folder of clips becomes one
  Action per clip on the selected armature, with optional NLA stashing and asset
  marking.
- Cross-platform console build (`foxanimrip-cli`).
- `tools/extract-refs.py` and build scripts, so the project can be built from
  source without redistributing any of FoxBrowser's assemblies.

### Fixed

- **Compatibility scanning missed most animation sets.** An `.mtar` only carries
  a bone-hash table when its header sets `HAS_SKEL_LIST`, and many archives —
  `SoldierGz_layers` among them — do not. Those now fall back to decoding a few
  clips and resolving them against the skeleton. For Snake in Ground Zeroes this
  is the difference between finding 5 sets and finding all 13 (~3,900 clips).
- Installer leftovers (`unins*.dat`) are no longer treated as game archives.
- `--list-models` no longer repeats a model once per archive copy.

### Changed

- `--scan` renamed to `--all`; the old name still works.

## [1.0.0] - 2026-08-16

### Added

- Initial release: bulk export of Fox Engine animations by driving FoxBrowser's
  own decoder, rig solve and FBX writer, read from the user's own installation.
- One skeleton-only FBX per clip, with an `index.tsv` manifest.
- Automatic discovery of the model's `.frig` rig and `.frdv` help-bone operators.
- Blender add-on: single / bulk / recursive model import, material rebuilding
  (DXT5nm normals, `_srm` channel split, alpha), and `_rig.json` application.

### Fixed

- **FoxBrowser's FBX animation cannot be imported by Blender.** Its animation
  objects are tagged `AnimationStack` / `AnimationLayer` instead of `AnimStack` /
  `AnimLayer`, which makes Blender's FBX importer assert and abort part-way
  through. Every file written is corrected; the add-on repairs files that came
  straight out of FoxBrowser too.
- **Bone names came out as hashes.** FoxBrowser's name dictionaries load
  relative to the running executable, so they are now staged beside the tool.
- Clips where nothing moves are detected and skipped rather than written as
  files with no animation in them.
