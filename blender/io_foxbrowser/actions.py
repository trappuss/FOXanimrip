# SPDX-License-Identifier: GPL-3.0-or-later
"""
A browser for an imported Action library.

Four thousand Actions in the Action Editor dropdown is not a usable interface.
This is a searchable list in the sidebar that knows what the clips are: it reads
the ``fox_clip`` / ``fox_mtar`` / ``fox_frames`` / ``fox_bones`` properties the
importer attaches, so you can search by name, filter by animation set, and see
how long a clip is and how much of the rig it drives before assigning it.
"""

from __future__ import annotations

import bpy


def clip_actions():
    """Every Action that came from a FoxBrowser clip."""
    return [a for a in bpy.data.actions if "fox_clip" in a]


def _matches(action, needle, mtar, min_frames):
    if needle and needle.lower() not in action.name.lower():
        return False
    if mtar and mtar != 'ALL' and str(action.get("fox_mtar", "")) != mtar:
        return False
    if min_frames > 0 and int(action.get("fox_frames", 0) or 0) < min_frames:
        return False
    return True


def mtar_items(self, context):
    """Animation sets present in the file, for the filter dropdown."""
    names = sorted({str(a.get("fox_mtar", "")) for a in clip_actions()
                    if a.get("fox_mtar")})
    items = [('ALL', "All sets", "Every animation set")]
    items.extend((name, name, "") for name in names)
    return items


class FOXB_UL_actions(bpy.types.UIList):
    """Rows of Actions, filtered by the sidebar's search settings."""

    def draw_item(self, context, layout, data, item, icon, active_data,
                  active_prop, index):
        scene = context.scene
        row = layout.row(align=True)
        row.label(text=item.name, icon='ACTION')

        frames = int(item.get("fox_frames", 0) or 0)
        bones = int(item.get("fox_bones", 0) or 0)
        if frames or bones:
            sub = row.row(align=True)
            sub.alignment = 'RIGHT'
            sub.scale_x = 0.9
            sub.label(text="%df" % frames if frames else "")
            sub.label(text="%d bones" % bones if bones else "")
        if item.use_fake_user:
            row.label(text="", icon='FAKE_USER_ON')

    def filter_items(self, context, data, propname):
        actions = getattr(data, propname)
        scene = context.scene
        needle = scene.foxb_action_search
        mtar = scene.foxb_action_mtar
        min_frames = scene.foxb_action_min_frames
        only_clips = scene.foxb_action_only_clips

        flags = []
        for action in actions:
            visible = True
            if only_clips and "fox_clip" not in action:
                visible = False
            elif not _matches(action, needle, mtar, min_frames):
                visible = False
            flags.append(self.bitflag_filter_item if visible else 0)

        order = []
        if self.use_filter_sort_alpha:
            order = bpy.types.UI_UL_list.sort_items_by_name(actions, "name")
        return flags, order


def _active_action(context):
    index = context.scene.foxb_action_index
    actions = bpy.data.actions
    if 0 <= index < len(actions):
        return actions[index]
    return None


def _target_armature(context):
    obj = context.object
    if obj is not None and obj.type == 'ARMATURE':
        return obj
    for candidate in context.selected_objects:
        if candidate.type == 'ARMATURE':
            return candidate
    return None


class FOXB_OT_action_assign(bpy.types.Operator):
    """Put this Action on the selected armature and set the frame range"""
    bl_idname = "foxbrowser.action_assign"
    bl_label = "Assign"
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        return _target_armature(context) is not None and _active_action(context) is not None

    def execute(self, context):
        armature = _target_armature(context)
        action = _active_action(context)
        if armature.animation_data is None:
            armature.animation_data_create()
        armature.animation_data.action = action

        start, end = action.frame_range
        context.scene.frame_start = int(round(start))
        context.scene.frame_end = max(int(round(end)), int(round(start)))
        context.scene.frame_set(context.scene.frame_start)
        self.report({'INFO'}, "Playing %s" % action.name)
        return {'FINISHED'}


class FOXB_OT_action_stash(bpy.types.Operator):
    """Move this Action onto its own muted NLA track"""
    bl_idname = "foxbrowser.action_stash"
    bl_label = "Stash"
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        return _target_armature(context) is not None and _active_action(context) is not None

    def execute(self, context):
        armature = _target_armature(context)
        action = _active_action(context)
        if armature.animation_data is None:
            armature.animation_data_create()
        anim = armature.animation_data
        try:
            track = anim.nla_tracks.new()
            track.name = action.name
            track.strips.new(action.name, int(round(action.frame_range[0])), action)
            track.mute = True
        except Exception as exc:
            self.report({'ERROR'}, "Could not stash: %s" % exc)
            return {'CANCELLED'}
        self.report({'INFO'}, "Stashed %s" % action.name)
        return {'FINISHED'}


class FOXB_OT_action_remove(bpy.types.Operator):
    """Delete this Action"""
    bl_idname = "foxbrowser.action_remove"
    bl_label = "Delete"
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        return _active_action(context) is not None

    def execute(self, context):
        action = _active_action(context)
        name = action.name
        try:
            bpy.data.actions.remove(action)
        except Exception as exc:
            self.report({'ERROR'}, str(exc))
            return {'CANCELLED'}
        self.report({'INFO'}, "Deleted %s" % name)
        return {'FINISHED'}


class FOXB_OT_action_purge(bpy.types.Operator):
    """Delete every clip Action currently shown by the filter"""
    bl_idname = "foxbrowser.action_purge"
    bl_label = "Delete Filtered"
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        return len(clip_actions()) > 0

    def invoke(self, context, event):
        return context.window_manager.invoke_confirm(self, event)

    def execute(self, context):
        scene = context.scene
        doomed = [a for a in clip_actions()
                  if _matches(a, scene.foxb_action_search, scene.foxb_action_mtar,
                              scene.foxb_action_min_frames)]
        for action in doomed:
            try:
                bpy.data.actions.remove(action)
            except Exception:
                pass
        self.report({'INFO'}, "Deleted %d action(s)" % len(doomed))
        return {'FINISHED'}


class FOXB_PT_actions(bpy.types.Panel):
    bl_idname = "FOXB_PT_actions"
    bl_label = "Animation Library"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "FoxBrowser"

    def draw(self, context):
        layout = self.layout
        scene = context.scene
        clips = clip_actions()

        row = layout.row(align=True)
        row.prop(scene, "foxb_action_search", text="", icon='VIEWZOOM')
        row.prop(scene, "foxb_action_only_clips", text="", icon='FILTER')

        row = layout.row(align=True)
        row.prop(scene, "foxb_action_mtar", text="")
        row.prop(scene, "foxb_action_min_frames", text="Min f")

        layout.template_list("FOXB_UL_actions", "", bpy.data, "actions",
                             scene, "foxb_action_index", rows=10)

        armature = _target_armature(context)
        col = layout.column(align=True)
        col.enabled = armature is not None
        row = col.row(align=True)
        row.operator("foxbrowser.action_assign", icon='PLAY')
        row.operator("foxbrowser.action_stash", icon='NLA_PUSHDOWN')
        col.operator("foxbrowser.action_remove", icon='TRASH')

        if armature is None:
            info = layout.row()
            info.scale_y = 0.8
            info.label(text="Select an armature to assign", icon='INFO')

        footer = layout.row()
        footer.scale_y = 0.8
        footer.label(text="%d clip action(s) in this file" % len(clips))
        layout.operator("foxbrowser.action_purge", icon='X')


classes = (
    FOXB_UL_actions,
    FOXB_OT_action_assign,
    FOXB_OT_action_stash,
    FOXB_OT_action_remove,
    FOXB_OT_action_purge,
    FOXB_PT_actions,
)


def register_props():
    bpy.types.Scene.foxb_action_search = bpy.props.StringProperty(
        name="Search", description="Show only Actions whose name contains this",
        default="", options={'TEXTEDIT_UPDATE'})
    bpy.types.Scene.foxb_action_index = bpy.props.IntProperty(
        name="Action", default=0)
    bpy.types.Scene.foxb_action_mtar = bpy.props.EnumProperty(
        name="Set", description="Show only clips from one animation set",
        items=mtar_items)
    bpy.types.Scene.foxb_action_min_frames = bpy.props.IntProperty(
        name="Min Frames", description="Hide clips shorter than this",
        default=0, min=0)
    bpy.types.Scene.foxb_action_only_clips = bpy.props.BoolProperty(
        name="Imported Clips Only",
        description="Hide Actions that did not come from a FoxBrowser clip",
        default=True)


def unregister_props():
    for name in ("foxb_action_search", "foxb_action_index", "foxb_action_mtar",
                 "foxb_action_min_frames", "foxb_action_only_clips"):
        if hasattr(bpy.types.Scene, name):
            delattr(bpy.types.Scene, name)
