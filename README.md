# foxanimrip

**Bulk asset and animation export for Fox Engine games — plus a Blender
importer that knows what to do with the results, and a research handbook on
what is actually inside these files.**

[FoxBrowser](https://www.nexusmods.com/metalgearsolidvtpp/mods/2531) can rip a
character out of MGSV with the one animation that happens to be playing.
`foxanimrip` drives the same decoders headlessly, so you can pull *every*
animation that fits a character in a single pass — thousands of clips in
minutes — along with models, textures and the customisation data behind them.

| Model import | Animation import |
| --- | --- |
| ![Imported model](docs/model-import.png) | ![Imported animation](docs/animation-import.png) |

*Left: `sna2_main0_def` imported by the add-on, no hand-fixing. Right: frames
30 / 80 / 130 of `enemasr_s_run_f_r_ed_bl`, exported by `foxanimrip` and
imported as an Action.*

---

## 📖 The Fox Engine Asset Handbook

Fox Engine is finished; no further games will be built on it. What is in these
files is all there will ever be.

**[→ Read the handbook in the wiki](../../wiki)** — a detailed account of the
internals of Ground Zeroes, The Phantom Pain (with MGO 3) and Metal Gear
Survive, written so that the next person to open these files does not have to
rediscover it:

| Page | Covers |
|------|--------|
| [Archives and Hashing](../../wiki/Archives-and-Hashing) | The 64-bit path hash, the extension-code table, FPK containers, dictionaries |
| [Models and Skeletons](../../wiki/Models-and-Skeletons) | FMDL, bone naming, the shared 53-bone rig, FRIG/FRDV, bind-pose properties |
| [Textures and Materials](../../wiki/Textures-and-Materials) | FTEX streamed mips, the map-suffix vocabulary, DXT5nm, decode pitfalls |
| [Animation System](../../wiki/Animation-System) | MTAR, GANI, 59.94 fps, rig binding, the clip-name grammar |
| **[Locomotion Deep Dive](../../wiki/Locomotion-Deep-Dive)** | **How MGSV's movement is built — and the full measured speed table** |
| [Characters and Customization](../../wiki/Characters-and-Customization) | Asset layout, the MGO avatar part system, FOVA variation, Survive |
| [Toolchain and Methods](../../wiki/Toolchain-and-Methods) | How to extract and *verify* all of it |
| [Open Questions](../../wiki/Open-Questions) | What is still unknown, and the next experiment for each |
| [Prior Art and Tools](../../wiki/Prior-Art-and-Tools) | The other projects that have opened these files, and which to reach for |

Every claim in the handbook is measured from shipped files and says how it was
established. A short version of the movement findings is in
[docs/locomotion-findings.md](docs/locomotion-findings.md); the debugging
specifics are in [docs/engineering-notes.md](docs/engineering-notes.md).

One result worth putting on the front page — MGSV's authored player speeds,
recovered from the stance foot because the cycles carry no root travel:

| gait | m/s | | gait | m/s |
|------|-----|---|------|-----|
| walk (standing) | 1.117 | | run (standing) | 3.955 |
| walk (crouched) | 0.705 | | run (crouched) | 2.847 |
| jog | 2.308 | | sprint | 6.394 |

The same numbers appear, to within 1–3 %, in all three games.

---

## What you need

* **Windows 10/11 64-bit** and the **.NET 10 Desktop Runtime** — the same one
  FoxBrowser needs, so if FoxBrowser runs, this runs.
* **FoxBrowser** installed anywhere. `foxanimrip` reads what it needs out of
  your copy; nothing from FoxBrowser is bundled here.
* A **Fox Engine game** installed.
* **Blender 4.2 or newer**, for the add-on.

## Quick start

1. Download the release and unzip it anywhere.
2. Double-click **`foxanimrip.exe`** for the window, or use the command line.
3. Install **`io_foxbrowser-*.zip`** in Blender: *Edit → Preferences →
   Add-ons → Install from Disk*.

```bat
foxanimrip-cli.exe --game gz --character sna2_main0_def --all --out C:\rips\anims
```

That exports every animation set that fits Snake in Ground Zeroes. Then point
the add-on's **Animation Library** at `C:\rips\anims`.

### The ready-made scripts

Each `test-*.bat` in the release runs one job end to end and writes a log:

| script | what it does |
|--------|-------------|
| `test-rip-all-anims.bat` | every animation in all three games, mirrored to the origin paths |
| `test-rip-loco-loops.bat` | the player gait loops, correctly rigged |
| `test-measure-locomotion.bat` | measures every player clip, then builds `cruise-table.tsv` |
| `test-rip-avatars.bat` / `test-rip-survive-avatars.bat` | MGO and Survive avatar parts |
| `test-rip-survive-chars.bat` | Survive characters |
| `test-rip-mgo-gear.bat` | MGO gear and headgear |
| `test-ext-histogram.bat` | file-type census of an install |
| `test-gather.bat` | inventory: every model, texture and customisation option |

Edit the game paths at the top of each before running.

---

## Command line

Run `foxanimrip-cli.exe --help` for the full list. The commands that matter:

**Choosing what to export**

| flag | meaning |
|------|---------|
| `--game <id>` | `gz` \| `tpp` \| `survive` \| `custom`. Auto-detected if omitted. |
| `--root <folder>` | Game folder holding the archives. |
| `--character <name>` | A model, e.g. `sna2_main0_def`. Repeatable. |
| `--model <path>` | …or a loose `.fmdl` on disk. |
| `--mtar <file\|name>` | One animation archive. Repeatable. |
| `--all` | Every animation archive that fits the model. |
| `--all-sets` | No model needed: rip every set, each bound to the skeleton that fits it. **See the caveat below.** |
| `--out <folder>` | Where clips land. |

**Finding things**

| flag | meaning |
|------|---------|
| `--list-models [text]`, `--list-sets [text]`, `--list-clips <set>` | enumerate |
| `--for-mtar <set>` | which models can play this animation set |
| `--list-mtars`, `--list-rigs` | which sets fit this model; which rig will be used |
| `--why-mtar <set>` | why a set is or is not offered |
| `--inventory <dir>` | every model, texture and customisation option as TSV |
| `--rip-variations <f>` | extract what a form variation actually swaps |
| `--ext-histogram <f>` | count every file by extension code |

**Measuring**

| flag | meaning |
|------|---------|
| `--measure` | write `locomotion-params.tsv`: per clip, root travel, speed, net turn, turn rate, and `gait_mps` — the authored speed read off the stance foot |
| `--analyze-locomotion <path>` | summarise those into `cruise-table.tsv` |

**Output shaping**

| flag | meaning |
|------|---------|
| `--tree` | mirror each set's origin path instead of a flat folder |
| `--filter <text>`, `--filter-any a,b,c`, `--locomotion` | narrow by clip name |
| `--pack <n>` | n clips per FBX as separate takes — hugely faster to import |
| `--dedupe [deg]` | skip clips whose motion matches one already written |
| `--root-motion` | keep the root's travel (off by default) |
| `--with-mesh`, `--step <n>`, `--fps <f>`, `--limit <n>`, `--min-match <n>` | see `--help` |

> **Caveat on `--all-sets`.** It currently binds each set to the *first*
> skeleton clearing the matched-bone threshold rather than the *best* one, so
> 209 TPP sets — the player's among them — end up on a 15-bone stand-in whose
> clips animate no legs. It is fine for surveying what exists; use
> per-character rips for animation work. Details in
> [Toolchain and Methods](../../wiki/Toolchain-and-Methods).

---

## The Blender add-on

Lives in [`blender/io_foxbrowser/`](blender/io_foxbrowser/) with
[its own README](blender/io_foxbrowser/README.md).

* **Model import** — single file, bulk folder, or a whole recursive tree.
  Rebuilds what the FBX cannot carry: Fox's DXT5nm normal packing, the
  `_srm`/`_trm`/`_lym` maps nothing references, and bone hashes and rig-unit
  grouping from the `_rig.json` sidecar.
* **Model Browser** — a searchable catalogue of everything you have ripped,
  described in plain language. Double-click to import, or to add onto a
  selected character.
* **Character assembly** — queue a base body and parts, build in one click.
  Fox characters are assembled from parts on a shared skeleton, and this does
  that properly rather than merging meshes.
* **Animation Library** — turns a folder of clips into a searchable Action
  library, with Blender 4.4+ slot binding handled.

---

## How it works

`foxanimrip` extracts FoxBrowser's assemblies from your installed copy at run
time and calls them exactly the way its own rip button does: the FMDL and GANI
decoders, the FRIG rig solve, the FRDV help bones, and the FBX writer are all
FoxBrowser's. Nothing about the formats is reimplemented here.

What this project adds is everything *around* that: cataloguing the archives,
working out which animation sets fit which skeleton, running the export in
bulk, and measuring the results.

**Nothing from FoxBrowser is bundled or redistributed.** If FoxBrowser is not
installed, this does not run.

## Game support

| Game | Status |
|------|--------|
| **MGSV: Ground Zeroes** | Verified end to end |
| **MGSV: The Phantom Pain** | Verified — 19,138 models and 1,236 animation sets indexed; full exports run |
| **Metal Gear Online 3** | Verified via the TPP install |
| **Metal Gear Survive** | Verified — 15,024 models, 542 sets |
| **Anything else Fox Engine** | `--game custom` with `--root` |

Archive support is FoxBrowser's: `.dat`, `.g0s`, `.qar`, with `.fpk` / `.fpkd`
/ `.pftxs` nested inside.

## Building from source

Targets **.NET 10**. `FoxAnimRip.Core` compiles against FoxBrowser's assemblies
for API shape only — point `-p:FoxBrowserRefDir=` at a folder holding them
(`tools/extract-refs.py` makes one).

```bash
dotnet publish src/FoxAnimRip.Headless -c Release -r win-x64 \
    --self-contained false -p:PublishSingleFile=true \
    -p:FoxBrowserRefDir=/path/to/refs
```

`scripts/push-github.bat` builds, tags, publishes a GitHub release, pushes
`main` and syncs the wiki in one step.

## Project status

The extraction, measurement and Blender tooling are complete and verified.
A standalone locomotion test bench was built and then removed — the
measurements it was made to produce outlived it and are written down in the
handbook instead.

Known outstanding work is listed in
[Open Questions](../../wiki/Open-Questions); the largest by far is locating the
motion graph.

## Licence and credits

`foxanimrip` (everything under `src/` and `tools/`) is **MIT** — see
[LICENSE](LICENSE). The Blender add-on is **GPL-3.0-or-later**, because Blender
add-ons link against Blender's GPL Python API — see
[blender/io_foxbrowser/LICENSE](blender/io_foxbrowser/LICENSE).

This handbook also draws on work by others — **FoxKit-3** (Joey35233) for the
animation track format, **fox_engine_mtar_tools_blender** (mctrollin) for MTAR
round-tripping and the GANI1/GANI2 split, **mgsv-lookup-strings** (kapuragu) for
hash dictionaries, and **Fox_Parser** (Frostyman758) for broad format coverage.
Each is credited where it is used and collected in
[Prior Art and Tools](../../wiki/Prior-Art-and-Tools).

**FoxBrowser** is the work of its author and is not included, modified or
redistributed here; this tool reads the copy on your own machine. All the hard
parts — the FMDL and GANI decoders, the Fox Engine rig solve, the FBX writer —
are theirs. If you find this useful, go and endorse
[FoxBrowser on Nexus Mods](https://www.nexusmods.com/metalgearsolidvtpp/mods/2531).

Metal Gear Solid, Ground Zeroes, The Phantom Pain, Metal Gear Survive and Fox
Engine are trademarks of Konami. This project is not affiliated with Konami and
ships no game assets.
