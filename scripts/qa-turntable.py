# SPDX-License-Identifier: MIT
"""
Headless turntable QA render for FoxBrowser model exports.

Run under Blender's Python so material/rig/texture regressions are visible at a
glance without opening the UI:

    blender --background --factory-startup --python qa-turntable.py -- \
        <model.fbx | folder> <out_dir> [views]

For a single .fbx it renders <views> (default 6) angles around the model. For a
folder it renders the first angle of every model FBX it finds (one thumbnail
each), so a whole rip can be eyeballed in a contact sheet.
"""

import os
import sys
import math

import bpy


def argv_after_dashes():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT" if hasattr(
        bpy.types, "RenderEngineEeveeNext") else "BLENDER_EEVEE"
    scene.render.resolution_x = 900
    scene.render.resolution_y = 1200
    scene.render.film_transparent = False
    try:
        scene.view_settings.view_transform = "Standard"
    except Exception:
        pass


def world_grey():
    world = bpy.data.worlds.new("qa")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.05, 0.05, 0.06, 1)
        bg.inputs[1].default_value = 1.0
    bpy.context.scene.world = world


def bounds(objs):
    import mathutils
    mins = mathutils.Vector((1e9, 1e9, 1e9))
    maxs = mathutils.Vector((-1e9, -1e9, -1e9))
    found = False
    for o in objs:
        if o.type != "MESH":
            continue
        found = True
        for corner in o.bound_box:
            w = o.matrix_world @ mathutils.Vector(corner)
            mins = mathutils.Vector((min(mins[i], w[i]) for i in range(3)))
            maxs = mathutils.Vector((max(maxs[i], w[i]) for i in range(3)))
    if not found:
        return mathutils.Vector((0, 0, 0)), 1.0
    center = (mins + maxs) / 2
    radius = max((maxs - mins).length / 2, 0.1)
    return center, radius


def setup_lookdev(center, radius):
    import mathutils
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(55), 0, math.radians(35))
    bpy.context.collection.objects.link(sun)

    cam_data = bpy.data.cameras.new("cam")
    cam = bpy.data.objects.new("cam", cam_data)
    bpy.context.collection.objects.link(cam)
    bpy.context.scene.camera = cam
    return cam, center, radius


def point_camera(cam, center, radius, angle_deg):
    import mathutils
    a = math.radians(angle_deg)
    dist = radius * 3.2
    cam.location = center + mathutils.Vector(
        (math.sin(a) * dist, -math.cos(a) * dist, radius * 0.35))
    direction = center - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()


def import_fbx(path):
    before = set(bpy.data.objects)
    try:
        bpy.ops.import_scene.fbx(filepath=path)
    except Exception as exc:
        print("  ! import failed: %s" % exc)
        return []
    return [o for o in bpy.data.objects if o not in before]


def render_to(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def do_one(fbx, out_dir, views):
    reset()
    world_grey()
    objs = import_fbx(fbx)
    if not objs:
        return
    center, radius = bounds(objs)
    cam, center, radius = setup_lookdev(center, radius)
    stem = os.path.splitext(os.path.basename(fbx))[0]
    os.makedirs(out_dir, exist_ok=True)
    for i in range(views):
        point_camera(cam, center, radius, i * (360.0 / views))
        render_to(os.path.join(out_dir, "%s_%02d.png" % (stem, i)))
    print("  rendered %s (%d view(s))" % (stem, views))


def find_models(root):
    out = []
    for dp, _dn, fn in os.walk(root):
        for f in fn:
            if f.lower().endswith(".fbx") and not f[:-4].endswith(tuple(
                    "_%03d" % n for n in range(1000))):
                # skip anim packs; also require a model sidecar next to it
                stem = f[:-4]
                if (os.path.isfile(os.path.join(dp, stem + "_rig.json"))
                        or os.path.isdir(os.path.join(dp, stem + "_textures"))):
                    out.append(os.path.join(dp, f))
    return sorted(out)


def main():
    args = argv_after_dashes()
    if not args:
        print("usage: qa-turntable.py -- <model.fbx | folder> <out_dir> [views]")
        return
    target = args[0]
    out_dir = args[1] if len(args) > 1 else os.path.join(os.path.dirname(target), "_qa")
    views = int(args[2]) if len(args) > 2 else 6

    if os.path.isdir(target):
        models = find_models(target)
        print("contact sheet: %d model(s)" % len(models))
        for m in models:
            do_one(m, out_dir, 1)      # one thumbnail each
    else:
        do_one(target, out_dir, views)
    print("done -> %s" % out_dir)


if __name__ == "__main__":
    main()
