# Changelog

All notable changes to this project are documented here.
This project follows [Semantic Versioning](https://semver.org/).

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
