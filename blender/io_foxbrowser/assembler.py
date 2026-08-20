# SPDX-License-Identifier: GPL-3.0-or-later
"""
Assemble a whole character from its parts, the way the game does.

Fox Engine does not store a finished character as one file. A created soldier --
MGO's avatar, Survive's created survivor -- is a minimal **base body** plus a
set of interchangeable part models (head, arms, legs, chest, armour, hats),
every one of them rigged to the *same* player skeleton with the same ``SKL_*``
bone names. The game loads the base and stacks the chosen parts onto that one
skeleton. This does the same: import the base, import each part, and move every
part's meshes onto the base's armature so the result is one character on one rig.

Because the parts share a skeleton, a mesh weighted to ``SKL_002_CHEST`` already
names a bone the base armature has, so re-pointing its armature modifier is
enough for it to deform. The only real work is carrying over any bone a part has
that the base lacks -- a head's face bones, say -- which :func:`_merge_bones`
copies across with their rest pose and parenting intact.

What this deliberately does not do: hide the base-body mesh where gear covers it.
In game a form variation (``.fv2``) hides those groups to stop clipping; the rip
names mesh groups by hash, so matching them is unreliable. Everything is brought
in visible instead, and overlap is left for you to hide by eye. Ground Zeroes
characters are usually a single complete model, so there is nothing to assemble
-- point this at one and it simply imports it.
"""

import os

import bpy
from mathutils import Matrix


class AssemblyResult:
    def __init__(self):
        self.armature = None
        self.meshes = []
        self.parts_loaded = 0
        self.bones_merged = 0
        self.warnings = []


def _new_objects(before):
    return [o for o in bpy.data.objects if o not in before]


def _import_fbx(path, report):
    """Native FBX import; returns the objects it created. Kept deliberately
    simple so the assembler works with or without the add-on's fuller pipeline."""
    before = set(bpy.data.objects)
    try:
        bpy.ops.import_scene.fbx(filepath=path)
    except Exception as exc:                       # noqa: BLE001
        report("! could not import %s (%s)" % (os.path.basename(path), exc))
        return []
    return _new_objects(before)


def _armature_of(objects):
    return next((o for o in objects if o.type == 'ARMATURE'), None)


def _meshes_of(objects):
    return [o for o in objects if o.type == 'MESH']


def _unhide(obj):
    obj.hide_viewport = False
    obj.hide_render = False
    try:
        obj.hide_set(False)
    except RuntimeError:
        pass


def _merge_bones(master, donor, report):
    """Copy every bone the donor armature has that master lacks.

    Same skeleton, so shared bones already line up; only genuinely new bones --
    a part's own help bones -- need carrying over. Parents are resolved in
    passes so a new bone whose parent is also new still lands correctly.
    """
    master_names = {b.name for b in master.data.bones}
    donor_bones = {b.name: b for b in donor.data.bones}
    missing = [n for n in donor_bones if n not in master_names]
    if not missing:
        return 0

    # donor rest transforms, in donor-object space
    rest = {n: donor.matrix_world @ donor_bones[n].matrix_local for n in donor_bones}
    to_master = master.matrix_world.inverted()

    ctx = bpy.context
    prev_active = ctx.view_layer.objects.active
    ctx.view_layer.objects.active = master
    bpy.ops.object.mode_set(mode='EDIT')
    eb = master.data.edit_bones

    added = 0
    remaining = list(missing)
    guard = 0
    while remaining and guard < len(missing) + 2:
        guard += 1
        progressed = False
        for name in list(remaining):
            db = donor_bones[name]
            parent = db.parent
            if parent is not None and parent.name not in eb and parent.name in donor_bones:
                # parent is itself a new bone not added yet -- wait for it
                if parent.name in remaining:
                    continue
            bone = eb.new(name)
            m = to_master @ rest[name]          # donor bone rest, in master space
            # head at the matrix origin; tail along the donor bone's own vector,
            # rotated into master space so the new bone keeps its orientation.
            vec = m.to_3x3() @ (db.tail_local - db.head_local)
            if vec.length < 1e-5:
                vec = m.to_3x3() @ Matrix.Identity(3).col[1] * max(db.length, 1e-3)
            bone.head = m.translation
            bone.tail = m.translation + vec
            if parent is not None and parent.name in eb:
                bone.parent = eb[parent.name]
            added += 1
            remaining.remove(name)
            progressed = True
        if not progressed:
            break

    bpy.ops.object.mode_set(mode='OBJECT')
    ctx.view_layer.objects.active = prev_active
    if remaining:
        report("  (%d part bone(s) could not be placed: %s)"
               % (len(remaining), ", ".join(remaining[:4])))
    return added


def _adopt_meshes(master, meshes, report):
    """Re-parent meshes onto the master armature and point their armature
    modifier at it. Vertex groups keep their bone names, which master shares."""
    for m in meshes:
        _unhide(m)
        mw = m.matrix_world.copy()
        m.parent = master
        m.matrix_world = mw
        armed = False
        for mod in m.modifiers:
            if mod.type == 'ARMATURE':
                mod.object = master
                armed = True
        if not armed:
            mod = m.modifiers.new("Armature", 'ARMATURE')
            mod.object = master


def _purge(objects):
    for o in objects:
        try:
            bpy.data.objects.remove(o, do_unlink=True)
        except Exception:                          # noqa: BLE001
            pass


def assemble(base_path, part_paths, report=None, link_collection=None, settings=None):
    """Build one character from a base body and a list of part models.

    base_path may be None -- then the first part becomes the master. Returns an
    :class:`AssemblyResult`.
    """
    report = report or (lambda _m: None)
    result = AssemblyResult()

    order = ([base_path] if base_path else []) + list(part_paths)
    if not order:
        report("! nothing to assemble")
        return result

    # the base is the first import; its armature becomes the master
    base_objs = _import_fbx(order[0], report)
    master = _armature_of(base_objs)
    if master is None:
        report("! the base has no armature -- nothing to build onto")
        return result
    base_meshes = _meshes_of(base_objs)
    for m in base_meshes:
        _unhide(m)
    _rebuild_part_materials(order[0], base_meshes, settings, report)
    result.armature = master
    result.meshes.extend(base_meshes)
    result.parts_loaded = 1
    report("base: %s (%d mesh%s, %d bones)"
           % (os.path.basename(order[0]), len(base_meshes),
              "" if len(base_meshes) == 1 else "es", len(master.data.bones)))

    # the rest are parts, merged onto the master
    if len(order) > 1:
        added = add_parts(master, order[1:], report=report, settings=settings)
        result.meshes.extend(added.meshes)
        result.parts_loaded += added.parts_loaded
        result.bones_merged += added.bones_merged

    if link_collection is not None:
        _relink_collection([master] + result.meshes, link_collection)

    report("assembled %d part(s): %d mesh(es) on 1 armature, %d bone(s) merged"
           % (result.parts_loaded, len(result.meshes), result.bones_merged))
    return result


def add_parts(master, part_paths, report=None, link_collection=None, settings=None):
    """Stack more parts onto an existing character rig.

    This is the incremental half of the workflow: import a base body normally,
    make its armature active, then feed part files here and each one's meshes are
    moved onto that armature. It can be called again and again to build a
    character up piece by piece, and the parts can live in different folders --
    unlike a single multi-select, which needs everything in one place.

    When *settings* is given, each part's materials are rebuilt with the full
    FoxBrowser treatment (DXT5nm normals, the srm split, the role sidecar) so an
    added part looks exactly like the same model imported on its own -- the plain
    FBX import used to leave parts with flat, srm-less materials.
    """
    report = report or (lambda _m: None)
    result = AssemblyResult()
    result.armature = master
    if master is None or master.type != 'ARMATURE':
        report("! no character rig is active -- import a base body and select "
               "its armature first")
        return result

    # Make the base body itself visible too. If it was imported outside the
    # add-on (plain FBX import) its default-hidden groups are still off, so the
    # assembled character would show parts but not the base skin underneath.
    for child in master.children:
        if child.type == 'MESH' and child.hide_viewport:
            _unhide(child)

    for path in part_paths:
        objs = _import_fbx(path, report)
        if not objs:
            continue
        arm = _armature_of(objs)
        meshes = _meshes_of(objs)
        for m in meshes:
            _unhide(m)
        if arm is not None:
            result.bones_merged += _merge_bones(master, arm, report)
        _adopt_meshes(master, meshes, report)
        _rebuild_part_materials(path, meshes, settings, report)
        result.meshes.extend(meshes)
        result.parts_loaded += 1
        report("part: %s (%d mesh%s)" % (os.path.basename(path), len(meshes),
                                         "" if len(meshes) == 1 else "es"))
        _purge([o for o in objs if o.type in {'ARMATURE', 'EMPTY'}])

    if link_collection is not None:
        _relink_collection(result.meshes, link_collection)
    return result


def _rebuild_part_materials(path, meshes, settings, report):
    """Give an added part the full material treatment its standalone import
    would get. No-op without settings (plain geometry merge)."""
    if settings is None or not meshes:
        return
    try:
        from . import discovery, importer, materials
        if not getattr(settings, "rebuild_materials", True):
            return
        export_set = discovery.ExportSet(path)
        rep = importer.Report()
        materials.rebuild(export_set, meshes, settings, rep)
    except Exception as exc:                            # never break the merge
        report("  (materials left as imported: %s)" % exc)


def _relink_collection(objects, collection):
    for o in objects:
        for c in list(o.users_collection):
            if c is not collection:
                c.objects.unlink(o)
        if o.name not in collection.objects:
            collection.objects.link(o)
