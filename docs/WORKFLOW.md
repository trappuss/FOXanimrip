# foxanimrip — workflow & folder reference

A quick map of the pieces and how they fit. Full option help: `foxanimrip-cli.exe --help`.

## The two tools

- **`foxanimrip-cli.exe` / `foxanimrip.exe`** — the ripper (console + window). Reads a
  Metal Gear game install and writes model FBX + textures, animation clips, and
  customization/variation textures.
- **`io_foxbrowser-*.zip`** — the Blender add-on. Imports what the ripper wrote,
  rebuilds Fox Engine materials, browses models, assembles characters, and drives
  an animation library. Install via Blender → Preferences → Add-ons → Install.

## Games

`--game tpp | gz | survive` (auto-detected if omitted). Pass `--root "<folder>"` to
point at a specific install. The three the tool knows:

- MGSV: The Phantom Pain / MGO — `…\steamapps\common\MGS_TPP`
- MGSV: Ground Zeroes — `…\Metal Gear Solid V - Ground Zeroes`
- Metal Gear Survive — `…\steamapps\common\METAL GEAR SURVIVE`

## The bats (double-click; each logs to `test-logs\`)

| Bat | What it does |
|---|---|
| `test-rip-all-anims.bat` | Every animation in all three games, both motions, origin-mirrored tree + coverage report. |
| `test-rip-avatars.bat` | MGO avatar heads/bodies/hair (from TPP). |
| `test-rip-survive-chars.bat` | The 419 Survive character parts + fova skins. |
| `test-rip-survive-avatars.bat` | Survive-native avatar heads/bodies (on the Survive skeleton). |
| `test-rip-mgo-gear.bat` | MGO gear (hats, chest, suits, …). |
| `test-measure-locomotion.bat` | Authored locomotion numbers → `locomotion-params.tsv` (no FBX). |

After a run, the working style is: the bat logs everything, you say **"read"**, and the
logs get checked.

## Folder layout (`rips\`)

```
rips\
  <rip-name>\
    models\
      <model>\<model>.fbx        model mesh + skeleton
              <model>_textures\  extracted .dds (bsm/nrm/srm/…)
              <model>_maps.tsv   base→normal/spec, incl. hash-named textures
              <model>_rig.json   bone hashes / rig info
              <model>_source\    the untouched .fmdl
    fova\textures\               customization skin tones (per-tone .dds)
  all-anims\<Game>\<motion>\Assets\...\<mtar>\<clip>.fbx   origin-mirrored clips
```

Model exports always carry the sidecars above; animation clips never do — that is how
the add-on's browser tells the two apart.

## Blender: assemble a character

1. Sidebar (`N`) → **FoxBrowser → Model Browser**. Set the models folder, **Scan**.
2. Filter **Game** + **Category** (Head, Headgear, Arms, Legs, Body, Base body, Hair,
   Eyewear). Double-click a **base body** (bsm0/bsf0 for Survive, skl0 for MGO) to import.
3. Keep its armature selected, double-click each **part** to add it onto that rig.
4. Select the rig → **Rewire Materials** for the full texture treatment.

Ground Zeroes characters are single models: just import.

## Blender: animation library

Import a clip pack (or the origin tree) onto a matching armature, then
**FoxBrowser → Animation Library**: search/filter, and double-click a clip to assign it
to the selected armature.

## Notes on textures

Most textures resolve to real names via the bundled community dictionary
(`FoxBrowser\dict\qar_dictionary.txt`, ~388k paths). A minority — some avatar faces —
are stored hash-named and are not in any public dictionary; the tool still extracts the
real image data and records it in `_maps.tsv`, so the add-on textures them correctly,
just under a hex filename. See `docs/MGSV-locomotion-1to1-feasibility.md` for the deeper
texture/locomotion background.
