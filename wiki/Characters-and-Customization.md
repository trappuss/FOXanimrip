# Characters and Customization

## 1. Character asset layout

Characters are grouped by a short code under `/Assets/tpp/chara/<code>/`. By
volume in TPP:

| code | entries | who |
|------|---------|-----|
| `avm` | 5,272 | MGO avatar system (by far the largest) |
| `sna` | 1,094 | Snake |
| `dds` | 573 | Diamond Dogs soldiers |
| `qui` | 484 | Quiet |
| `svs` | 416 | — |
| `pfs` | 354 | — |
| `dct` | 277 | Code Talker |
| `kaz` | 276 | Kaz |
| `ptn` | 270 | — |
| `hrs` | 269 | horse |
| `chd` | 269 | child soldiers |

Each character folder uses the same three subfolders:

```
Fox_files/        model, rig and asset definitions  (35 for sna)
Fox_files/.fpklp/ packed-list variants of the same
Pictures/         textures                          (889 for sna)
Scenes/           assembled part / scene definitions (167 for sna)
```

`Scenes/` is where assembled variants live — `sna0_arm0_cov`, `sna0_arm1_cov`,
`sna0_arm2_cov` and so on — which is how one character supports many outfit
states.

## 2. Model naming

The convention is `<char><n>_<part><n>_<variant>`:

```
sna2_main0_def      Snake, main body, default   (GZ fatigues in TPP)
sna0_main0_def      Snake, another suit
avf0_body0_def      MGO avatar, female, body part 0
avm1_type7_def      MGO avatar, male, head/face preset 7
bsf0_main0_def      Survive, female player base body
skl0_main0_def      first-person arms  ← not a body
```

Part tokens seen on avatars and gear: `main` (whole body), `body`, `type`
(head/face preset), `arm`, `bdn` (bandana), `hair`, `hone`, `cnt`, `wmcs`,
`pacth` (patch), `sub`.

Variant tokens: `def` (default), `cov` (cover/variant), `v00`–`vNN`, `c00`
(colour 0).

### Two naming traps

- **`skl0_main0_def` is the first-person arms model** — `MESH_arm_0/1/2`, 1,378
  vertices, material `view`, no textures. It carries the full player skeleton
  and animates correctly, so it passes every automated check while having no
  body. Snake's body is `sna2_main0_def` (138 bones, 45 meshes, 31 textures).
- **`avf0_body0_def` is a torso part**, not a character. Rendering it alone
  produces a floating shirt — which is correct behaviour for that asset.

## 3. The MGO avatar system

MGO 3 builds a player from interchangeable parts on one shared skeleton. This is
the most systematic character data in the game, and the most confusing to
approach.

The part families:

| prefix | what it is |
|--------|-----------|
| `av[mf]N_bodyN` | base body (the torso/limb foundation) |
| `av[mf]N_typeN_def` | **the actual head/face** — eyes, mouth, skin, bandana |
| `hd[fm]*` | **headgear** — hats, masks, helmets, respirators. *Not* heads. |
| `hone` | head-adjacent gear |
| `ar` / `lg` / `ua` / `bd` / `cr` / `rg` | arm, leg, upper-arm, body, chest-rig, leg-rig gear |
| `hair` | hair pieces |

The `hd*` vs `av*_type*` distinction cost real time: searching for "heads" by
the `hd` prefix returns exclusively face *gear*. The heads are the
`av[mf]N_typeN_def` presets.

Head presets exist as `type0` through `type7` for each gender. **Only two of
them ship textured** — `avf0_type2_def` and `avf0_type7_def`; the rest arrive
with only the bandana textured. That is how they ship, not an extraction fault,
and face skin for the others is supplied through the FOVA variation system
rather than baked into the head model.

Assembly order that works: base body first, then head, then gear, all onto the
one armature.

MGO's own asset tree is small (`/Assets/mgo/`, 2,444 paths) because most of it
lives in the TPP tree; `/Assets/mgo/chara/avm` (115) and `/Assets/mgo/chara/hats`
(57) hold the MGO-specific pieces.

## 4. FOVA — form variation

`.fova` (extension code 4235) and `.fv2` (3089) are the **form variation**
system: how one model becomes many visual variants without duplicating geometry.

`/Assets/tpp/fova/` holds 3,025 paths:

| subtree | entries |
|---------|---------|
| `chara` | 1,704 |
| `weapon` | 632 |
| `common_source` | 352 |
| `mecha` | 226 |
| `item` | 69 |
| `environ` | 40 |

A variation entry does one of two things:

1. **swap a texture** — point a material's map at a different file, or
2. **set a shader value** — change a parameter without changing any texture.

Everything inside is identified by 32- or 64-bit hash, so names resolve only as
far as the dictionaries reach. A variation that swaps a texture can be followed
to the actual file; one that only sets a parameter yields a number whose meaning
lives in the shader.

This is the mechanism behind soldier variety, weapon skins, and avatar skin
tones — `ssd/fova/chara` supplies Survive's face-tone variations the same way.

## 5. Survive's differences

Survive is a fork of TPP's engine and keeps the formats intact. What differs:

- **Its own asset root**, `/Assets/ssd/` (10,091 paths), while still referencing
  the `tpp` tree — including `Assets/tpp/rig/frig/human_finger`, the same rig.
- **Character subtrees named by slot** rather than by character: `arm`, `avm`,
  `base`, `body`, `boss`, `boy`, `chest_rig`, `dmc`, `eng`, `glasses`, …, which
  is an even more systematic version of MGO's part system.
- **`SsdPlayer_layers`** as the player animation set, and `bsf0`/`bsm0` base
  bodies.
- **Shared animation.** Individual clip names are identical to TPP's, and the
  measured speeds match to within 1–3 %. Survive's player movement is TPP's
  player movement.

Because the skeletons are compatible, TPP and Survive assets can be mixed — but
only if each model is ripped against its *own* game's rig. Mixing a
Survive-ripped body with TPP-ripped animation works; assuming one rip covers
both does not.
