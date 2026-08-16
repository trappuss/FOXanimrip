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
