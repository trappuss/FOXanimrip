# SPDX-License-Identifier: GPL-3.0-or-later
"""
Slotted-Action compatibility for Blender 4.4 and later.

Blender 4.4 split an Action into *slots*: the F-curves no longer address an
object directly, they address a slot, and something has to bind that slot to the
object before anything is evaluated. An Action whose slot is not bound has all
of its keyframes and animates nothing.

That is exactly what happens to an imported clip. The FBX importer names the
slot after the object *it* created -- ``OBArmature`` -- and the clip's armature
is thrown away immediately afterwards. Assigning the leftover Action to the
model's armature (``dlf0_main0_def_f_Armature``) leaves the slot unbound,
because Blender only auto-binds when the names line up. The Action Editor shows
a full set of keyframes, the character stays in its rest pose, and there is
nothing in the interface to suggest why.

So: rename each Action's object slot after the armature it is meant for. That
makes Blender's own name-based auto-binding do the right thing everywhere --
this add-on's panel, the Action Editor dropdown, the NLA, linking the file into
another scene -- instead of only in the one code path we control.

Every function here is a no-op on Blender 4.2/4.3, which has no slots.
"""

from __future__ import annotations

import bpy

#: 4.4 is where Actions gained slots. Before that an Action binds by itself.
HAS_SLOTS = bpy.app.version >= (4, 4, 0)


def object_slots(action):
    """The slots of *action* that can drive an Object, newest API only."""
    slots = getattr(action, "slots", None)
    if not slots:
        return []
    out = []
    for slot in slots:
        # target_id_type is 'UNSPECIFIED' until the slot has been used once.
        kind = getattr(slot, "target_id_type", 'OBJECT')
        if kind in ('OBJECT', 'UNSPECIFIED'):
            out.append(slot)
    return out


def retarget(action, id_data):
    """Rename *action*'s object slot after *id_data* so Blender auto-binds it.

    Returns True when a slot was renamed. Safe to call repeatedly and safe to
    call on an Action that is already bound.
    """
    if not HAS_SLOTS or id_data is None:
        return False
    slots = object_slots(action)
    if len(slots) != 1:
        # Zero slots: nothing to bind, the Action is empty. More than one: the
        # Action drives several objects and renaming would be a guess.
        return False
    slot = slots[0]
    try:
        if slot.name_display != id_data.name:
            slot.name_display = id_data.name
        return True
    except (AttributeError, RuntimeError):
        return False


def bind(anim_data, action, id_data=None):
    """Assign *action* and make sure a slot is actually bound to it.

    This is ``anim_data.action = action`` on any Blender, plus the slot binding
    4.4 needs. Returns the bound slot, or None.
    """
    anim_data.action = action
    if not HAS_SLOTS:
        return None
    if getattr(anim_data, "action_slot", None) is not None:
        return anim_data.action_slot

    candidates = list(getattr(anim_data, "action_suitable_slots", None) or ())
    if not candidates:
        candidates = object_slots(action)
    for slot in candidates:
        try:
            anim_data.action_slot = slot
            return slot
        except (TypeError, RuntimeError):
            continue
    return None


def bind_strip(strip, action, id_data=None):
    """Bind an NLA strip's slot, so stashed clips play when unmuted."""
    if not HAS_SLOTS:
        return None
    if getattr(strip, "action_slot", None) is not None:
        return strip.action_slot

    candidates = list(getattr(strip, "action_suitable_slots", None) or ())
    if not candidates:
        candidates = object_slots(action)
    for slot in candidates:
        try:
            strip.action_slot = slot
            return slot
        except (TypeError, RuntimeError, AttributeError):
            continue
    return None


def is_bound(anim_data):
    """False when an Action is assigned but nothing will be evaluated."""
    if anim_data is None or anim_data.action is None:
        return False
    if not HAS_SLOTS:
        return True
    return getattr(anim_data, "action_slot", None) is not None
