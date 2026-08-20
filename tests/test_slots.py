# SPDX-License-Identifier: GPL-3.0-or-later
"""
Regression test: an imported clip must actually deform the model.

    blender --background --factory-startup --python tests/test_slots.py -- <export-dir>

<export-dir> is a ``foxanimrip --export-model`` output: a model FBX beside the
per-set clip folders.

The bug this guards against is invisible to every cheaper check. On Blender 4.4
and later an Action carries *slots*, and the F-curves only animate once a slot
is bound to the object. A clip's slot is named after the throwaway armature the
FBX importer built, so assigning it to the model's armature bound nothing: the
Action Editor showed a full set of keyframes and the character stood in its rest
pose. Checking that F-curves exist, that bone names match, that the modifier and
weights are there -- all of that passed. The only check that fails is evaluating
the deformed mesh at two frames and comparing the vertices.
"""

import os
import shutil
import sys

import bpy

ADDON = "io_foxbrowser"
FAILURES = []


def check(condition, message):
    print(("  ok   " if condition else "  FAIL ") + message)
    if not condition:
        FAILURES.append(message)


def install():
    here = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    src = os.path.join(here, "blender", ADDON)
    addons = bpy.utils.user_resource('SCRIPTS', path="addons", create=True)
    dst = os.path.join(addons, ADDON)
    shutil.rmtree(dst, ignore_errors=True)
    shutil.copytree(src, dst, ignore=shutil.ignore_patterns("__pycache__"))
    if addons not in sys.path:
        sys.path.append(addons)
    bpy.ops.preferences.addon_enable(module=ADDON)


def find_inputs(base):
    model = None
    for entry in sorted(os.listdir(base)):
        if entry.lower().endswith(".fbx"):
            model = os.path.join(base, entry)
            break
    clips = None
    for entry in sorted(os.listdir(base)):
        path = os.path.join(base, entry)
        if not os.path.isdir(path):
            continue
        if any(f.lower().endswith(".fbx")
               for _, _, files in os.walk(path) for f in files):
            clips = path
            break
    return model, clips


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not argv:
        print("usage: ... --python tests/test_slots.py -- <export-dir>")
        sys.exit(2)
    base = argv[0]

    install()
    from io_foxbrowser import slots

    print("Blender %s, slotted actions: %s"
          % (bpy.app.version_string, slots.HAS_SLOTS))

    model, clips = find_inputs(base)
    if not model or not clips:
        print("no model FBX and clip folder under %r" % base)
        sys.exit(2)
    print("model: %s\nclips: %s\n" % (model, clips))

    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)

    bpy.ops.foxbrowser.import_files(
        'EXEC_DEFAULT', filepath=model, directory=os.path.dirname(model),
        files=[{"name": os.path.basename(model)}])
    armature = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    check(armature is not None, "the model imported an armature")
    check(len(meshes) > 0, "the model imported meshes")
    if armature is None or not meshes:
        return

    for obj in bpy.context.scene.objects:
        obj.select_set(False)
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature

    before = set(bpy.data.actions)
    bpy.ops.foxbrowser.import_animations('EXEC_DEFAULT', directory=clips, limit=1)
    new = [a for a in bpy.data.actions if a not in before]
    check(bool(new), "importing a clip produced at least one Action")
    if not new:
        return

    # Pick the clip that drives the most of this rig, so the test is not at the
    # mercy of whichever clip happens to sort first.
    deform_bones = {b.name for b in armature.data.bones}

    def coverage(action):
        names = set()
        for curve in action.fcurves:
            if curve.data_path.startswith('pose.bones["'):
                names.add(curve.data_path[12:curve.data_path.find('"]', 12)])
        return len(names & deform_bones)

    action = max(new, key=coverage)
    print("\nclip: %s (%d bones, %d fcurves)"
          % (action.name, coverage(action), len(action.fcurves)))
    check(len(action.fcurves) > 0, "the clip has F-curves")
    check(coverage(action) > 0, "the clip's F-curves name bones on this armature")

    # -- the assignment path the panel and the Action Editor both use ---------
    if armature.animation_data is None:
        armature.animation_data_create()
    slots.retarget(action, armature)
    bound = slots.bind(armature.animation_data, action, armature)
    if slots.HAS_SLOTS:
        check(bound is not None, "assigning the clip bound an Action slot")
        check(slots.is_bound(armature.animation_data),
              "the armature reports a bound Action")
        names = [s.name_display for s in slots.object_slots(action)]
        check(armature.name in names,
              "the slot is named after the armature (%r in %r)"
              % (armature.name, names))

    # -- the check that actually catches it ----------------------------------
    scene = bpy.context.scene
    first = int(action.frame_range[0])
    mid = int((action.frame_range[0] + action.frame_range[1]) / 2)
    check(mid > first, "the clip is longer than one frame")

    def evaluate(frame):
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        graph = bpy.context.evaluated_depsgraph_get()
        out = {}
        for obj in meshes:
            evaluated = obj.evaluated_get(graph)
            mesh = evaluated.to_mesh()
            out[obj.name] = [v.co.copy() for v in mesh.vertices]
            evaluated.to_mesh_clear()
        return out

    a, b = evaluate(first), evaluate(mid)
    moved = {}
    for obj in meshes:
        moved[obj.name] = max(
            ((p - q).length for p, q in zip(a[obj.name], b[obj.name])), default=0.0)
    worst = max(moved.values(), default=0.0)
    deforming = sum(1 for d in moved.values() if d > 1e-5)

    print("\n  %-34s %s" % ("mesh", "max vertex travel"))
    for name in sorted(moved):
        print("  %-34s %10.5f%s"
              % (name[:34], moved[name], "" if moved[name] > 1e-5 else "   (static)"))

    check(worst > 1e-4,
          "the clip deforms the model (worst vertex travel %.5f)" % worst)
    check(deforming > 0,
          "%d of %d meshes deform" % (deforming, len(meshes)))

    # -- the repair path, for scenes imported before the fix ------------------
    if slots.HAS_SLOTS:
        # Recreate the broken state exactly: a slot named after the throwaway
        # armature, on an object with no memory of ever having used it.
        for slot in slots.object_slots(action):
            slot.name_display = "Armature"
        armature.animation_data_clear()
        armature.animation_data_create()
        armature.animation_data.action = action
        broken = not slots.is_bound(armature.animation_data)
        check(broken, "the pre-fix state still reproduces (slot does not bind)")
        bpy.ops.foxbrowser.action_rebind('EXEC_DEFAULT')
        check(slots.is_bound(armature.animation_data),
              "Repair Slots re-binds a broken Action")

    print("\n%s (%d failure(s))"
          % ("FAILED" if FAILURES else "PASSED", len(FAILURES)))
    for failure in FAILURES:
        print("  - " + failure)
    sys.exit(1 if FAILURES else 0)


main()
