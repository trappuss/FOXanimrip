# SPDX-License-Identifier: GPL-3.0-or-later
"""Import operators: single/multi file, whole folder, and recursive tree."""

from __future__ import annotations

import os

import bpy
from bpy.props import (BoolProperty, CollectionProperty, EnumProperty,
                       FloatProperty, IntProperty, StringProperty)
from bpy_extras.io_utils import ImportHelper

from . import discovery, importer, materials, prefs
from .prefs import NORMAL_MODE_ITEMS

LOG_TEXT_NAME = "FoxBrowser Import Log"

MERGE_ITEMS = (
    ('NONE', "Keep Separate", "Leave every mesh part as its own object"),
    ('GROUP', "Merge per Group",
     "Join the parts under each MESH_* group into one object "
     "(MESH_head, MESH_body, ...)"),
    ('ALL', "Merge All", "Join every mesh part into a single object"),
)

VERTEX_COLOR_ITEMS = (
    ('NONE', "None", "Do not import vertex colours"),
    ('SRGB', "sRGB", "Import vertex colours as sRGB"),
    ('LINEAR', "Linear", "Import vertex colours as linear"),
)


class FoxImportSettings:
    """Every option the pipeline reads.  Mixed into each import operator."""

    # -- geometry / rig
    global_scale: FloatProperty(
        name="Scale", default=1.0, min=0.001, max=1000.0,
        description="Extra scale on top of the file's own unit conversion")
    automatic_bone_orientation: BoolProperty(
        name="Automatic Bone Orientation", default=False,
        description="Let Blender reorient bones. Off keeps Fox Engine's own "
                    "bone axes, which is what you want if the model is going "
                    "back into the game")
    ignore_leaf_bones: BoolProperty(
        name="Ignore Leaf Bones", default=False,
        description="Skip the end bones the exporter adds to terminate chains")
    armature_in_front: BoolProperty(
        name="Armature In Front", default=True,
        description="Draw the armature on top of the mesh")
    vertex_colors: EnumProperty(
        name="Vertex Colours", items=VERTEX_COLOR_ITEMS, default='SRGB',
        description="Fox Engine uses these as blend and wetness masks")
    merge_meshes: EnumProperty(
        name="Mesh Parts", items=MERGE_ITEMS, default='NONE')
    flatten_hierarchy: BoolProperty(
        name="Flatten Hierarchy", default=False,
        description="Parent every mesh directly to the armature and delete the "
                    "MESH_* group empties, keeping transforms")

    # -- rig JSON
    apply_rig_json: BoolProperty(
        name="Apply Rig JSON", default=True,
        description="Read <model>_rig.json and attach Fox Engine bone hashes, "
                    "rig unit numbers and clip info to the armature")
    bone_collections: BoolProperty(
        name="Bone Collections per Rig Unit", default=True,
        description="Sort bones into one bone collection per Fox Engine rig unit")
    store_rig_json: BoolProperty(
        name="Store Raw JSON", default=False,
        description="Keep the whole rig JSON as a custom property on the "
                    "armature. Useful for round-tripping, costs file size")

    # -- animation
    import_animation: BoolProperty(
        name="Import Animation", default=True,
        description="Import the baked clip. Rig units, help bones and IK are "
                    "already solved into these keys")
    repair_animation: BoolProperty(
        name="Repair Animation Block", default=True,
        description="FoxBrowser tags its FBX animation objects AnimationStack "
                    "/ AnimationLayer instead of AnimStack / AnimLayer, which "
                    "makes Blender's FBX importer abort. This imports a "
                    "corrected temporary copy instead. Turn off only to see "
                    "the raw failure")
    set_scene_fps: BoolProperty(
        name="Set Scene Frame Rate", default=True,
        description="Match the scene frame rate and range to the clip "
                    "(59.94 fps becomes 60/1.001)")
    action_fake_user: BoolProperty(
        name="Protect Action", default=True,
        description="Give the imported action a fake user so it survives a "
                    "save/reload even when unassigned")

    # -- materials
    rebuild_materials: BoolProperty(
        name="Rebuild Materials", default=True,
        description="Replace the imported material nodes with a Principled "
                    "tree wired to the Fox Engine maps")
    wire_extra_maps: BoolProperty(
        name="Find Extra Maps", default=True,
        description="Look in the _textures folder for the maps the FBX never "
                    "referenced (_srm, _trm, _ilm, _lym ...)")
    fuzzy_texture_match: BoolProperty(
        name="Fuzzy Texture Matching", default=True,
        description="Fall back to longest-unique-prefix matching when no "
                    "exactly-named sibling map exists")
    use_alpha: BoolProperty(
        name="Connect Alpha", default=True,
        description="Wire the alpha of _alp maps and switch the material to "
                    "blended (hair, eyelashes, patches)")
    backface_culling: BoolProperty(
        name="Backface Culling", default=False,
        description="Enable backface culling on opaque materials")
    material_prefix: BoolProperty(
        name="Prefix Material Names", default=False,
        description="Rename materials to <model>__<material> so bulk imports "
                    "of different characters never collide")
    normal_mode: EnumProperty(
        name="Normal Maps", items=NORMAL_MODE_ITEMS, default='DXT5NM')
    normal_strength: FloatProperty(
        name="Normal Strength", default=1.0, min=0.0, soft_max=2.0)
    flip_green: BoolProperty(
        name="Flip Green", default=False,
        description="Invert the normal map's Y axis (DirectX-style green)")
    srm_red: EnumProperty(name="SRM Red", items=materials.ROLE_ITEMS,
                          default='SPECULAR')
    srm_green: EnumProperty(name="SRM Green", items=materials.ROLE_ITEMS,
                            default='ROUGHNESS')
    srm_blue: EnumProperty(name="SRM Blue", items=materials.ROLE_ITEMS,
                           default='NONE')

    # -- scene organisation
    parent_collection: StringProperty(
        name="Parent Collection", default="FoxBrowser Imports",
        description="Per-model collections are nested under this one. "
                    "Leave blank to use the scene root")

    def draw_material_settings(self, layout):
        header, body = _panel(layout, "Materials")
        header.prop(self, "rebuild_materials", text="Materials")
        if body:
            body.enabled = self.rebuild_materials
            body.prop(self, "normal_mode")
            row = body.row(align=True)
            row.enabled = self.normal_mode != 'NONE'
            row.prop(self, "normal_strength")
            row.prop(self, "flip_green", toggle=True)
            body.separator()
            body.prop(self, "wire_extra_maps")
            sub = body.column()
            sub.enabled = self.wire_extra_maps
            sub.prop(self, "fuzzy_texture_match")
            sub.prop(self, "srm_red")
            sub.prop(self, "srm_green")
            sub.prop(self, "srm_blue")
            body.separator()
            body.prop(self, "use_alpha")
            body.prop(self, "backface_culling")
            body.prop(self, "material_prefix")

    def draw_settings(self, layout):
        layout.use_property_split = True
        layout.use_property_decorate = False

        header, body = _panel(layout, "Model")
        header.label(text="Model")
        if body:
            body.prop(self, "global_scale")
            body.prop(self, "merge_meshes")
            body.prop(self, "vertex_colors")
            body.prop(self, "flatten_hierarchy")

        header, body = _panel(layout, "Armature")
        header.label(text="Armature & Rig")
        if body:
            body.prop(self, "automatic_bone_orientation")
            body.prop(self, "ignore_leaf_bones")
            body.prop(self, "armature_in_front")
            body.separator()
            body.prop(self, "apply_rig_json")
            sub = body.column()
            sub.enabled = self.apply_rig_json
            sub.prop(self, "bone_collections")
            sub.prop(self, "store_rig_json")

        header, body = _panel(layout, "Animation")
        header.prop(self, "import_animation", text="Animation")
        if body:
            body.enabled = self.import_animation
            body.prop(self, "repair_animation")
            body.prop(self, "set_scene_fps")
            body.prop(self, "action_fake_user")

        self.draw_material_settings(layout)

        header, body = _panel(layout, "Scene")
        header.label(text="Scene")
        if body:
            body.prop(self, "parent_collection")


def _panel(layout, idname):
    """Collapsible sub-panel, with a boxed fallback on older or odd contexts."""
    panel = getattr(layout, "panel", None)
    if panel is not None:
        try:
            header, body = panel("FOXB_%s" % idname, default_closed=False)
            if header is not None:
                return header, body
        except Exception:
            pass
    box = layout.box()
    return box.row(), box.column()


def _write_log(report):
    text = bpy.data.texts.get(LOG_TEXT_NAME)
    if text is None:
        text = bpy.data.texts.new(LOG_TEXT_NAME)
    text.clear()
    text.write(report.as_text() or "Nothing to report.")
    return text


class FoxImportBase(FoxImportSettings):
    """Shared execute loop.  Subclasses only implement :meth:`collect`."""

    def collect(self, context):
        raise NotImplementedError

    def invoke(self, context, event):
        prefs.apply_defaults(self, context)
        context.window_manager.fileselect_add(self)
        return {'RUNNING_MODAL'}

    def draw(self, _context):
        self.draw_settings(self.layout)

    def execute(self, context):
        try:
            sets = self.collect(context)
        except Exception as exc:
            self.report({'ERROR'}, "could not scan for models: %s" % exc)
            return {'CANCELLED'}

        if not sets:
            self.report({'WARNING'}, "No FoxBrowser models found there")
            return {'CANCELLED'}

        if context.mode != 'OBJECT' and context.object is not None:
            try:
                bpy.ops.object.mode_set(mode='OBJECT')
            except RuntimeError:
                pass
        for obj in list(context.selected_objects):
            obj.select_set(False)

        report = importer.Report()
        parent = importer.ensure_parent_collection(context,
                                                   self.parent_collection.strip())

        wm = context.window_manager
        wm.progress_begin(0, len(sets))
        results = []
        try:
            for index, export_set in enumerate(sets):
                wm.progress_update(index)
                results.append(importer.import_set(context, export_set, self,
                                                   report, parent))
        finally:
            wm.progress_end()

        ok = [r for r in results if r.ok]
        failed = [r for r in results if not r.ok]

        report.set_context("")
        report.info("imported %d of %d model(s) in %.1fs"
                    % (len(ok), len(results), sum(r.seconds for r in results)))
        _write_log(report)

        if failed:
            self.report({'WARNING'},
                        "Imported %d of %d models; %d failed - see the '%s' "
                        "text block" % (len(ok), len(results), len(failed),
                                        LOG_TEXT_NAME))
        elif report.warnings:
            self.report({'INFO'},
                        "Imported %d model(s) with %d warning(s) - see '%s'"
                        % (len(ok), report.warnings, LOG_TEXT_NAME))
        else:
            self.report({'INFO'}, "Imported %d model(s)" % len(ok))

        return {'FINISHED'} if ok else {'CANCELLED'}


class FOXB_OT_import_files(FoxImportBase, ImportHelper, bpy.types.Operator):
    """Import one or more FoxBrowser model exports"""
    bl_idname = "foxbrowser.import_files"
    bl_label = "Import FoxBrowser Model(s)"
    bl_options = {'REGISTER', 'UNDO', 'PRESET'}

    filename_ext = ".fbx"
    filter_glob: StringProperty(default="*.fbx;*.dae;*.obj", options={'HIDDEN'})
    files: CollectionProperty(type=bpy.types.OperatorFileListElement,
                              options={'HIDDEN', 'SKIP_SAVE'})
    directory: StringProperty(subtype='DIR_PATH', options={'HIDDEN', 'SKIP_SAVE'})

    all_formats: BoolProperty(
        name="Import Every Format", default=False,
        description="By default a model exported as both .fbx and .dae is "
                    "imported once, preferring .fbx. Turn this on to import "
                    "each container separately")

    def invoke(self, context, event):
        prefs.apply_defaults(self, context)
        return ImportHelper.invoke(self, context, event)

    def collect(self, _context):
        paths = []
        if self.files:
            for item in self.files:
                if item.name:
                    paths.append(os.path.join(self.directory, item.name))
        if not paths and self.filepath:
            paths = [self.filepath]
        return discovery.gather_from_files(
            paths, prefer_all_formats=self.all_formats)

    def draw(self, _context):
        layout = self.layout
        layout.use_property_split = True
        layout.prop(self, "all_formats")
        self.draw_settings(layout)


class FoxFolderBase(FoxImportBase):
    directory: StringProperty(subtype='DIR_PATH', options={'SKIP_SAVE'})
    filter_folder: BoolProperty(default=True, options={'HIDDEN'})
    filter_glob: StringProperty(default="*.fbx;*.dae;*.obj", options={'HIDDEN'})

    all_formats: BoolProperty(
        name="Import Every Format", default=False,
        description="Import .fbx, .dae and .obj versions of the same model "
                    "separately instead of picking one")

    recursive = False

    max_depth: IntProperty(
        name="Max Depth", default=0, min=0, soft_max=12,
        description="How many folder levels to descend. 0 means no limit")

    def collect(self, _context):
        return discovery.gather_from_folder(
            self.directory, recursive=self.recursive,
            prefer_all_formats=self.all_formats,
            max_depth=self.max_depth if self.recursive else 0)

    def draw(self, _context):
        layout = self.layout
        layout.use_property_split = True
        box = layout.box()
        box.label(text=_shorten_path(self.directory), icon='FILE_FOLDER')
        box.prop(self, "all_formats")
        if self.recursive:
            box.prop(self, "max_depth")
        self.draw_settings(layout)


class FOXB_OT_import_folder(FoxFolderBase, bpy.types.Operator):
    """Import every FoxBrowser model in the chosen folder"""
    bl_idname = "foxbrowser.import_folder"
    bl_label = "Import Folder (Bulk)"
    bl_options = {'REGISTER', 'UNDO', 'PRESET'}
    recursive = False


class FOXB_OT_import_recursive(FoxFolderBase, bpy.types.Operator):
    """Import every FoxBrowser model in the chosen folder and all subfolders"""
    bl_idname = "foxbrowser.import_recursive"
    bl_label = "Import Folder Tree (Recursive)"
    bl_options = {'REGISTER', 'UNDO', 'PRESET'}
    recursive = True


def _shorten_path(path, limit=54):
    if not path:
        return "(no folder chosen)"
    path = os.path.normpath(path)
    if len(path) <= limit:
        return path
    return "..." + path[-(limit - 3):]


# -- utility operator -----------------------------------------------------

class FOXB_OT_rewire_materials(FoxImportSettings, bpy.types.Operator):
    """Rebuild the materials on the selected objects from their _textures folder"""
    bl_idname = "foxbrowser.rewire_materials"
    bl_label = "Rewire Materials"
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        return any(o.type == 'MESH' for o in context.selected_objects)

    def invoke(self, context, event):
        prefs.apply_defaults(self, context)
        return context.window_manager.invoke_props_dialog(self, width=400)

    def draw(self, _context):
        layout = self.layout
        layout.use_property_split = True
        layout.use_property_decorate = False
        self.draw_material_settings(layout)

    def execute(self, context):
        objects = [o for o in context.selected_objects if o.type == 'MESH']
        report = importer.Report()
        total = 0
        for export_set in _export_sets_for(objects, report):
            total += materials.rebuild(export_set, objects, self, report)
        _write_log(report)
        if not total:
            self.report({'WARNING'},
                        "Could not locate a FoxBrowser _textures folder for "
                        "the selection - see '%s'" % LOG_TEXT_NAME)
            return {'CANCELLED'}
        self.report({'INFO'}, "Rewired %d material(s)" % total)
        return {'FINISHED'}


def _export_sets_for(objects, report):
    """Reconstruct export sets from the image paths already on the objects."""
    roots = {}
    for mat in materials.iter_materials(objects):
        if not mat.use_nodes or mat.node_tree is None:
            continue
        for node in mat.node_tree.nodes:
            if node.type != 'TEX_IMAGE' or node.image is None:
                continue
            path = bpy.path.abspath(node.image.filepath)
            folder = os.path.dirname(path)
            if not folder.endswith(discovery.TEXTURE_DIR_SUFFIX):
                continue
            model_dir = os.path.dirname(folder)
            name = os.path.basename(folder)[:-len(discovery.TEXTURE_DIR_SUFFIX)]
            roots.setdefault((model_dir, name), None)

    sets = []
    for model_dir, name in roots:
        model_path = ""
        for ext in discovery.MODEL_EXTENSIONS:
            candidate = os.path.join(model_dir, name + ext)
            if os.path.isfile(candidate):
                model_path = candidate
                break
        sets.append(discovery.ExportSet(model_path or
                                        os.path.join(model_dir, name + ".fbx")))
    if not sets:
        report.warn("no image in the selection points at a "
                    "'<model>_textures' folder")
    return sets


class FOXB_OT_import_animations(bpy.types.Operator):
    """Import a folder of clip files as Actions on the selected armature"""
    bl_idname = "foxbrowser.import_animations"
    bl_label = "Import Animations (Bulk)"
    bl_options = {'REGISTER', 'UNDO', 'PRESET'}

    directory: StringProperty(subtype='DIR_PATH', options={'SKIP_SAVE'})
    filter_folder: BoolProperty(default=True, options={'HIDDEN'})
    filter_glob: StringProperty(default="*.fbx", options={'HIDDEN'})

    recursive: BoolProperty(
        name="Include Subfolders", default=True,
        description="foxanimrip writes one subfolder per animation set, so this "
                    "is normally what you want")
    auto_scope: BoolProperty(
        name="Match Folder to Armature", default=True,
        description="A multi-character export has one folder per character. "
                    "When one of them matches the selected armature's model "
                    "name, import only that folder instead of every character's "
                    "clips")
    name_filter: StringProperty(
        name="Name Contains", default="",
        description="Only import clips whose file name contains this text")
    limit: IntProperty(
        name="Limit", default=0, min=0,
        description="Stop after this many clips. 0 means no limit")
    min_frames: IntProperty(
        name="Min. Frames", default=0, min=0,
        description="Skip clips shorter than this, read from the exporter's "
                    "index.tsv. 0 imports everything")
    min_bones: IntProperty(
        name="Min. Matched Bones", default=0, min=0,
        description="Skip clips that drive fewer bones than this, read from the "
                    "exporter's index.tsv. 0 imports everything")
    action_prefix: StringProperty(
        name="Action Prefix", default="",
        description="Prepended to every Action name, e.g. 'gz_'")
    fake_user: BoolProperty(
        name="Protect Actions", default=True,
        description="Give every Action a fake user so the library survives a "
                    "save and reload. Without this Blender discards unassigned "
                    "Actions on the next load")
    push_to_nla: BoolProperty(
        name="Stash in NLA", default=False,
        description="Put each Action on its own muted NLA track. Browsable, "
                    "but heavy above a few hundred clips")
    mark_asset: BoolProperty(
        name="Mark as Assets", default=False,
        description="Mark every Action as an asset so it shows up in the "
                    "Asset Browser")
    keep_mismatched: BoolProperty(
        name="Keep Mismatched", default=False,
        description="Keep Actions whose bones do not exist on the target "
                    "armature instead of discarding them")

    repair_animation: BoolProperty(
        name="Repair Animation Block", default=True,
        description="Fix FoxBrowser's AnimationStack / AnimationLayer class "
                    "tokens, which Blender's FBX importer cannot read. Clips "
                    "written by foxanimrip are already correct")
    global_scale: FloatProperty(name="Scale", default=1.0, min=0.001, max=1000.0)
    automatic_bone_orientation: BoolProperty(
        name="Automatic Bone Orientation", default=False,
        description="MUST match the setting you imported the model with. "
                    "Actions store local bone transforms, so a mismatch here "
                    "scrambles every rotation")
    ignore_leaf_bones: BoolProperty(name="Ignore Leaf Bones", default=False)

    @classmethod
    def poll(cls, context):
        return _target_armature(context) is not None

    def invoke(self, context, event):
        context.window_manager.fileselect_add(self)
        return {'RUNNING_MODAL'}

    def draw(self, context):
        layout = self.layout
        layout.use_property_split = True
        layout.use_property_decorate = False

        armature = _target_armature(context)
        box = layout.box()
        box.label(text=_shorten_path(self.directory), icon='FILE_FOLDER')
        box.label(text="Target: %s" % (armature.name if armature else "none"),
                  icon='ARMATURE_DATA')

        header, body = _panel(layout, "AnimSelect")
        header.label(text="Selection")
        if body:
            body.prop(self, "recursive")
            body.prop(self, "auto_scope")
            body.prop(self, "name_filter")
            body.prop(self, "min_frames")
            body.prop(self, "min_bones")
            body.prop(self, "limit")

        header, body = _panel(layout, "AnimActions")
        header.label(text="Actions")
        if body:
            body.prop(self, "action_prefix")
            body.prop(self, "fake_user")
            body.prop(self, "push_to_nla")
            body.prop(self, "mark_asset")
            body.prop(self, "keep_mismatched")

        header, body = _panel(layout, "AnimImport")
        header.label(text="Import")
        if body:
            body.prop(self, "automatic_bone_orientation")
            body.prop(self, "ignore_leaf_bones")
            body.prop(self, "global_scale")
            body.prop(self, "repair_animation")

    def execute(self, context):
        from . import animlib

        armature = _target_armature(context)
        if armature is None:
            self.report({'ERROR'}, "Select the armature the clips belong to")
            return {'CANCELLED'}
        if not self.directory:
            self.report({'ERROR'}, "No folder chosen")
            return {'CANCELLED'}

        if context.mode != 'OBJECT':
            try:
                bpy.ops.object.mode_set(mode='OBJECT')
            except RuntimeError:
                pass

        report = importer.Report()
        report.set_context("")
        try:
            count, skipped, actions = animlib.import_folder(
                context, armature, self.directory, self, report)
        except Exception as exc:
            report.error(str(exc))
            _write_log(report)
            self.report({'ERROR'}, "Import failed: %s - see '%s'"
                        % (exc, LOG_TEXT_NAME))
            return {'CANCELLED'}

        report.info("%d action(s) imported onto %s, %d skipped"
                    % (count, armature.name, skipped))
        _write_log(report)

        if count == 0:
            self.report({'WARNING'},
                        "No clips imported - see the '%s' text block" % LOG_TEXT_NAME)
            return {'CANCELLED'}
        if report.warnings:
            self.report({'INFO'}, "Imported %d action(s), %d warning(s) - see '%s'"
                        % (count, report.warnings, LOG_TEXT_NAME))
        else:
            self.report({'INFO'}, "Imported %d action(s) onto %s"
                        % (count, armature.name))
        return {'FINISHED'}


def _target_armature(context):
    """The armature the clips should land on: active first, then selection."""
    obj = context.object
    if obj is not None and obj.type == 'ARMATURE':
        return obj
    for candidate in context.selected_objects:
        if candidate.type == 'ARMATURE':
            return candidate
        if candidate.type == 'MESH':
            for mod in candidate.modifiers:
                if mod.type == 'ARMATURE' and mod.object is not None:
                    return mod.object
    return None


classes = (
    FOXB_OT_import_files,
    FOXB_OT_import_folder,
    FOXB_OT_import_recursive,
    FOXB_OT_import_animations,
    FOXB_OT_rewire_materials,
)
