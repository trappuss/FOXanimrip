# Models and Skeletons

## 1. FMDL

`.fmdl` (extension code 4244) is the model container: geometry, materials,
texture references, and the skeleton. TPP holds **19,138** of them; Survive
**15,024**; GZ **1,017**.

Structurally, what a consumer needs:

- **Bones** — an ordered list. Each carries a `NameIndex` into the model's own
  string table, a `ParentIndex`, and both a `LocalPosition` and `WorldPosition`
  (stored as `Vector4`).
- **Meshes** — position, normal, tangent, up to four UV sets, vertex colour,
  triangle indices, a material index, and a skin.
- **Skin** — a *palette* of bone indices plus per-vertex indices into that
  palette and four weights. The palette indirection is per-mesh; resolve through
  it to get global bone indices.
- **MaterialTextures** — per material, a set of `(role, hash, path)` where role
  is `base`, `normal`, `spec` and others.

### The bind pose has no rotation

Fox bone rest is a **position only**. This is why an exporter can publish bone
transforms as three floats and lose nothing, and it means the inverse bind
matrix is simply a translation:

```
skin[b] = translate(-bindWorld[b]) · animWorld[b]
```

If a future format breaks that assumption, skinning shears in a way that looks
like a corrupt mesh rather than a bad matrix. **[verified]** across every
character model examined.

### Hierarchy invariant

With translation-only rest, this must hold for every bone:

```
world[i] == world[parent[i]] + local[i]
```

It is the cheapest possible check that a parsed hierarchy is correct, and it
caught real bugs. Run it before trusting anything downstream.

## 2. Bone naming

Names follow `SKL_<number>_<TAG>`:

```
SKL_000_WAIST      the root of the deform skeleton
SKL_001_SPINE
SKL_002_CHEST
SKL_003_NECK
SKL_010_LSHLD      left shoulder
SKL_030_LTHIGH
SKL_031_LLEG
SKL_032_LFOOT
SKL_033_LTOE
SKL_040_RTHIGH … SKL_043_RTOE
```

The numbering is a stable family scheme: 0xx spine and head, 01x/02x arms,
03x/04x legs, 5xx/6xx helpers.

Two suffix classes matter:

- **`_HLP` — help bones** (64 in the bone dictionary). Muscle/twist correctives,
  driven by the FRDV operator list rather than by animation curves. Their rest
  lengths are millimetres, so their *relative* motion is enormous and completely
  normal — do not treat that as an error. Example: `SKL_511_RHMRS_HLP`,
  `SKL_502_LHMRS_HLP`.
- **`_SIM` — simulation bones** (130). Physics-driven secondary motion: cloth,
  straps, hair, pouches. Example: `SKL_633_BRARD_SIM`. **[inferred]** from
  naming and placement; the simulation parameters themselves are elsewhere.

Bone names come from `bone_dictionary.txt` (561 entries) and the model's own
string table via `fmdl_dictionary.txt` (20,659). **Without the dictionary
staged, bones come out as `bone_<hex>` and nothing will bind.**

### The waist is the origin, not the floor

`SKL_000_WAIST` sits at `(0, 0, 0)`; the feet are at roughly **y = −0.96**.
Characters are authored about the pelvis. Anything that expects a
feet-on-the-floor origin must offset by the foot's bind height — read it from
the rig rather than hard-coding 0.96, since it varies by character.

Axis convention is **Y-up**. (Blender's FBX importer converts to Z-up on the way
in; that is the importer, not the data.)

## 3. Skeleton sizes

| model | bones | what it is |
|-------|-------|-----------|
| `skl0_main0_def` | 121 | **first-person arms** — see the trap below |
| `sna2_main0_def` | 138 | Snake, Ground Zeroes fatigues (45 meshes, 31 textures) |
| `avf0_body0_def` | 116 | MGO female avatar base body (a torso part, not a character) |
| `bsf0_main0_def` | 126 | Survive female player body |

Bone count varies because each model carries only the helper and simulation
bones its own geometry needs. The **deforming core is shared**, which is what
lets one animation set drive all of them.

### The `skl0_main0_def` trap

The obvious-looking name for "the TPP player" is the **first-person arms
model**: meshes `MESH_arm_0/1/2`, 1,378 vertices, one material named `view`,
zero textures. It carries the full player skeleton and animates perfectly — it
simply has no body. Several hours were lost to this. Snake's body is
`sna2_main0_def`.

Similarly `avf0_body0_def` is one *part* of an MGO avatar, not a whole
character; see [Characters and Customization](Characters-and-Customization).

## 4. FRIG — the rig

`.frig` (2276) holds the rig that turns animation channels into a pose:

- **Rig units** (18 for the human rig) and **segments** (56).
- **Bone drives** — mappings from animation tracks onto bones.
- **IK jobs** — the solvers, notably the legs. Without them, feet do not plant.

The canonical human rig is `/Assets/fox/rig/frig/human_finger`, and it resolves
as **53 bones** across TPP, MGO, GZ and Survive character models alike. A
correctly bound player model reports *53 of its 53 rig bones present* while
those 53 drive only ~40 % of the model's total bones — the rest are helpers and
simulation bones the rig does not address directly.

**Rig quality is the single best signal that an export is sound.** A clip that
drives 44 of 53 bones is properly bound; one driving 6 is bound to a stub.

### The 8-bone trap

Generic core bones (waist, spine, chest) are shared by nearly every humanoid
skeleton in the game, including props and stand-ins. A minimum-match threshold
of 8 bones is therefore *far* too permissive: a 15-bone prop skeleton will
"match" a full player animation set. Any automated model↔animation pairing must
rank candidates by matched-bone count, not accept the first over a threshold.
We shipped that bug; it is documented in
[Open Questions](Open-Questions).

## 5. FRDV — help bone operators

`.frdv` (6588) is the help-bone operator list — 23 to 32 operators on a player
model. These compute the corrective bones (`_HLP`) from the pose: twist
distribution along forearms and thighs, shoulder correctives, and so on.

They must be evaluated *after* the base pose and *before* skinning. Skipping
them produces a pose that is subtly wrong at the joints — collapsed elbows and
pinched shoulders — while looking broadly correct, which makes it a nasty class
of silent error.

## 6. Practical decode order

To get from an FMDL to a posed, skinned character:

1. Stage the name dictionaries, or every name is a hash.
2. Parse the FMDL: bones, meshes, skin palettes, material texture references.
3. Resolve the rig (`.frig`) for this skeleton, and its help bones (`.frdv`).
4. For a clip: decode the GANI, resolve its tracks onto bone indices, solve the
   bone drives, then the IK jobs, then the help-bone operators.
5. Skin with `translate(-bindWorld) · animWorld`, resolving skin palette indices
   to global bone indices.

Steps 3 and 4 are where a naive implementation goes wrong, and both failures are
quiet. The invariants in [Toolchain and Methods](Toolchain-and-Methods) catch
them.
