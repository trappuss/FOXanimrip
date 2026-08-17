# Changelog

All notable changes to this project are documented here.
This project follows [Semantic Versioning](https://semver.org/).

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
