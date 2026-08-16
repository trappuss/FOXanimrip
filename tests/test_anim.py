"""Bulk animation import test against real foxanimrip output."""
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
MODEL = os.environ["FOXB_MODEL"]
CLIPS = os.environ["FOXB_CLIPS"]


addons = bpy.utils.user_resource('SCRIPTS', path="addons", create=True)
dst = os.path.join(addons, "io_foxbrowser")
shutil.rmtree(dst, ignore_errors=True)
shutil.copytree(SRC, dst, ignore=shutil.ignore_patterns("__pycache__"))
if addons not in sys.path:
    sys.path.append(addons)
bpy.ops.preferences.addon_enable(module="io_foxbrowser")

for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj, do_unlink=True)

failures = []


def check(label, cond, detail=""):
    print(("  PASS  " if cond else "  FAIL  ") + label
          + (" :: %s" % detail if detail else ""))
    if not cond:
        failures.append(label)


# clip folder needs a subfolder to exercise the recursive walk
work = "/tmp/cliplib/SoldierGz_layers"
os.makedirs(work, exist_ok=True)
for name in os.listdir(CLIPS):
    if name.endswith(".fbx"):
        shutil.copy(os.path.join(CLIPS, name), os.path.join(work, name))
print("clips:", sorted(os.listdir(work)))

print("\n== import the model ==")
bpy.ops.foxbrowser.import_files(
    'EXEC_DEFAULT', filepath=MODEL, directory=os.path.dirname(MODEL),
    files=[{"name": os.path.basename(MODEL)}], import_animation=False)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
print("  armature:", arm.name, len(arm.data.bones), "bones")
check("no action yet", arm.animation_data is None or arm.animation_data.action is None)

print("\n== bulk import animations ==")
for o in bpy.context.scene.objects:
    o.select_set(False)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm

before = set(bpy.data.actions)
res = bpy.ops.foxbrowser.import_animations('EXEC_DEFAULT', directory="/tmp/cliplib")
print("  result:", res)
_log = bpy.data.texts.get("FoxBrowser Import Log")
print("  --- log ---\n" + (_log.as_string() if _log else "(none)") + "\n  -----------")
check("operator finished", 'FINISHED' in res)

new = [a for a in bpy.data.actions if a not in before]
print("  actions:", sorted(a.name for a in new))
check("three actions created", len(new) == 3, str(len(new)))
check("named after the clips",
      {a.name for a in new} == {"enemasr_s_slp_u_ptw", "enemasr_s_run_f_r_ed_bl", "enemshg_s_rdy_idl_el_aim_dsh_l"},
      str(sorted(a.name for a in new)))
check("fake users set", all(a.use_fake_user for a in new))

long_action = max(new, key=lambda a: a.frame_range[1])
print("  %s frame range %s, %d fcurves"
      % (long_action.name, tuple(long_action.frame_range), len(long_action.fcurves)))
check("92-frame clip present", int(round(long_action.frame_range[1])) >= 90,
      str(long_action.frame_range))

bones = set()
for fc in long_action.fcurves:
    if fc.data_path.startswith('pose.bones["'):
        bones.add(fc.data_path[12:fc.data_path.find('"]', 12)])
model_bones = {b.name for b in arm.data.bones}
print("  keyed bones: %d, all present on the armature: %s"
      % (len(bones), bones <= model_bones))
check("fcurves target real bones", bones and bones <= model_bones,
      str(sorted(bones - model_bones)[:4]))
check("many bones keyed", len(bones) >= 40, str(len(bones)))

print("\n== the action actually drives the rig ==")
arm.animation_data.action = long_action
scene = bpy.context.scene
scene.frame_set(1)
a = arm.pose.bones["SKL_004_HEAD"].matrix_basis.copy()
loc_a = arm.pose.bones["SKL_000_WAIST"].location.copy()
scene.frame_set(60)
b = arm.pose.bones["SKL_004_HEAD"].matrix_basis.copy()
loc_b = arm.pose.bones["SKL_000_WAIST"].location.copy()
moved = any(abs(x - y) > 1e-5 for ra, rb in zip(a, b) for x, y in zip(ra, rb))
check("head bone pose changes over the clip", moved)
print("  waist local translation f1=%s f60=%s"
      % (tuple(round(v, 3) for v in loc_a), tuple(round(v, 3) for v in loc_b)))

print("\n== no leftovers from the temporary imports ==")
arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
check("only the original armature remains", len(arms) == 1, str(len(arms)))
empties = [o for o in bpy.data.objects if o.type == 'EMPTY']
print("  objects in file: %d (armature + %d meshes + %d empties)"
      % (len(bpy.data.objects),
         len([o for o in bpy.data.objects if o.type == 'MESH']), len(empties)))
check("no orphan collections",
      not any(c.name.startswith("FoxBrowser Clip Temp") for c in bpy.data.collections),
      str([c.name for c in bpy.data.collections]))

print("\n== filters ==")
before = set(bpy.data.actions)
res = bpy.ops.foxbrowser.import_animations(
    'EXEC_DEFAULT', directory="/tmp/cliplib", name_filter="run_f_r",
    action_prefix="gz_")
new2 = [a for a in bpy.data.actions if a not in before]
check("name filter honoured", len(new2) == 1, str([a.name for a in new2]))
check("prefix applied", new2 and new2[0].name.startswith("gz_"),
      new2[0].name if new2 else "-")

print("\n== NLA stash ==")
before = set(bpy.data.actions)
res = bpy.ops.foxbrowser.import_animations(
    'EXEC_DEFAULT', directory="/tmp/cliplib", push_to_nla=True,
    action_prefix="nla_")
tracks = [t.name for t in arm.animation_data.nla_tracks]
check("nla tracks created", len(tracks) >= 2, str(tracks))
check("tracks muted", all(t.mute for t in arm.animation_data.nla_tracks))

log = bpy.data.texts.get("FoxBrowser Import Log")
print("\n----- LOG -----")
print(log.as_string() if log else "(none)")
print("---------------")




print("\n== per-character folder scoping and index.tsv filters ==")
# lay out a multi-character export: <out>/<character>/<set>/<clip>.fbx
multi = "/tmp/cliplib_multi"
shutil.rmtree(multi, ignore_errors=True)
mine = os.path.join(multi, arm.get("fox_model"), "SoldierGz_layers")
other = os.path.join(multi, "some_other_char", "OtherSet")
os.makedirs(mine, exist_ok=True)
os.makedirs(other, exist_ok=True)
src_clips = sorted(f for f in os.listdir(work) if f.endswith(".fbx"))
for name in src_clips:
    shutil.copy(os.path.join(work, name), os.path.join(mine, name))
shutil.copy(os.path.join(work, src_clips[0]),
            os.path.join(other, "decoy_should_not_import.fbx"))
with open(os.path.join(multi, arm.get("fox_model"), "index.tsv"), "w") as fh:
    fh.write("mtar\tgani\tframes\tfps\tmatchedBones\tfile\n")
    for i, name in enumerate(src_clips):
        stem = os.path.splitext(name)[0]
        fh.write("SoldierGz_layers\t%s\t%d\t59.94\t53\tSoldierGz_layers/%s\n"
                 % (stem, 5 if i == 0 else 200, name))

before = set(bpy.data.actions)
res = bpy.ops.foxbrowser.import_animations(
    'EXEC_DEFAULT', directory=multi, action_prefix="scoped_")
new3 = [a for a in bpy.data.actions if a not in before]
names3 = sorted(a.name for a in new3)
check("scoped to this armature's folder",
      all("decoy" not in n for n in names3), str(names3))
check("imported this character's clips", len(new3) == len(src_clips), str(len(new3)))

before = set(bpy.data.actions)
res = bpy.ops.foxbrowser.import_animations(
    'EXEC_DEFAULT', directory=multi, action_prefix="long_", min_frames=100)
new4 = [a for a in bpy.data.actions if a not in before]
check("min-frames filter drops the short clip",
      len(new4) == len(src_clips) - 1, "%d of %d" % (len(new4), len(src_clips)))

before = set(bpy.data.actions)
res = bpy.ops.foxbrowser.import_animations(
    'EXEC_DEFAULT', directory=multi, action_prefix="wide_", min_bones=99)
new5 = [a for a in bpy.data.actions if a not in before]
check("min-bones filter drops everything at 99", len(new5) == 0, str(len(new5)))
log = bpy.data.texts["FoxBrowser Import Log"].as_string()
check("filtering explained in the log", "filtered out" in log)

print("\n== unscoped import still sees every character ==")
before = set(bpy.data.actions)
res = bpy.ops.foxbrowser.import_animations(
    'EXEC_DEFAULT', directory=multi, action_prefix="unscoped_", auto_scope=False)
new6 = [a for a in bpy.data.actions if a not in before]
check("without scoping the decoy comes in too",
      any("decoy" in a.name for a in new6), str(sorted(a.name for a in new6)))

print("\n=========================")
print("FAILURES (round 2): %d" % len(failures))
for f in failures:
    print("  -", f)
print("=========================")
sys.exit(1 if failures else 0)
