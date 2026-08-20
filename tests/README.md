# Tests

Headless Blender tests for the add-on. They need the `bpy` module
(`pip install bpy==4.2.0`) and a real FoxBrowser export to work against — no
game assets are committed here.

```bash
export FOXB_MODEL=/path/to/sna2_main0_def.fbx     # a FoxBrowser model rip
export FOXB_CLIPS=/path/to/anims/SoldierGz_layers # foxanimrip output

python3 tests/test_import.py    # model import: materials, rig JSON, bulk, recursive
python3 tests/test_import2.py   # skinning, animation content, degraded paths
python3 tests/test_anim.py      # bulk animation import onto an armature

export FOXB_PACK=/path/to/a/folder/with/a/packed/multi-take/fbx
python3 tests/test_actions.py   # multi-take import and the Action browser
```

Each script prints PASS/FAIL per assertion and exits non-zero on any failure.

## test_slots.py

This one runs in a real Blender rather than against `bpy` as a module, because
it has to evaluate the depsgraph the way the viewport does:

```bash
blender --background --factory-startup \
        --python tests/test_slots.py -- /path/to/foxanimrip/output/<character>
```

The argument is a `--export-model` output folder: the model FBX beside the
per-set clip folders. The test imports the model, imports a clip, assigns it,
and compares the *evaluated* mesh vertices at two frames.

That last step is the point. Blender 4.4's Action slots made it possible for an
Action to be assigned, complete and visible in the Action Editor while animating
nothing at all. Checking that the F-curves exist, that the bone names match,
that the armature modifier and vertex weights are present — all of it passes
while the character stands in a T-pose. Comparing deformed vertices is the only
check that fails. Run it on both a 4.2-era and a 4.4+ Blender; it covers the
repair path too.

## tests/rig

```bash
dotnet run --project tests/rig
```

Which `.frig` belongs to a skeleton. Two real cases, from two opposite failures,
and the rule has to satisfy both: a 144-bone rig handed to a 94-bone soldier
because it was large (which stretched the character), and a 53-bone rig rejected
for a 120-bone player because it "only covered 44%" (which left the character
with no rig, so nothing played at all). Needs no game files — the numbers were
measured from real rips, and any rule that satisfies one case and not the other
fails here.

## tests/preview

```bash
dotnet run --project tests/preview -- /tmp/preview-out
```

The software rasteriser, checked on pixels rather than by eye. Pass a folder to
also write the frames out as PNGs.

## tests/grid

```bash
dotnet run --project tests/grid
```

Locomotion grid detection: which clip names form a complete eight-direction set
of starts, loops, turns and stops, and which merely look like it. Needs no game
files — the names are the data.

## tests/index

```bash
dotnet run --project tests/index
```

Whether the cached archive index notices that it is an older shape than the code
that is reading it. This is the one bug class the archive fingerprint cannot
catch: the game has not changed, only what is being looked for in it.

It exists because the first attempt at that guard silently did nothing.
`public int Schema { get; set; } = CurrentSchema;` looks like a sensible default,
but `System.Text.Json` leaves an absent property at its declaration initialiser —
so every cache written before the field existed deserialised claiming to be
current, passed the check, and was reused. The visible symptom was an inventory
run reporting no form variations at all on a game full of them.
