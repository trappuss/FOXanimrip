# SPDX-License-Identifier: GPL-3.0-or-later
"""The actual import pipeline: one :class:`~.discovery.ExportSet` at a time."""

from __future__ import annotations

import os
import re
import time

import bpy

from . import fbxpatch, materials, rig

#: ``MESH_head_12`` -> ``MESH_head``; also copes with Blender's ``.001`` tails.
_PART_SUFFIX = re.compile(r"(?:\.\d{3})?_\d+(?:\.\d{3})?$")


class Report:
    """Collects messages so a 400-model batch run ends with one summary."""

    def __init__(self):
        self.lines = []
        self.warnings = 0
        self.errors = 0
        self._context = ""

    def set_context(self, name):
        self._context = name

    def _add(self, level, message):
        prefix = "%s: " % self._context if self._context else ""
        self.lines.append((level, prefix + str(message)))

    def info(self, message):
        self._add('INFO', message)

    def warn(self, message):
        self.warnings += 1
        self._add('WARNING', message)

    def error(self, message):
        self.errors += 1
        self._add('ERROR', message)

    def as_text(self):
        return "\n".join("%-7s %s" % (lvl, msg) for lvl, msg in self.lines)


class ImportResult:
    __slots__ = ("name", "collection", "objects", "armature", "materials",
                 "error", "seconds")

    def __init__(self, name):
        self.name = name
        self.collection = None
        self.objects = []
        self.armature = None
        self.materials = 0
        self.error = ""
        self.seconds = 0.0

    @property
    def ok(self):
        return not self.error


# -- collection plumbing --------------------------------------------------

def _layer_collection_for(layer_collection, collection):
    if layer_collection.collection is collection:
        return layer_collection
    for child in layer_collection.children:
        found = _layer_collection_for(child, collection)
        if found is not None:
            return found
    return None


def ensure_parent_collection(context, name):
    """Fetch or create the shared parent collection, linked to the scene."""
    if not name:
        return context.scene.collection
    collection = bpy.data.collections.get(name)
    if collection is None:
        collection = bpy.data.collections.new(name)
    if _layer_collection_for(context.view_layer.layer_collection,
                             collection) is None:
        context.scene.collection.children.link(collection)
    return collection


def _make_collection(context, name, parent):
    collection = bpy.data.collections.new(name)
    parent.children.link(collection)
    return collection


# -- format dispatch ------------------------------------------------------

def _supported_kwargs(op, kwargs):
    """Drop keyword arguments this Blender's importer does not know about."""
    try:
        known = set(op.get_rna_type().properties.keys())
    except Exception:
        return kwargs
    return {k: v for k, v in kwargs.items() if k in known}


def _run_native_import(path, ext, opts, report, use_anim):
    if ext == ".fbx":
        op = bpy.ops.import_scene.fbx
        kwargs = dict(
            filepath=path,
            use_anim=use_anim,
            use_custom_normals=True,
            use_custom_props=True,
            use_image_search=False,
            global_scale=opts.global_scale,
            automatic_bone_orientation=opts.automatic_bone_orientation,
            ignore_leaf_bones=opts.ignore_leaf_bones,
            force_connect_children=False,
            use_prepost_rot=True,
            colors_type=opts.vertex_colors,
        )
    elif ext == ".dae":
        op = getattr(bpy.ops.wm, "collada_import", None)
        if op is None:
            raise RuntimeError("Collada import is not available in this build")
        kwargs = dict(filepath=path, import_units=False,
                      auto_connect=False, find_chains=False,
                      fix_orientation=False)
    elif ext == ".obj":
        op = getattr(bpy.ops.wm, "obj_import", None)
        if op is None:
            raise RuntimeError("OBJ import is not available in this build")
        kwargs = dict(filepath=path, global_scale=opts.global_scale,
                      forward_axis='NEGATIVE_Z', up_axis='Y')
    else:
        raise RuntimeError("unsupported model format '%s'" % ext)

    result = op(**_supported_kwargs(op, kwargs))
    if 'FINISHED' not in result:
        raise RuntimeError("the %s importer reported %s"
                           % (ext.lstrip("."), ", ".join(result)))


def _purge(objects):
    for obj in objects:
        try:
            bpy.data.objects.remove(obj, do_unlink=True)
        except Exception:
            pass


def _import_model(export_set, opts, report, snapshot):
    """Run the native importer, repairing and retrying as needed.

    Returns True when animation actually came through.
    """
    ext = export_set.extension
    path = export_set.model_path
    temp_path = ""
    want_anim = bool(opts.import_animation)

    if want_anim and ext == ".fbx" and opts.repair_animation:
        try:
            if fbxpatch.needs_fix(path):
                temp_path, fixes = fbxpatch.write_fixed(path)
                if temp_path:
                    report.info("repaired %d non-standard animation class "
                                "token(s) in a temporary copy; Blender's FBX "
                                "importer cannot read the original" % fixes)
        except Exception as exc:
            report.warn("could not repair the animation block (%s); "
                        "importing without animation" % exc)
            temp_path = ""
            want_anim = False

    source = temp_path or path
    try:
        try:
            _run_native_import(source, ext, opts, report, want_anim)
            return want_anim
        except Exception as first:
            if not want_anim:
                raise
            _purge([o for o in bpy.data.objects if o not in snapshot])
            report.warn("import failed with animation enabled (%s); "
                        "retrying without it" % first)
            _run_native_import(path, ext, opts, report, False)
            return False
    finally:
        if temp_path:
            try:
                os.remove(temp_path)
            except OSError:
                pass


# -- post processing ------------------------------------------------------

def _relink(objects, collection):
    for obj in objects:
        for existing in list(obj.users_collection):
            if existing is not collection:
                existing.objects.unlink(obj)
        if obj.name not in collection.objects:
            collection.objects.link(obj)


def _rename_action(armature_obj, model_name, clip_name, fake_user):
    anim = armature_obj.animation_data
    if anim is None or anim.action is None:
        return None
    action = anim.action
    action.name = "%s|%s" % (model_name, clip_name or "take")
    action.use_fake_user = bool(fake_user)
    return action


def _apply_scene_timing(context, data, action):
    timing = rig.clip_fps(data)
    if timing is not None:
        fps, base = timing
        context.scene.render.fps = fps
        context.scene.render.fps_base = base
    if action is not None:
        start, end = action.frame_range
        context.scene.frame_start = int(round(start))
        context.scene.frame_end = max(int(round(end)), int(round(start)))


def _join(context, target, others):
    if not others:
        return target
    for obj in list(context.selected_objects):
        obj.select_set(False)
    target.select_set(True)
    for obj in others:
        obj.select_set(True)
    with context.temp_override(active_object=target,
                               selected_editable_objects=[target] + others):
        bpy.ops.object.join()
    for obj in list(context.selected_objects):
        obj.select_set(False)
    return target


def _merge_meshes(context, objects, mode, model_name, report):
    meshes = [o for o in objects if o.type == 'MESH']
    if len(meshes) < 2 or mode == 'NONE':
        return objects

    groups = {}
    if mode == 'ALL':
        groups[model_name] = meshes
    else:
        # Blender's FBX importer reparents skinned meshes to the armature and
        # throws away FoxBrowser's MESH_* null hierarchy, so the group has to
        # come from the object name: MESH_head_0 ... MESH_head_23 -> MESH_head.
        for obj in meshes:
            parent = obj.parent
            if parent is not None and parent.type == 'EMPTY':
                key = parent.name
            else:
                key = _PART_SUFFIX.sub("", obj.name)
            groups.setdefault(key, []).append(obj)

    survivors = set(objects)
    for name, members in groups.items():
        members.sort(key=lambda o: o.name)
        if len(members) < 2:
            members[0].name = name          # drop the part index anyway
            continue
        try:
            kept = _join(context, members[0], members[1:])
        except Exception as exc:
            report.warn("could not merge %s (%s)" % (name, exc))
            continue
        kept.name = name
        for gone in members[1:]:
            survivors.discard(gone)
    return [o for o in objects if o in survivors]


def _flatten_hierarchy(objects, armature_obj, report):
    """Re-parent meshes straight to the armature and drop the group empties."""
    if armature_obj is None:
        report.info("no armature found, keeping the empty hierarchy")
        return objects

    empties = [o for o in objects if o.type == 'EMPTY']
    if not empties:
        return objects

    inverse = armature_obj.matrix_world.inverted()
    for obj in objects:
        if obj.type != 'MESH':
            continue
        world = obj.matrix_world.copy()
        obj.parent = armature_obj
        obj.matrix_parent_inverse = inverse
        obj.matrix_world = world

    remaining = [o for o in objects if o not in set(empties)]
    for empty in empties:
        try:
            bpy.data.objects.remove(empty, do_unlink=True)
        except Exception:
            remaining.append(empty)
    return remaining


# -- entry point ----------------------------------------------------------

def import_set(context, export_set, opts, report, parent_collection=None):
    """Import one export set.  Never raises; failures land in the result."""
    started = time.perf_counter()
    result = ImportResult(export_set.name)
    report.set_context(export_set.name)

    view_layer = context.view_layer
    previous_active = view_layer.active_layer_collection
    parent = parent_collection or context.scene.collection
    collection = _make_collection(context, export_set.name, parent)
    result.collection = collection

    before = set(bpy.data.objects)
    have_anim = False
    try:
        layer = _layer_collection_for(view_layer.layer_collection, collection)
        if layer is not None:
            view_layer.active_layer_collection = layer
        have_anim = _import_model(export_set, opts, report, before)
    except Exception as exc:
        result.error = str(exc)
        report.error(str(exc))
        _purge([o for o in bpy.data.objects if o not in before])
        view_layer.active_layer_collection = previous_active
        if not collection.objects and not collection.children:
            bpy.data.collections.remove(collection)
            result.collection = None
        result.seconds = time.perf_counter() - started
        return result
    finally:
        view_layer.active_layer_collection = previous_active

    created = [o for o in bpy.data.objects if o not in before]
    _relink(created, collection)
    result.objects = created

    armature_obj = next((o for o in created if o.type == 'ARMATURE'), None)
    result.armature = armature_obj

    # -- rig JSON
    rig_data = rig.load(export_set.rig_json) if opts.apply_rig_json else None
    if rig_data is not None and armature_obj is not None:
        stats = rig.apply(armature_obj, rig_data, opts, report)
        report.info("rig: %d/%d bones annotated, %d rig units"
                    % (stats["matched"], rig_data.get("bones", 0), stats["units"]))
    elif opts.apply_rig_json and export_set.rig_json and armature_obj is None:
        report.info("rig JSON found but the import produced no armature")

    if armature_obj is not None:
        armature_obj.show_in_front = opts.armature_in_front
        armature_obj["fox_source"] = os.path.basename(export_set.model_path)
        # Blender names every FBX armature "Armature"; a bulk run would end up
        # with Armature.001 ... Armature.400 and no way to tell them apart.
        armature_obj.name = "%s_Armature" % export_set.name
        armature_obj.data.name = "%s_Armature" % export_set.name

    # -- animation
    action = None
    if have_anim and armature_obj is not None:
        clip_name = ""
        if rig_data:
            clip_name = (rig_data.get("clip") or {}).get("name", "")
        action = _rename_action(armature_obj, export_set.name, clip_name,
                                opts.action_fake_user)
        if action is None:
            report.info("no animation was present in the file")
    if opts.set_scene_fps and (action is not None or rig_data):
        _apply_scene_timing(context, rig_data, action)

    # -- geometry tidy-up
    if opts.merge_meshes != 'NONE':
        result.objects = _merge_meshes(context, result.objects,
                                       opts.merge_meshes, export_set.name, report)
    if opts.flatten_hierarchy:
        result.objects = _flatten_hierarchy(result.objects, armature_obj, report)

    # -- materials
    if opts.rebuild_materials:
        if export_set.textures_dir:
            result.materials = materials.rebuild(export_set, result.objects,
                                                 opts, report)
        else:
            report.warn("no %s_textures folder next to the model, "
                        "materials left as imported" % export_set.name)

    result.seconds = time.perf_counter() - started
    report.set_context("")
    return result
