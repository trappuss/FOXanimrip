"""Second pass: skinning, animation contents, and the degraded paths."""
import os
import shutil
import sys

import bpy

# Paths come from the environment so this runs on any machine:
#   FOXB_ADDON   the add-on source folder      (default: ../blender/io_foxbrowser)
#   FOXB_MODEL   a FoxBrowser model export     (<name>.fbx with its sidecars)
#   FOXB_CLIPS   a folder of foxanimrip clips  (animation test only)
_here = os.path.dirname(os.path.abspath(__file__))
SRC = os.environ.get("FOXB_ADDON",
                     os.path.join(os.path.dirname(_here), "blender", "io_foxbrowser"))
EXAMPLE = os.environ["FOXB_MODEL"]


addons = bpy.utils.user_resource('SCRIPTS', path="addons", create=True)
dst = os.path.join(addons, "io_foxbrowser")
shutil.rmtree(dst, ignore_errors=True)
shutil.copytree(SRC, dst, ignore=shutil.ignore_patterns("__pycache__"))
if addons not in sys.path:
    sys.path.append(addons)
bpy.ops.preferences.addon_enable(module="io_foxbrowser")

failures = []


def check(label, cond, detail=""):
    print(("  PASS  " if cond else "  FAIL  ") + label
          + (" :: %s" % detail if detail else ""))
    if not cond:
        failures.append(label)


def wipe():
    for coll in list(bpy.data.collections):
        bpy.data.collections.remove(coll)
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for act in list(bpy.data.actions):
        bpy.data.actions.remove(act)


def do_import(**kwargs):
    return bpy.ops.foxbrowser.import_files(
        'EXEC_DEFAULT', filepath=EXAMPLE,
        directory=os.path.dirname(EXAMPLE),
        files=[{"name": os.path.basename(EXAMPLE)}], **kwargs)


print("\n== 1. skinning and animation content ==")
do_import()
coll = bpy.data.collections["sna2_main0_def"]
meshes = [o for o in coll.objects if o.type == 'MESH']
arm = next(o for o in coll.objects if o.type == 'ARMATURE')

modded = [o for o in meshes if any(m.type == 'ARMATURE' for m in o.modifiers)]
check("every mesh has an armature modifier",
      len(modded) == len(meshes), "%d/%d" % (len(modded), len(meshes)))
targets = {m.object for o in meshes for m in o.modifiers if m.type == 'ARMATURE'}
check("modifiers point at the imported armature", targets == {arm}, str(targets))

vg = [o for o in meshes if len(o.vertex_groups) > 0]
check("meshes carry vertex groups", len(vg) == len(meshes),
      "%d/%d" % (len(vg), len(meshes)))
sample = meshes[0]
weighted = sum(1 for v in sample.data.vertices if v.groups)
check("weights present on %s" % sample.name, weighted == len(sample.data.vertices),
      "%d/%d verts" % (weighted, len(sample.data.vertices)))
print("  vertex groups on %s: %d, e.g. %s" %
      (sample.name, len(sample.vertex_groups),
       [g.name for g in sample.vertex_groups][:4]))

act = arm.animation_data.action
paths = {fc.data_path.split('"')[1] for fc in act.fcurves if '"' in fc.data_path}
check("action drives many bones", len(paths) > 20, "%d bones keyed" % len(paths))
check("keyed bones exist in the armature",
      paths.issubset({b.name for b in arm.data.bones}),
      str(sorted(paths - {b.name for b in arm.data.bones})[:4]))
kf = sum(len(fc.keyframe_points) for fc in act.fcurves)
check("24 keys per curve", all(len(fc.keyframe_points) == 24 for fc in act.fcurves),
      "%d curves, %d keys" % (len(act.fcurves), kf))
check("action has a fake user", act.use_fake_user)

# the pose actually moves between frames
bpy.context.scene.frame_set(1)
a = arm.pose.bones["SKL_004_HEAD"].matrix_basis.copy()
bpy.context.scene.frame_set(20)
b = arm.pose.bones["SKL_004_HEAD"].matrix_basis.copy()
check("pose changes over time", any(abs(x - y) > 1e-5 for ra, rb in zip(a, b)
                                    for x, y in zip(ra, rb)))

print("\n== 2. UVs, vertex colours, custom normals ==")
me = sample.data
check("UV layer present", len(me.uv_layers) >= 1,
      ", ".join(l.name for l in me.uv_layers))
check("vertex colours present", len(me.color_attributes) >= 1,
      ", ".join(a.name for a in me.color_attributes))
check("custom split normals", me.has_custom_normals if hasattr(
    me, "has_custom_normals") else True)

print("\n== 3. normal group wiring ==")
ng = bpy.data.node_groups["FoxEngine Normal (DXT5nm)"]
comb = next(n for n in ng.nodes if n.type == 'COMBINE_COLOR')
red_src = comb.inputs["Red"].links[0].from_node
check("Red comes from the group's Alpha input", red_src.type == 'GROUP_INPUT'
      and comb.inputs["Red"].links[0].from_socket.name == "Alpha",
      comb.inputs["Red"].links[0].from_socket.name)
check("Green comes from a math node",
      comb.inputs["Green"].links[0].from_node.type == 'MATH')
check("Blue comes from a math node",
      comb.inputs["Blue"].links[0].from_node.type == 'MATH')
nm = next(n for n in ng.nodes if n.type == 'NORMAL_MAP')
check("Normal Map fed by Combine Color",
      nm.inputs["Color"].links[0].from_node.name == comb.name,
      nm.inputs["Color"].links[0].from_node.name)

print("\n== 4. no-animation import ==")
wipe()
do_import(import_animation=False)
arm = next(o for o in bpy.data.collections["sna2_main0_def"].objects
           if o.type == 'ARMATURE')
check("no action created", arm.animation_data is None
      or arm.animation_data.action is None)
check("model still imported", len([o for o in bpy.data.objects
                                   if o.type == 'MESH']) == 45)

print("\n== 5. repair disabled -> graceful fallback ==")
wipe()
res = do_import(repair_animation=False)
check("still finishes", 'FINISHED' in res, str(res))
coll = bpy.data.collections.get("sna2_main0_def")
check("model recovered after retry",
      coll is not None and len([o for o in coll.objects if o.type == 'MESH']) == 45,
      str(len([o for o in coll.objects if o.type == 'MESH']) if coll else 0))
arm = next((o for o in coll.objects if o.type == 'ARMATURE'), None)
check("no duplicate leftovers from the failed attempt",
      len([o for o in bpy.data.objects if o.type == 'ARMATURE']) == 1,
      str(len([o for o in bpy.data.objects if o.type == 'ARMATURE'])))
log = bpy.data.texts["FoxBrowser Import Log"].as_string()
check("failure explained in the log", "retrying without it" in log)

print("\n== 6. re-import into the same scene ==")
before = len(bpy.data.objects)
do_import()
check("second import adds a second collection",
      len([c for c in bpy.data.collections if c.name.startswith("sna2_main0_def")]) == 2,
      str([c.name for c in bpy.data.collections]))
check("objects doubled", len(bpy.data.objects) >= before + 45)

print("\n== 7. no textures folder ==")
wipe()
bare = "/tmp/bare"
os.makedirs(bare, exist_ok=True)
shutil.copy(EXAMPLE, os.path.join(bare, "bare.fbx"))
res = bpy.ops.foxbrowser.import_files(
    'EXEC_DEFAULT', filepath=os.path.join(bare, "bare.fbx"),
    directory=bare, files=[{"name": "bare.fbx"}])
check("imports without a _textures folder", 'FINISHED' in res, str(res))
log = bpy.data.texts["FoxBrowser Import Log"].as_string()
check("warned about the missing folder", "_textures" in log)

print("\n== 8. empty folder ==")
empty = "/tmp/empty"
os.makedirs(empty, exist_ok=True)
res = bpy.ops.foxbrowser.import_folder('EXEC_DEFAULT', directory=empty)
check("empty folder is cancelled, not crashed", res == {'CANCELLED'}, str(res))

print("\n=========================")
print("FAILURES: %d" % len(failures))
for f in failures:
    print("  -", f)
print("=========================")
sys.exit(1 if failures else 0)
