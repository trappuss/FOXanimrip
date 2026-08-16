# SPDX-License-Identifier: GPL-3.0-or-later
"""File > Import entries and the 3D View sidebar panel."""

from __future__ import annotations

import bpy

from .operators import LOG_TEXT_NAME


class FOXB_MT_import(bpy.types.Menu):
    bl_idname = "FOXB_MT_import"
    bl_label = "FoxBrowser (MGSV / Ground Zeroes)"

    def draw(self, _context):
        layout = self.layout
        layout.operator("foxbrowser.import_files",
                        text="Model(s)...", icon='FILE_3D')
        layout.operator("foxbrowser.import_folder",
                        text="Folder (Bulk)...", icon='FILE_FOLDER')
        layout.operator("foxbrowser.import_recursive",
                        text="Folder Tree (Recursive)...", icon='OUTLINER')
        layout.separator()
        layout.operator("foxbrowser.import_animations",
                        text="Animations onto Armature...", icon='ANIM')


class FOXB_PT_sidebar(bpy.types.Panel):
    bl_idname = "FOXB_PT_sidebar"
    bl_label = "FoxBrowser Import"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "FoxBrowser"

    def draw(self, context):
        layout = self.layout

        col = layout.column(align=True)
        col.scale_y = 1.25
        col.operator("foxbrowser.import_files",
                     text="Model(s)", icon='FILE_3D')
        col.operator("foxbrowser.import_folder",
                     text="Folder (Bulk)", icon='FILE_FOLDER')
        col.operator("foxbrowser.import_recursive",
                     text="Folder Tree", icon='OUTLINER')

        layout.separator()
        col = layout.column(align=True)
        col.scale_y = 1.25
        col.operator("foxbrowser.import_animations",
                     text="Animations (Bulk)", icon='ANIM')
        if not any(o.type == 'ARMATURE' for o in context.selected_objects) \
                and (context.object is None or context.object.type != 'ARMATURE'):
            row = layout.row()
            row.scale_y = 0.8
            row.label(text="Select an armature first", icon='INFO')

        layout.separator()
        layout.operator("foxbrowser.rewire_materials",
                        text="Rewire Materials", icon='NODE_MATERIAL')

        text = bpy.data.texts.get(LOG_TEXT_NAME)
        if text is not None:
            box = layout.box()
            box.label(text="Last run", icon='TEXT')
            lines = list(text.lines)
            warnings = sum(1 for line in lines
                           if line.body.startswith(("WARNING", "ERROR")))
            summary = lines[-1].body if lines else ""
            col = box.column(align=True)
            col.scale_y = 0.85
            col.label(text=summary[:48] if summary else "-")
            if warnings:
                col.label(text="%d warning(s) or error(s)" % warnings,
                          icon='ERROR')


class FOXB_PT_rig_info(bpy.types.Panel):
    bl_idname = "FOXB_PT_rig_info"
    bl_label = "Fox Engine Rig"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "FoxBrowser"

    @classmethod
    def poll(cls, context):
        obj = context.object
        return obj is not None and "fox_model" in obj

    def draw(self, context):
        obj = context.object
        layout = self.layout
        layout.use_property_split = True

        col = layout.column(align=True)
        col.label(text=str(obj.get("fox_model", "")), icon='ARMATURE_DATA')
        col.label(text="Source: %s" % obj.get("fox_source", "?"))

        box = layout.box()
        grid = box.grid_flow(columns=2, even_columns=True, align=True)
        for label, key in (("Bones", "fox_bone_count"),
                           ("Meshes", "fox_mesh_count"),
                           ("Rig units", "fox_rig_units"),
                           ("Segments", "fox_rig_segments"),
                           ("Help bone ops", "fox_help_bone_ops")):
            if key in obj:
                grid.label(text=label)
                grid.label(text=str(obj[key]))

        if "fox_clip_name" in obj:
            box = layout.box()
            box.label(text="Clip: %s" % obj["fox_clip_name"], icon='ACTION')
            col = box.column(align=True)
            col.scale_y = 0.85
            col.label(text="%d frames @ %.2f fps"
                          % (obj.get("fox_clip_frames", 0),
                             obj.get("fox_clip_fps", 0.0)))
            if obj.get("fox_clip_baked"):
                col.label(text="Rig units, help bones and IK are baked in")

        bone = context.active_bone
        if bone is not None and "fox_hash" in bone:
            box = layout.box()
            box.label(text=bone.name, icon='BONE_DATA')
            col = box.column(align=True)
            col.scale_y = 0.85
            col.label(text="Hash: %s" % bone["fox_hash"])
            if "fox_rig_unit" in bone:
                col.label(text="Rig unit: %d" % bone["fox_rig_unit"])


def menu_func_import(self, _context):
    self.layout.menu(FOXB_MT_import.bl_idname, icon='FILE_3D')


classes = (
    FOXB_MT_import,
    FOXB_PT_sidebar,
    FOXB_PT_rig_info,
)


def register_menus():
    bpy.types.TOPBAR_MT_file_import.append(menu_func_import)


def unregister_menus():
    bpy.types.TOPBAR_MT_file_import.remove(menu_func_import)
