"""Action browser + multi-take import test."""
import os, shutil, sys
import bpy

_here = os.path.dirname(os.path.abspath(__file__))
SRC = os.environ.get("FOXB_ADDON",
                     os.path.join(os.path.dirname(_here), "blender", "io_foxbrowser"))
addons = bpy.utils.user_resource('SCRIPTS', path="addons", create=True)
shutil.rmtree(os.path.join(addons, "io_foxbrowser"), ignore_errors=True)
shutil.copytree(SRC, os.path.join(addons, "io_foxbrowser"),
                ignore=shutil.ignore_patterns("__pycache__"))
sys.path.append(addons)
bpy.ops.preferences.addon_enable(module="io_foxbrowser")

failures = []
def check(label, cond, detail=""):
    print(("  PASS  " if cond else "  FAIL  ") + label + (" :: %s" % detail if detail else ""))
    if not cond: failures.append(label)

print("== registration ==")
sc = bpy.context.scene
check("scene props registered", all(hasattr(sc, n) for n in
      ("foxb_action_search", "foxb_action_index", "foxb_action_mtar",
       "foxb_action_min_frames", "foxb_action_only_clips")))
check("panel registered", hasattr(bpy.types, "FOXB_PT_actions"))
check("uilist registered", hasattr(bpy.types, "FOXB_UL_actions"))
for op in ("action_assign", "action_stash", "action_remove", "action_purge"):
    check("operator %s" % op, hasattr(bpy.ops.foxbrowser, op))

print("\n== import a packed multi-take file ==")
MODEL = os.environ["FOXB_MODEL"]
PACK = os.environ["FOXB_PACK"]
for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.foxbrowser.import_files('EXEC_DEFAULT', filepath=MODEL,
    directory=os.path.dirname(MODEL), files=[{"name": os.path.basename(MODEL)}],
    import_animation=False)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
for o in bpy.context.scene.objects: o.select_set(False)
arm.select_set(True); bpy.context.view_layer.objects.active = arm

before = set(bpy.data.actions)
res = bpy.ops.foxbrowser.import_animations('EXEC_DEFAULT', directory=PACK)
new = [a for a in bpy.data.actions if a not in before]
print("  result:", res, "actions:", len(new))
print("  names:", sorted(a.name for a in new)[:6])
check("multi-take file yields several actions", len(new) >= 3, str(len(new)))
check("actions named after their takes",
      not any(a.name.startswith("Armature|") for a in new),
      str([a.name for a in new][:3]))
check("actions tagged with clip metadata",
      all("fox_clip" in a for a in new))
tagged = [a for a in new if "fox_frames" in a]
check("frame counts came from index.tsv", len(tagged) == len(new),
      "%d of %d" % (len(tagged), len(new)))

print("\n== the browser filters ==")
import io_foxbrowser.actions as A
check("clip_actions finds them", len(A.clip_actions()) >= len(new))
sc = bpy.context.scene
sc.foxb_action_search = new[0].name[:6]
shown = [a for a in A.clip_actions()
         if A._matches(a, sc.foxb_action_search, 'ALL', 0)]
check("search narrows the list", 0 < len(shown) <= len(new), str(len(shown)))
sc.foxb_action_search = ""
sc.foxb_action_min_frames = 10**6
none_shown = [a for a in A.clip_actions() if A._matches(a, "", 'ALL', 10**6)]
check("min-frames filter can empty the list", len(none_shown) == 0)
sc.foxb_action_min_frames = 0
items = A.mtar_items(None, bpy.context)
check("set dropdown lists the mtar", any(i[0] == "SoldierGz_layers" for i in items),
      str([i[0] for i in items]))

print("\n== assign / stash / delete ==")
sc.foxb_action_index = list(bpy.data.actions).index(new[0])
r = bpy.ops.foxbrowser.action_assign('EXEC_DEFAULT')
check("assign works", 'FINISHED' in r, str(r))
check("action is on the armature", arm.animation_data.action == new[0])
check("frame range followed", bpy.context.scene.frame_end >= 1)
r = bpy.ops.foxbrowser.action_stash('EXEC_DEFAULT')
check("stash works", 'FINISHED' in r and len(arm.animation_data.nla_tracks) >= 1)
n_before = len(bpy.data.actions)
r = bpy.ops.foxbrowser.action_remove('EXEC_DEFAULT')
check("delete works", 'FINISHED' in r and len(bpy.data.actions) == n_before - 1)

print("\n=========================")
print("FAILURES: %d" % len(failures))
for f in failures: print("  -", f)
print("=========================")
sys.exit(1 if failures else 0)
