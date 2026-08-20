# SPDX-License-Identifier: GPL-3.0-or-later
"""
The in-Blender model browser: a searchable list of every ripped model, with a
plain-language description, that imports or adds the chosen one with a click.

Point it at a rips folder, hit Scan, then type in the search box -- "quiet",
"arm female", "hat", "respirator" -- and the matching models surface out of a
folder of terse filenames. Import a standalone model, or add one onto the active
character rig with the assembler.
"""

import time

import bpy
from bpy.props import (CollectionProperty, EnumProperty, IntProperty,
                       StringProperty)

from . import assembler, catalog, discovery, importer, prefs
from .operators import LOG_TEXT_NAME, FoxImportSettings, _active_armature

# Double-click on a list row: within this window, a second click on the same row
# activates it (import, or add to the active rig). One click just selects.
_DOUBLE_CLICK_S = 0.35
_last_click = [-1, 0.0]   # [row index, monotonic time of last click]


class FOXB_CatalogItem(bpy.types.PropertyGroup):
    name: StringProperty()
    path: StringProperty()
    game: StringProperty()
    category: StringProperty()
    desc: StringProperty()
    gender: StringProperty()


class FOXB_BuildItem(bpy.types.PropertyGroup):
    name: StringProperty()
    path: StringProperty()


class FOXB_UL_catalog(bpy.types.UIList):
    def draw_item(self, _context, layout, _data, item, _icon, _active, _prop, index):
        if self.layout_type in {'DEFAULT', 'COMPACT'}:
            # Two columns: icon + name flush left, description flush right.
            # The parent row must NOT be align=True -- that joins the two
            # sub-rows and packs them together on the left, which bunches the
            # description up behind the name. A plain row distributes the width
            # so each sub-row's alignment lands its content at the intended edge.
            row = layout.row()
            # The name is a borderless operator so a click routes through our
            # double-click handler; a second click imports / adds it. Its
            # sub-row is left-aligned so the label hugs the icon instead of the
            # operator button centring its text across the column.
            name = row.row()
            name.alignment = 'LEFT'
            op = name.operator("foxbrowser.catalog_click", text=item.name,
                               icon='OUTLINER_OB_ARMATURE', emboss=False)
            op.index = index
            sub = row.row()
            sub.alignment = 'RIGHT'
            sub.label(text=item.desc)
        else:
            layout.label(text=item.name)

    def filter_items(self, context, data, propname):
        items = getattr(data, propname)
        wm = context.window_manager
        term = (wm.foxb_cat_search or "").lower()
        game = wm.foxb_cat_game
        cat = wm.foxb_cat_category
        gender = wm.foxb_cat_gender
        flags = []
        for it in items:
            ok = True
            if term:
                ok = term in (it.name + " " + it.desc + " " + it.category).lower()
            if ok and game != 'ALL':
                ok = it.game == game
            if ok and cat != 'ALL':
                ok = it.category == cat
            if ok and gender != 'ALL':
                ok = (it.gender or "Unisex") == gender
            flags.append(self.bitflag_filter_item if ok else 0)
        return flags, []


class FOXB_OT_catalog_scan(bpy.types.Operator):
    """Scan the rips folder for models and describe them"""
    bl_idname = "foxbrowser.catalog_scan"
    bl_label = "Scan Rips Folder"
    bl_options = {'REGISTER'}

    def execute(self, context):
        wm = context.window_manager
        root = bpy.path.abspath(wm.foxb_cat_root)
        wm.foxb_cat_items.clear()
        found = catalog.scan(root)
        games = set()
        cats = set()
        for rec in found:
            it = wm.foxb_cat_items.add()
            it.name = rec["name"]; it.path = rec["path"]
            it.game = rec["game"]; it.category = rec["category"]; it.desc = rec["desc"]
            it.gender = rec.get("gender", "Unisex")
            games.add(rec["game"]); cats.add(rec["category"])
        _rebuild_category_items(cats)
        wm.foxb_cat_index = 0
        if not found:
            self.report({'WARNING'},
                        "No model FBXs found under that folder")
        else:
            self.report({'INFO'}, "found %d model(s) in %d game(s)"
                        % (len(found), len(games)))
        return {'FINISHED'}


def _selected(context):
    wm = context.window_manager
    idx = wm.foxb_cat_index
    if 0 <= idx < len(wm.foxb_cat_items):
        return wm.foxb_cat_items[idx]
    return None


def _do_import(context, item, settings):
    """Import one catalog model standalone. True on success."""
    settings.import_animation = False        # a model, not a clip
    sets = discovery.gather_from_files([item.path])
    if not sets:
        return False
    report = importer.Report()
    parent = importer.ensure_parent_collection(context, "")
    for es in sets:
        importer.import_set(context, es, settings, report, parent)
    text = bpy.data.texts.get(LOG_TEXT_NAME) or bpy.data.texts.new(LOG_TEXT_NAME)
    text.clear(); text.write(report.as_text() or "Imported.")
    return True


def _do_add(context, master, item, settings):
    """Add one catalog model onto *master* armature. Returns bones merged."""
    settings.import_animation = False
    coll = master.users_collection[0] if master.users_collection else None
    lines = []
    res = assembler.add_parts(master, [item.path], report=lines.append,
                              link_collection=coll, settings=settings)
    text = bpy.data.texts.get(LOG_TEXT_NAME) or bpy.data.texts.new(LOG_TEXT_NAME)
    text.clear(); text.write("\n".join(lines) or "Added.")
    for o in context.selected_objects:
        o.select_set(False)
    master.select_set(True)
    context.view_layer.objects.active = master
    return res.bones_merged


def _selected_armature(context):
    """An armature the user has actually SELECTED -- not merely the lingering
    active object after a deselect. This governs whether a double-click adds to
    a character or imports standalone, so 'nothing selected' reliably imports."""
    return next((o for o in context.selected_objects if o.type == 'ARMATURE'), None)


class FOXB_OT_catalog_click(FoxImportSettings, bpy.types.Operator):
    """Select this model. Double-click to import it -- or, with a character rig
    selected, to add it to that character"""
    bl_idname = "foxbrowser.catalog_click"
    bl_label = "Select / Activate Model"
    # UNDO, and the work is done here directly (not via a nested operator) so a
    # double-click is one clean undo step -- undo removes just this model, not
    # also the one imported before it.
    bl_options = {'REGISTER', 'UNDO', 'INTERNAL'}

    index: IntProperty(default=-1)

    def execute(self, context):
        wm = context.window_manager
        if not (0 <= self.index < len(wm.foxb_cat_items)):
            return {'CANCELLED'}
        wm.foxb_cat_index = self.index
        now = time.monotonic()
        prev_index, prev_t = _last_click
        if not (prev_index == self.index and (now - prev_t) <= _DOUBLE_CLICK_S):
            _last_click[0], _last_click[1] = self.index, now
            return {'FINISHED'}

        # Second click on the same row -> act on it.
        _last_click[0], _last_click[1] = -1, 0.0
        item = _selected(context)
        if item is None:
            return {'CANCELLED'}
        prefs.apply_defaults(self, context)
        master = _selected_armature(context)
        if master is not None:
            merged = _do_add(context, master, item, self)
            self.report({'INFO'}, "added %s (%d bone(s) merged)" % (item.name, merged))
        elif _do_import(context, item, self):
            self.report({'INFO'}, "imported %s" % item.name)
        else:
            self.report({'ERROR'}, "could not read %s" % item.name)
            return {'CANCELLED'}
        return {'FINISHED'}


class FOXB_OT_catalog_import(FoxImportSettings, bpy.types.Operator):
    """Import the selected model with the full FoxBrowser treatment"""
    bl_idname = "foxbrowser.catalog_import"
    bl_label = "Import Selected"
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        return _selected(context) is not None

    def execute(self, context):
        item = _selected(context)
        if item is None:
            self.report({'ERROR'}, "nothing selected")
            return {'CANCELLED'}
        prefs.apply_defaults(self, context)
        if not _do_import(context, item, self):
            self.report({'ERROR'}, "could not read %s" % item.name)
            return {'CANCELLED'}
        self.report({'INFO'}, "imported %s" % item.name)
        return {'FINISHED'}


class FOXB_OT_catalog_add(FoxImportSettings, bpy.types.Operator):
    """Add the selected model onto the active character rig"""
    bl_idname = "foxbrowser.catalog_add"
    bl_label = "Add to Active Character"
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        return _selected(context) is not None and _active_armature(context) is not None

    def execute(self, context):
        item = _selected(context)
        master = _active_armature(context)
        if item is None or master is None:
            self.report({'ERROR'}, "select a model and an active character rig")
            return {'CANCELLED'}
        prefs.apply_defaults(self, context)
        merged = _do_add(context, master, item, self)
        self.report({'INFO'}, "added %s (%d bone(s) merged)" % (item.name, merged))
        return {'FINISHED'}


class FOXB_OT_catalog_queue(bpy.types.Operator):
    """Add the selected model to the assembly queue"""
    bl_idname = "foxbrowser.catalog_queue"
    bl_label = "Queue for Assembly"
    bl_options = {'REGISTER'}

    @classmethod
    def poll(cls, context):
        return _selected(context) is not None

    def execute(self, context):
        item = _selected(context)
        wm = context.window_manager
        if any(b.path == item.path for b in wm.foxb_build):
            self.report({'INFO'}, "already queued")
            return {'FINISHED'}
        b = wm.foxb_build.add()
        b.name = item.name
        b.path = item.path
        self.report({'INFO'}, "queued %s (%d in build)" % (item.name, len(wm.foxb_build)))
        return {'FINISHED'}


class FOXB_OT_catalog_unqueue(bpy.types.Operator):
    """Clear the assembly queue"""
    bl_idname = "foxbrowser.catalog_unqueue"
    bl_label = "Clear Queue"
    bl_options = {'REGISTER'}

    def execute(self, context):
        context.window_manager.foxb_build.clear()
        return {'FINISHED'}


class FOXB_OT_catalog_assemble(FoxImportSettings, bpy.types.Operator):
    """Assemble the whole queue at once: import the first as the base body, then
    add the rest onto its rig -- one character in one click"""
    bl_idname = "foxbrowser.catalog_assemble"
    bl_label = "Assemble Queue (base first)"
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        return len(context.window_manager.foxb_build) >= 1

    def execute(self, context):
        wm = context.window_manager
        items = list(wm.foxb_build)
        prefs.apply_defaults(self, context)

        # First queued item is the base body.
        if not _do_import(context, items[0], self):
            self.report({'ERROR'}, "could not import base %s" % items[0].name)
            return {'CANCELLED'}
        master = _active_armature(context)
        if master is None:
            self.report({'WARNING'},
                        "%s imported but has no armature to build on" % items[0].name)
            return {'FINISHED'}

        added = 0
        for it in items[1:]:
            try:
                _do_add(context, master, it, self)
                added += 1
            except Exception as exc:   # keep going; one bad part shouldn't abort
                print("foxbrowser: add %s failed: %s" % (it.name, exc))
        wm.foxb_build.clear()
        self.report({'INFO'}, "assembled %s + %d part(s)" % (items[0].name, added))
        return {'FINISHED'}


class FOXB_PT_catalog(bpy.types.Panel):
    bl_idname = "FOXB_PT_catalog"
    bl_label = "Model Browser"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "FoxBrowser"
    bl_options = {'DEFAULT_CLOSED'}

    def draw(self, context):
        wm = context.window_manager
        layout = self.layout
        row = layout.row(align=True)
        row.prop(wm, "foxb_cat_root", text="")
        row.operator("foxbrowser.catalog_scan", text="", icon='FILE_REFRESH')

        n = len(wm.foxb_cat_items)
        if n == 0:
            layout.label(text="Set your models folder, then Scan", icon='INFO')
            return

        layout.prop(wm, "foxb_cat_search", text="", icon='VIEWZOOM')
        row = layout.row(align=True)
        row.prop(wm, "foxb_cat_game", text="")
        row.prop(wm, "foxb_cat_category", text="")
        layout.prop(wm, "foxb_cat_gender", text="")
        layout.template_list("FOXB_UL_catalog", "", wm, "foxb_cat_items",
                             wm, "foxb_cat_index", rows=8)

        item = _selected(context)
        if item is not None:
            box = layout.box()
            box.scale_y = 0.85
            box.label(text=item.name, icon='OUTLINER_OB_ARMATURE')
            box.label(text=item.desc)
            box.label(text=item.game)

        col = layout.column(align=True)
        col.scale_y = 1.2
        col.operator("foxbrowser.catalog_import", icon='IMPORT')
        add = col.row(align=True)
        add.enabled = _active_armature(context) is not None
        add.operator("foxbrowser.catalog_add", icon='ADD')
        if _active_armature(context) is None:
            r = layout.row(); r.scale_y = 0.8
            r.label(text="select a rig to enable 'Add'", icon='INFO')

        # One-click assembly: queue a base + parts, build them in one go.
        build = wm.foxb_build
        box = layout.box()
        head = box.row(align=True)
        head.operator("foxbrowser.catalog_queue", text="Queue Selected", icon='PLUS')
        if len(build):
            head.operator("foxbrowser.catalog_unqueue", text="", icon='X')
            col = box.column(align=True)
            col.scale_y = 0.8
            for i, b in enumerate(build):
                col.label(text=("base:  " if i == 0 else "  + ") + b.name)
            asm = box.row()
            asm.scale_y = 1.2
            asm.operator("foxbrowser.catalog_assemble", icon='OUTLINER_OB_ARMATURE')
        else:
            box.label(text="queue a base then parts, assemble in one click",
                      icon='INFO')


class FOXB_PT_howto(bpy.types.Panel):
    bl_idname = "FOXB_PT_howto"
    bl_label = "How to Assemble a Character"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "FoxBrowser"
    bl_options = {'DEFAULT_CLOSED'}

    def draw(self, _context):
        layout = self.layout
        col = layout.column(align=True)
        col.scale_y = 0.85
        for line in (
            "Fox Engine builds a character from a",
            "base body plus parts on one skeleton.",
            "",
            "1. Model Browser: search a base body",
            "   (bsm0/bsf0 Survive, skl0 MGO),",
            "   Import Selected.",
            "2. Keep its armature selected.",
            "3. Search each part and Add to Active",
            "   Character. Categories: Head (the",
            "   avatar face presets, av*_type*),",
            "   Headgear (helmets/masks/hats),",
            "   Arms, Legs, Body, Hair, Eyewear.",
            "4. Select the rig, Rewire Materials",
            "   for the full texture treatment.",
            "",
            "Tip: double-click a model to import",
            "it -- or, with a rig selected, to add",
            "it to that character.",
            "",
            "Ground Zeroes characters are single",
            "models: just Import Selected.",
        ):
            col.label(text=line)


classes = (
    FOXB_CatalogItem,
    FOXB_BuildItem,
    FOXB_UL_catalog,
    FOXB_OT_catalog_scan,
    FOXB_OT_catalog_click,
    FOXB_OT_catalog_import,
    FOXB_OT_catalog_add,
    FOXB_OT_catalog_queue,
    FOXB_OT_catalog_unqueue,
    FOXB_OT_catalog_assemble,
    FOXB_PT_catalog,
    FOXB_PT_howto,
)

_GAME_ITEMS = (
    ('ALL', "All games", ""),
    ("Metal Gear Survive", "Survive", ""),
    ("MGSV: The Phantom Pain / MGO", "TPP / MGO", ""),
    ("MGSV: Ground Zeroes", "Ground Zeroes", ""),
)

_GENDER_ITEMS = (
    ('ALL', "Any gender", ""),
    ("Male", "Male", ""),
    ("Female", "Female", ""),
    ("Unisex", "Unisex", ""),
)

# Category enum is rebuilt from whatever the scan found. Blender keeps no strong
# reference to enum item strings, so they are held here to avoid a crash.
_CATEGORY_ITEMS = [('ALL', "All categories", "")]


def _category_items(_self, _context):
    return _CATEGORY_ITEMS


def _rebuild_category_items(categories):
    global _CATEGORY_ITEMS
    _CATEGORY_ITEMS = [('ALL', "All categories", "")]
    for c in sorted(categories):
        _CATEGORY_ITEMS.append((c, c, ""))


def register_props():
    wm = bpy.types.WindowManager
    wm.foxb_cat_root = StringProperty(
        name="Models folder", subtype='DIR_PATH',
        description="Folder of ripped models to browse. Any folder works -- it "
                    "does not need to be called 'rips'; the scan walks it for "
                    "model FBXs")
    wm.foxb_cat_search = StringProperty(
        name="Search", description="Filter by name or description",
        options={'TEXTEDIT_UPDATE'})
    wm.foxb_cat_game = EnumProperty(name="Game", items=_GAME_ITEMS, default='ALL')
    wm.foxb_cat_category = EnumProperty(name="Category", items=_category_items)
    wm.foxb_cat_gender = EnumProperty(name="Gender", items=_GENDER_ITEMS,
                                      default='ALL')
    wm.foxb_cat_items = CollectionProperty(type=FOXB_CatalogItem)
    wm.foxb_cat_index = IntProperty(default=0)
    wm.foxb_build = CollectionProperty(type=FOXB_BuildItem)


def unregister_props():
    wm = bpy.types.WindowManager
    for attr in ("foxb_cat_root", "foxb_cat_search", "foxb_cat_game",
                 "foxb_cat_category", "foxb_cat_gender",
                 "foxb_cat_items", "foxb_cat_index", "foxb_build"):
        if hasattr(wm, attr):
            delattr(wm, attr)
