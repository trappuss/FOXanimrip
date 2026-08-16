# FoxBrowser Import for Blender

Imports [FoxBrowser](https://www.nexusmods.com/metalgearsolidvtpp/mods/2531)
model exports from **Metal Gear Solid V: The Phantom Pain** and **Ground
Zeroes** — one model, a folder of them, or a whole extracted archive tree.

Requires **Blender 4.2 or newer**.

---

## Install

1. Download `io_foxbrowser-1.0.0.zip`.
2. Blender → **Edit ▸ Preferences ▸ Add-ons ▸ ⌄ ▸ Install from Disk…**
3. Pick the zip. It appears as **FoxBrowser Import (MGSV / Ground Zeroes)**.

## Use

**File ▸ Import ▸ FoxBrowser (MGSV / Ground Zeroes)**, or the **FoxBrowser**
tab in the 3D viewport sidebar (`N`).

| Mode | What it does |
| --- | --- |
| **Model(s)…** | One file, or ctrl-click several in the file browser |
| **Folder (Bulk)…** | Every model directly inside the chosen folder |
| **Folder Tree (Recursive)…** | The chosen folder and everything under it |
| **Animations onto Armature…** | A folder of clip files → one Action per clip |

Each model lands in its own collection at the world origin, nested under a
shared `FoxBrowser Imports` collection. Nothing is offset, so you isolate
models by toggling collection visibility.

A model exported as both `.fbx` and `.dae` is imported **once**, preferring
`.fbx`. Turn on *Import Every Format* if you actually want both.

Every run writes a `FoxBrowser Import Log` text block listing what was
repaired, guessed, or skipped. A batch never aborts on one bad file.

---

## What it fixes that a plain FBX import does not

### 1. The animation block

FoxBrowser tags its FBX animation objects `AnimationStack` / `AnimationLayer`.
Every other FBX consumer — the Autodesk SDK, Maya, Blender — expects
`AnimStack` / `AnimLayer`; the long spelling belongs only in the `Definitions`
table. Blender 4.x hits this in `import_fbx.blen_read_animations`:

```
stack_name = elem_name_ensure_class(fbx_asdata, b'AnimStack')
    assert(elem_class == clss)      # AssertionError
```

and aborts part-way through, leaving a half-built scene and no animation.

This add-on rewrites those two strings into a temporary copy of the file and
imports that instead. It is a full FBX re-serialisation — node headers store
*absolute* end offsets, so shortening a string moves every offset after it —
but property payloads, including the compressed vertex arrays, are copied byte
for byte and never re-encoded. Your original file is never touched.

If the repair is disabled or fails, the import automatically retries without
animation rather than dying.

### 2. Normal maps

Fox Engine packs tangent-space normals into DXT5 as **X in alpha, Y in green**.
The RGB colour block is a constant dummy — `(131, 125, 131)` in every export
checked, carrying no information at all. Feeding that straight into a Normal
Map node gives you flat, faintly-wrong shading because the real X never reaches
the red channel.

Imported normals go through a **FoxEngine Normal (DXT5nm)** node group that
takes X from alpha, Y from green, and reconstructs Z as `sqrt(1 - x² - y²)`.
The image is set to Non-Color and `CHANNEL_PACKED` so Blender does not
premultiply the alpha and destroy X.

`_hnm` maps use the identical packing and are treated the same way; several
materials use `_hnm` *instead of* `_nrm`.

### 3. The maps the FBX never mentions

FoxBrowser writes the whole texture set to `<model>_textures/`, but only wires
`_bsm` and `_nrm`/`_hnm` into the FBX. `_srm` and friends are found by name.

Fox Engine numbers sibling maps inconsistently — `sna0_cnt1_def_bsm` pairs with
`sna0_cnt2_def_nrm` — so matching walks a ladder: exact name, then
digit-insensitive, then progressively shorter stems, then a longest-unique-prefix
fallback. That last rung only fires when the winner is *strictly* unique, and
every guess it makes is written to the import log.

Recognised suffixes:

| Suffix | Wired to |
| --- | --- |
| `_bsm` | Base Color (sRGB), plus Alpha when the name ends `_alp` |
| `_nrm`, `_hnm` | Normal, via the DXT5nm group |
| `_srm` | R → Specular IOR Level, G → Roughness (both remappable) |
| `_trm` `_ilm` `_lym` `_mtl` `_occ` | Loaded, labelled, left unconnected |

The `_srm` routing was measured, not guessed: across the sample exports, red
sits at 0 on alpha-card eyelashes and 0.93 on hard gear (specular), while green
tracks microsurface — 0.13 on eyes, 0.70 on skin, 0.87 on fabric (roughness).
Blue was constant or zero everywhere, so it stays unused. If your models
disagree, all three channels are remappable in the add-on preferences.

### 4. The rig JSON

`<model>_rig.json` carries what the FBX throws away. The add-on attaches it as
custom properties you can see in the sidebar:

* `fox_hash` — the 48-bit Fox Engine bone name hash, per bone
* `fox_rig_unit` — which procedural rig unit the bone belongs to; bones are
  also sorted into one **bone collection per rig unit**
* `fox_index`, `fox_rest` — original bone order and rest position
* model-level counts, and the clip's frame count and rate

Frame rate is applied properly: 59.94 fps becomes Blender's 60 / 1.001, not a
rounded 60.

---

## Bulk animation import

FoxBrowser bakes the animation that happens to be playing into the model
export, one clip at a time. The companion tool **`foxanimrip`** exports every
gani in an mtar — or every gani in the game that fits your model — as one
skeleton-only FBX per clip. This add-on turns that folder into an Action
library.

Select the armature, then **Animations onto Armature…**, and point it at the
output folder. Each clip becomes one Action named after the file, with a fake
user so the library survives a save and reload.

**The one setting that matters:** *Automatic Bone Orientation* must be the same
as when you imported the model. Actions store *local* bone transforms, so if
the model came in with Blender's reorientation off and the clips with it on,
every rotation lands in a different local frame and the result is scrambled.
Both importers default to off.

**Multi-character exports.** When `foxanimrip` exports several characters it
writes one folder per character. Point the importer at the top of that and it
looks for a folder matching the selected armature's model name and imports only
that one — so you do not end up with three characters' clips on one rig. Turn
off *Match Folder to Armature* if you want everything regardless.

**Multi-take files.** `foxanimrip --pack 50` writes fifty clips into one FBX as
separate takes. The importer creates one Action per take, named after the take
rather than the file, and tags each with the animation set, frame count and bone
coverage from the manifest. Importing 4,000 clips as 80 files instead of 4,000
is the difference between minutes and most of an hour.

**The Animation Library panel** (sidebar, *FoxBrowser* tab) is where those
Actions become usable: search by name, filter by animation set or minimum
length, see each clip's length and how many bones it drives, then assign it to
the selected armature, stash it on a muted NLA track, or delete it. There is a
bulk delete for everything the current filter shows.

**Filtering before importing.** The exporter writes an `index.tsv` listing every
clip's frame count and how many bones it drives. *Min. Frames* and *Min. Matched
Bones* use it to drop clips before any of the expensive work happens, which is
how you get "the long clips that drive the whole body" out of four thousand
without importing the other 3,800.

Also available: a name filter, a limit, an Action name prefix, stashing every
clip on its own muted NLA track, and marking Actions as assets for the Asset
Browser. Clips whose bones do not exist on the target armature are reported and
discarded rather than left cluttering the file.

Clips where nothing moves — Fox Engine's single-frame pose snapshots — carry no
curves at all, because FoxBrowser's FBX writer only emits a channel that
changes. `foxanimrip` skips those by default and counts them separately.

---

## Options worth knowing

**Mesh Parts** — Fox Engine models arrive as dozens of small parts
(`MESH_head_0` … `MESH_head_23`). *Merge per Group* joins them back into
`MESH_head`, `MESH_body`, `MESH_arm` and so on, keeping every material slot.

**Flatten Hierarchy** — parents every mesh straight to the armature and deletes
the leftover `MESH_*` empties, keeping transforms.

**Automatic Bone Orientation** — off by default. On keeps Blender happier for
animation work; off preserves Fox Engine's own bone axes, which is what you
want if the model is going back into the game.

**Prefix Material Names** — renames materials to `<model>__<material>`. Worth
turning on for bulk runs, since Fox Engine reuses names like `body1` and
`head_hair` across every character.

**Rewire Materials** (sidebar) — rebuilds materials on the current selection
using the settings you pick in the dialog. Use it to try a different `_srm`
routing or flip the green channel without re-importing.

---

## Known limitations

* `.fmdl` in `<model>_source/` is not read; the add-on works from what
  FoxBrowser exported. Those folders are skipped during recursive scans.
* `.dae` and `.obj` exports import through Blender's own loaders and carry no
  FBX material links, so material rebuilding falls back to whatever image nodes
  the importer created.
* Blender's FBX importer reparents skinned meshes to the armature and discards
  FoxBrowser's `MESH_*` null hierarchy, so *Merge per Group* groups by object
  name instead.
* Only DXT1/DXT5 `.dds` was present in the exports tested. Other DDS variants
  depend on Blender's own DDS support.

## Licence

GPL-3.0-or-later, matching Blender.
