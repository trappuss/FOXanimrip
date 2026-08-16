# SPDX-License-Identifier: GPL-3.0-or-later
"""
Bulk animation import: a folder of clip files -> one Action per clip.

``foxanimrip`` writes one skeleton-only FBX per gani. Each of those carries the
same armature as the model, so its action's F-curves already address
``pose.bones["SKL_..."]`` by the right names. Importing a clip therefore means:
bring the file in, steal the action, throw the temporary armature away.

The one thing that has to match is bone orientation. An action stores *local*
bone transforms, so if the model was imported with automatic bone orientation
off and the clips with it on, every rotation lands in a different local frame
and the result is scrambled. The importer defaults to the same setting the
model importer uses and says so in the UI.
"""

from __future__ import annotations

import os

import bpy

from . import discovery, importer

CLIP_EXTENSIONS = (".fbx",)

INDEX_NAME = "index.tsv"


def model_name_of(armature_obj):
    """The Fox Engine model an armature came from.

    Set by the model importer as a custom property; falls back to the object
    name with the ``_Armature`` suffix the importer adds stripped off.
    """
    stored = armature_obj.get("fox_model")
    if stored:
        return str(stored)
    name = armature_obj.name
    for suffix in ("_Armature", "_armature"):
        if name.endswith(suffix):
            return name[: -len(suffix)]
    return name


def scope_for(directory, armature_obj):
    """Narrow a multi-character export folder to this armature's own clips.

    ``foxanimrip`` writes ``<out>/<set>/<clip>.fbx`` for one character and
    ``<out>/<character>/<set>/<clip>.fbx`` for several. Pointing the importer at
    the top of a multi-character export would otherwise load every character's
    clips onto whichever armature happens to be selected.

    Returns ``(folder, matched_name)``.
    """
    if armature_obj is None or not os.path.isdir(directory):
        return directory, ""
    wanted = model_name_of(armature_obj)
    if not wanted:
        return directory, ""

    try:
        entries = [e for e in os.listdir(directory)
                   if os.path.isdir(os.path.join(directory, e))]
    except OSError:
        return directory, ""

    for entry in entries:
        if entry.lower() == wanted.lower():
            return os.path.join(directory, entry), entry
    return directory, ""


def read_index(directory):
    """Merge every ``index.tsv`` under *directory* into ``{clip: info}``.

    The exporter writes one per run: mtar, clip, frames, fps, matched bones,
    relative path. It costs nothing to read and lets the importer filter by
    length or rig coverage before doing any of the expensive work.
    """
    info = {}
    if not os.path.isdir(directory):
        return info
    for root, dirs, files in os.walk(directory):
        dirs[:] = [d for d in dirs if not d.startswith(".")]
        if INDEX_NAME not in files:
            continue
        try:
            with open(os.path.join(root, INDEX_NAME), "r", encoding="utf-8-sig") as fh:
                for line_no, line in enumerate(fh):
                    if line_no == 0 or not line.strip():
                        continue
                    parts = line.rstrip("\n").split("\t")
                    if len(parts) < 5:
                        continue
                    try:
                        info[parts[1]] = {
                            "mtar": parts[0],
                            "frames": int(parts[2]),
                            "fps": float(parts[3]),
                            "bones": int(parts[4]),
                        }
                    except ValueError:
                        continue
        except OSError:
            continue
    return info


def find_clips(directory, recursive=True, extensions=CLIP_EXTENSIONS):
    """Every clip file under *directory*, sorted, sidecar folders skipped."""
    directory = os.path.normpath(directory)
    found = []
    if not os.path.isdir(directory):
        return found

    if not recursive:
        for entry in sorted(os.listdir(directory)):
            path = os.path.join(directory, entry)
            if os.path.isfile(path) and os.path.splitext(entry)[1].lower() in extensions:
                found.append(path)
        return found

    for root, dirs, files in os.walk(directory):
        dirs[:] = sorted(d for d in dirs
                         if not d.startswith(".")
                         and not d.lower().endswith(discovery.SOURCE_DIR_SUFFIX)
                         and not d.lower().endswith(discovery.TEXTURE_DIR_SUFFIX))
        for entry in sorted(files):
            if os.path.splitext(entry)[1].lower() in extensions:
                found.append(os.path.join(root, entry))
    return found


def _fcurve_bones(action):
    names = set()
    for curve in action.fcurves:
        path = curve.data_path
        if path.startswith('pose.bones["'):
            end = path.find('"]', 12)
            if end > 0:
                names.add(path[12:end])
    return names


def take_name_of(action, fallback):
    """The take an Action came from.

    Blender names an FBX action ``<object>|<stack>``. foxanimrip names every
    stack after its clip, so for a packed multi-take file the tail is the clip
    name; for a single-clip file it is just "take" and the file name is better.
    """
    parts = [p.strip() for p in action.name.split("|")]
    # parts[0] is the object; parts[1] is the stack, which is the clip.
    for part in parts[1:]:
        if part and part.lower() not in ("take", "baselayer"):
            return part
    return fallback


def _import_one(context, path, opts, report):
    """Import one clip file and return its Actions (a file may hold several)."""
    before_objects = set(bpy.data.objects)
    before_actions = set(bpy.data.actions)

    temp = bpy.data.collections.new("FoxBrowser Clip Temp")
    context.scene.collection.children.link(temp)
    previous = context.view_layer.active_layer_collection
    layer = importer._layer_collection_for(context.view_layer.layer_collection, temp)
    if layer is not None:
        context.view_layer.active_layer_collection = layer

    try:
        set_stub = _ClipOptions(opts)
        export_set = discovery.ExportSet(path)
        importer._import_model(export_set, set_stub, report, before_objects)
    except Exception as exc:
        report.warn("%s: %s" % (os.path.basename(path), exc))
        return []
    finally:
        context.view_layer.active_layer_collection = previous
        for obj in [o for o in bpy.data.objects if o not in before_objects]:
            try:
                bpy.data.objects.remove(obj, do_unlink=True)
            except Exception:
                pass
        try:
            bpy.data.collections.remove(temp)
        except Exception:
            pass

    new_actions = [a for a in bpy.data.actions if a not in before_actions]
    if not new_actions:
        report.warn("%s: no animation in the file" % os.path.basename(path))
        return []
    # A packed file holds one take per clip, so every action is wanted. A
    # single-clip file yields exactly one.
    new_actions.sort(key=lambda a: a.name)
    return new_actions


class _ClipOptions:
    """The subset of import options the FBX call needs, fixed for clip files."""

    def __init__(self, opts):
        self.import_animation = True
        self.repair_animation = opts.repair_animation
        self.global_scale = opts.global_scale
        self.automatic_bone_orientation = opts.automatic_bone_orientation
        self.ignore_leaf_bones = opts.ignore_leaf_bones
        self.vertex_colors = 'NONE'


def import_folder(context, armature_obj, directory, opts, report):
    """Import every clip under *directory* onto *armature_obj*.

    Returns ``(imported, skipped, actions)``.
    """
    if getattr(opts, "auto_scope", True):
        directory, matched = scope_for(directory, armature_obj)
        if matched:
            report.info("using the '%s' folder, which matches this armature" % matched)

    paths = find_clips(directory, opts.recursive)
    if opts.name_filter:
        needle = opts.name_filter.lower()
        paths = [p for p in paths
                 if needle in os.path.splitext(os.path.basename(p))[0].lower()]

    # The exporter's manifest lets us drop clips before paying to import them.
    min_frames = getattr(opts, "min_frames", 0)
    min_bones = getattr(opts, "min_bones", 0)
    filtered_out = 0
    if min_frames > 0 or min_bones > 0:
        index = read_index(directory)
        if not index:
            report.info("no index.tsv found, so length and bone filters are ignored")
        else:
            kept = []
            for path in paths:
                entry = index.get(os.path.splitext(os.path.basename(path))[0])
                if entry is None:
                    kept.append(path)
                    continue
                if entry["frames"] < min_frames or entry["bones"] < min_bones:
                    filtered_out += 1
                    continue
                kept.append(path)
            paths = kept

    if opts.limit > 0:
        paths = paths[:opts.limit]

    if filtered_out:
        report.info("%d clip(s) filtered out by the length/bone limits" % filtered_out)
    if not paths:
        return 0, 0, []

    index_info = read_index(directory)
    target_bones = {b.name for b in armature_obj.data.bones}
    if armature_obj.animation_data is None:
        armature_obj.animation_data_create()

    prefix = opts.action_prefix.strip()
    imported = []
    skipped = 0

    # Deleting the temporary clip objects clears the active object, which would
    # leave the operator un-pollable for a second run. Put the selection back
    # the way we found it once the batch is done.
    previous_selection = [o for o in context.selected_objects]

    window = context.window_manager
    window.progress_begin(0, len(paths))
    try:
        for index, path in enumerate(paths):
            window.progress_update(index)
            actions = _import_one(context, path, opts, report)
            if not actions:
                skipped += 1
                continue

            stem = os.path.splitext(os.path.basename(path))[0]
            info = index_info.get(stem) if index_info else None

            for action in actions:
                name = take_name_of(action, stem) if len(actions) > 1 else stem
                action.name = (prefix + name) if prefix else name
                action.use_fake_user = opts.fake_user

                meta = index_info.get(name) if index_info else info
                action["fox_clip"] = name
                action["fox_source"] = os.path.basename(path)
                if meta:
                    action["fox_mtar"] = meta.get("mtar", "")
                    action["fox_frames"] = meta.get("frames", 0)
                    action["fox_bones"] = meta.get("bones", 0)

                bones = _fcurve_bones(action)
                overlap = bones & target_bones
                if bones and not overlap:
                    report.warn("%s: none of its %d bone(s) exist on %s"
                                % (name, len(bones), armature_obj.name))
                    skipped += 1
                    if not opts.keep_mismatched:
                        bpy.data.actions.remove(action)
                        continue
                elif bones and len(overlap) < len(bones) * 0.5:
                    report.info("%s: only %d of %d bones matched"
                                % (name, len(overlap), len(bones)))

                if opts.mark_asset and hasattr(action, "asset_mark"):
                    try:
                        action.asset_mark()
                    except Exception:
                        pass

                imported.append(action)
    finally:
        window.progress_end()
        for obj in previous_selection:
            try:
                obj.select_set(True)
            except (ReferenceError, RuntimeError):
                pass
        try:
            armature_obj.select_set(True)
            context.view_layer.objects.active = armature_obj
        except (ReferenceError, RuntimeError):
            pass

    if opts.push_to_nla and imported:
        _push_to_nla(armature_obj, imported, report)
    elif imported:
        armature_obj.animation_data.action = imported[0]

    return len(imported), skipped, imported


def _push_to_nla(armature_obj, actions, report):
    """One muted NLA track per action, so the whole library is browsable."""
    anim = armature_obj.animation_data
    for action in actions:
        try:
            track = anim.nla_tracks.new()
            track.name = action.name
            start = int(round(action.frame_range[0]))
            strip = track.strips.new(action.name, start, action)
            strip.name = action.name
            track.mute = True
        except Exception as exc:
            report.warn("could not stash %s in the NLA: %s" % (action.name, exc))
            return
    anim.action = None
