# SPDX-License-Identifier: GPL-3.0-or-later
"""
Applying the ``<model>_rig.json`` sidecar to the imported armature.

The JSON does not carry transforms worth trusting -- the FBX already has the
solved pose -- but it does carry things the FBX throws away:

* the 48-bit Fox Engine bone name hash, which is what the game actually keys
  on and what you need if you ever want to write a model back;
* which *rig unit* each bone belongs to, i.e. Fox Engine's procedural rig
  groups (twist chains, IK helpers, cloth segments);
* the source clip's frame count and rate, and the note that rig units, help
  bones and IK are already baked into those keys.

All of it is attached as custom properties and bone collections, so it survives
a .blend save and shows up in the sidebar.
"""

from __future__ import annotations

import json
import os

UNASSIGNED_COLLECTION = "Skeleton"
UNIT_COLLECTION_FMT = "Rig Unit %02d"


def load(path):
    """Parse a rig JSON file.  Returns ``None`` on any problem."""
    if not path or not os.path.isfile(path):
        return None
    try:
        with open(path, "r", encoding="utf-8") as fh:
            data = json.load(fh)
    except (OSError, ValueError):
        return None
    if not isinstance(data, dict) or "skeleton" not in data:
        return None
    return data


def _unit_by_hash(data):
    """``{hash32: unit}`` from the ``rig.bones`` table."""
    result = {}
    rig = data.get("rig") or {}
    for entry in rig.get("bones") or ():
        h = str(entry.get("hash32", "")).lower()
        if h:
            result[h] = int(entry.get("unit", 0))
    return result


def _new_collection(armature, name):
    """Create or fetch a bone collection, tolerating API drift."""
    collections = getattr(armature, "collections", None)
    if collections is None:
        return None
    existing = collections.get(name) if hasattr(collections, "get") else None
    if existing is not None:
        return existing
    try:
        return collections.new(name)
    except Exception:
        return None


def apply(armature_obj, data, opts, report):
    """Annotate *armature_obj* from parsed rig JSON *data*.

    Returns a small stats dict for the caller's summary line.
    """
    armature = armature_obj.data
    bones = armature.bones
    unit_by_hash = _unit_by_hash(data)

    matched = 0
    missing = []
    unit_members = {}

    for entry in data.get("skeleton") or ():
        name = entry.get("name")
        if not name:
            continue
        bone = bones.get(name)
        if bone is None:
            missing.append(name)
            continue
        matched += 1

        full_hash = str(entry.get("hash", "")).lower()
        bone["fox_hash"] = full_hash
        bone["fox_index"] = int(entry.get("i", -1))
        unit = unit_by_hash.get(full_hash[-8:], 0)
        if unit:
            bone["fox_rig_unit"] = unit
        world = entry.get("world")
        if isinstance(world, (list, tuple)) and len(world) == 3:
            bone["fox_rest"] = [float(v) for v in world]

        unit_members.setdefault(unit, []).append(bone)

    extra = [b.name for b in bones if b.name not in
             {e.get("name") for e in (data.get("skeleton") or ())}]

    if opts.bone_collections and unit_members:
        for unit in sorted(unit_members):
            label = (UNIT_COLLECTION_FMT % unit) if unit else UNASSIGNED_COLLECTION
            collection = _new_collection(armature, label)
            if collection is None:
                report.info("bone collections unavailable in this Blender build")
                break
            for bone in unit_members[unit]:
                try:
                    collection.assign(bone)
                except Exception:
                    pass

    clip = data.get("clip") or {}
    armature_obj["fox_model"] = data.get("model", "")
    armature_obj["fox_bone_count"] = int(data.get("bones", 0))
    armature_obj["fox_mesh_count"] = int(data.get("meshes", 0))
    armature_obj["fox_help_bone_ops"] = int(data.get("helpBoneOps", 0))
    rig = data.get("rig") or {}
    armature_obj["fox_rig_units"] = int(rig.get("units", 0))
    armature_obj["fox_rig_segments"] = int(rig.get("segments", 0))
    if clip:
        armature_obj["fox_clip_name"] = clip.get("name", "")
        armature_obj["fox_clip_frames"] = int(clip.get("frames", 0))
        armature_obj["fox_clip_fps"] = float(clip.get("fps", 0.0))
        armature_obj["fox_clip_baked"] = bool(clip.get("baked", False))

    if opts.store_rig_json:
        try:
            armature_obj["fox_rig_json"] = json.dumps(data, separators=(",", ":"))
        except Exception:
            pass

    if missing:
        report.warn("%d bone(s) in the rig JSON are not in the model: %s%s"
                    % (len(missing), ", ".join(missing[:6]),
                       " ..." if len(missing) > 6 else ""))
    if extra:
        report.info("%d bone(s) in the model are not in the rig JSON: %s%s"
                    % (len(extra), ", ".join(extra[:6]),
                       " ..." if len(extra) > 6 else ""))

    return {
        "matched": matched,
        "missing": len(missing),
        "extra": len(extra),
        "units": len([u for u in unit_members if u]),
    }


def clip_fps(data):
    """``(fps, fps_base)`` for the source clip, or ``None``.

    59.94 fps is stored the way Blender wants it: 60 / 1.001.
    """
    clip = (data or {}).get("clip") or {}
    fps = float(clip.get("fps", 0.0) or 0.0)
    if fps <= 0.0:
        return None
    rounded = round(fps)
    if abs(fps - rounded / 1.001) < 0.01:
        return int(rounded), 1.001
    if abs(fps - rounded) < 0.001:
        return int(rounded), 1.0
    return int(round(fps)), round(fps) / fps
