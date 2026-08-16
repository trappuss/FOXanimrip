"""Headless smoke test for the FoxBrowser import add-on."""
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
print("ADDON ENABLED")

p = bpy.context.preferences.addons["io_foxbrowser"].preferences
print("prefs:", p.normal_mode, p.srm_red, p.srm_green, p.parent_collection)

failures = []


def check(label, cond, detail=""):
    print(("  PASS  " if cond else "  FAIL  ") + label + (" :: %s" % detail if detail else ""))
    if not cond:
        failures.append(label)


# ---------------------------------------------------------------- single
res = bpy.ops.foxbrowser.import_files(
    'EXEC_DEFAULT',
    filepath=EXAMPLE,
    directory=os.path.dirname(EXAMPLE),
    files=[{"name": os.path.basename(EXAMPLE)}],
)
print("operator result:", res)

log = bpy.data.texts.get("FoxBrowser Import Log")
print("\n----- LOG -----")
print(log.as_string() if log else "(no log)")
print("---------------\n")

check("operator finished", 'FINISHED' in res)

coll = bpy.data.collections.get("sna2_main0_def")
check("per-model collection", coll is not None)
parent = bpy.data.collections.get("FoxBrowser Imports")
check("parent collection", parent is not None and coll is not None
      and coll.name in parent.children)

objs = list(coll.objects) if coll else []
meshes = [o for o in objs if o.type == 'MESH']
arms = [o for o in objs if o.type == 'ARMATURE']
print("  objects=%d meshes=%d armatures=%d empties=%d"
      % (len(objs), len(meshes), len(arms),
         len([o for o in objs if o.type == 'EMPTY'])))
check("45 mesh parts", len(meshes) == 45, str(len(meshes)))
check("one armature", len(arms) == 1)

arm = arms[0] if arms else None
if arm:
    check("fox_model prop", arm.get("fox_model") == "sna2_main0_def",
          str(arm.get("fox_model")))
    check("bone count prop", arm.get("fox_bone_count") == 138)
    check("clip fps prop", abs(arm.get("fox_clip_fps", 0) - 59.94) < 0.01)
    b = arm.data.bones.get("SKL_000_WAIST")
    check("bone hash", b is not None and b.get("fox_hash") == "ed0636c5b26b",
          str(b.get("fox_hash")) if b else "missing bone")
    check("bone rig unit", b is not None and b.get("fox_rig_unit") == 1,
          str(b.get("fox_rig_unit")) if b else "-")
    annotated = sum(1 for bone in arm.data.bones if "fox_hash" in bone)
    check("all 138 bones annotated", annotated == 138, str(annotated))
    names = [c.name for c in arm.data.collections]
    check("bone collections created", len(names) >= 2, ", ".join(names[:6]))
    act = arm.animation_data.action if arm.animation_data else None
    check("action renamed", act is not None and act.name == "sna2_main0_def|take",
          act.name if act else "no action")
    if act:
        print("  action frame range:", tuple(act.frame_range))

check("scene fps 59.94",
      bpy.context.scene.render.fps == 60
      and abs(bpy.context.scene.render.fps_base - 1.001) < 1e-6,
      "%s / %s" % (bpy.context.scene.render.fps,
                   bpy.context.scene.render.fps_base))

# ------------------------------------------------------------- materials
ng = bpy.data.node_groups.get("FoxEngine Normal (DXT5nm)")
check("normal node group", ng is not None)

mats = []
for o in meshes:
    for s in o.material_slots:
        if s.material and s.material not in mats:
            mats.append(s.material)
print("  materials:", len(mats))
check("22 materials", len(mats) == 22, str(len(mats)))

wired_normal = wired_rough = wired_spec = wired_alpha = 0
missing_images = []
for m in mats:
    bsdf = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if bsdf is None:
        continue
    if bsdf.inputs["Normal"].is_linked:
        wired_normal += 1
    if bsdf.inputs["Roughness"].is_linked:
        wired_rough += 1
    spec = bsdf.inputs.get("Specular IOR Level")
    if spec is not None and spec.is_linked:
        wired_spec += 1
    if bsdf.inputs["Alpha"].is_linked:
        wired_alpha += 1
    for n in m.node_tree.nodes:
        if n.type == 'TEX_IMAGE' and n.image is not None:
            img = n.image
            abspath = bpy.path.abspath(img.filepath)
            ok = os.path.isfile(abspath) and tuple(img.size) != (0, 0)
            if not ok:
                missing_images.append((m.name, img.name, img.size[:],
                                       os.path.isfile(abspath)))

print("  normal=%d roughness=%d specular=%d alpha=%d"
      % (wired_normal, wired_rough, wired_spec, wired_alpha))
check("normals wired on most materials", wired_normal >= 18, str(wired_normal))
check("roughness wired from srm", wired_rough >= 14, str(wired_rough))
check("specular wired from srm", wired_spec >= 14, str(wired_spec))
check("alpha wired on _alp materials", wired_alpha >= 3, str(wired_alpha))

# every image that exists on disk should have decoded
undecoded = [(m, i) for m, i in missing_images]
print("  images without data:", len(undecoded), undecoded[:4])

face = next((m for m in mats if m.name == "TENSION_FHEAD"), None)
if face:
    grp = next((n for n in face.node_tree.nodes
                if n.type == 'GROUP' and n.node_tree is ng), None)
    check("face uses DXT5nm group", grp is not None)
    if grp:
        check("group Alpha input linked", grp.inputs["Alpha"].is_linked)
        tex = grp.inputs["Alpha"].links[0].from_node
        check("normal image is CHANNEL_PACKED",
              tex.image.alpha_mode == 'CHANNEL_PACKED', tex.image.alpha_mode)
        check("normal image Non-Color",
              tex.image.colorspace_settings.name == 'Non-Color',
              tex.image.colorspace_settings.name)

hair = next((m for m in mats if m.name == "head_hair"), None)
if hair:
    bsdf = next(n for n in hair.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    check("hair alpha blended", bsdf.inputs["Alpha"].is_linked)
    print("  hair render method:",
          getattr(hair, "surface_render_method", "?"),
          getattr(hair, "blend_method", "?"))

# ------------------------------------------------------- recursive import
tree = "/tmp/tree/level1/level2"
os.makedirs(tree, exist_ok=True)
src_dir = os.path.dirname(EXAMPLE)
for name in ("alpha", "beta"):
    target = os.path.join(tree if name == "beta" else "/tmp/tree/level1", name)
    os.makedirs(target, exist_ok=True)
    shutil.copy(EXAMPLE, os.path.join(target, name + ".fbx"))
    shutil.copy(os.path.join(src_dir, "sna2_main0_def_rig.json"),
                os.path.join(target, name + "_rig.json"))
    texdir = os.path.join(target, name + "_textures")
    if not os.path.isdir(texdir):
        shutil.copytree(os.path.join(src_dir, "sna2_main0_def_textures"), texdir)

res = bpy.ops.foxbrowser.import_recursive('EXEC_DEFAULT', directory="/tmp/tree",
                                          merge_meshes='GROUP',
                                          flatten_hierarchy=True,
                                          material_prefix=True)
print("recursive result:", res)
check("recursive finished", 'FINISHED' in res)
check("alpha collection", bpy.data.collections.get("alpha") is not None)
check("beta collection", bpy.data.collections.get("beta") is not None)
ac = bpy.data.collections.get("alpha")
if ac:
    ms = [o for o in ac.objects if o.type == 'MESH']
    es = [o for o in ac.objects if o.type == 'EMPTY']
    print("  alpha: meshes=%d empties=%d" % (len(ms), len(es)))
    check("merged into groups", 5 <= len(ms) < 45,
          "%d: %s" % (len(ms), ", ".join(sorted(o.name for o in ms))))
    check("empties removed", len(es) == 0, str(len(es)))
    check("meshes parented to armature",
          all(o.parent is not None and o.parent.type == 'ARMATURE' for o in ms))
    pref = [o for o in ms if o.material_slots and o.material_slots[0].material]
    if pref:
        mn = pref[0].material_slots[0].material.name
        check("material prefixed", mn.startswith("alpha__"), mn)

log = bpy.data.texts.get("FoxBrowser Import Log")
print("\n----- RECURSIVE LOG (tail) -----")
print("\n".join((log.as_string() if log else "").splitlines()[-12:]))
print("--------------------------------\n")

# ------------------------------------------------------------- rewire op
for o in bpy.context.scene.objects:
    o.select_set(False)
if ac:
    for o in ac.objects:
        if o.type == 'MESH':
            o.select_set(True)
    bpy.context.view_layer.objects.active = next(
        o for o in ac.objects if o.type == 'MESH')
    r = bpy.ops.foxbrowser.rewire_materials('EXEC_DEFAULT')
    print("rewire result:", r)
    check("rewire finished", 'FINISHED' in r)

print("\n=========================")
print("FAILURES: %d" % len(failures))
for f in failures:
    print("  -", f)
print("=========================")
sys.exit(1 if failures else 0)
