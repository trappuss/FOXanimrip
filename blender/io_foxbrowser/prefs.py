# SPDX-License-Identifier: GPL-3.0-or-later
"""Add-on preferences: the defaults every import operator starts from."""

from __future__ import annotations

import bpy
from bpy.props import BoolProperty, EnumProperty, FloatProperty, StringProperty

from . import materials

NORMAL_MODE_ITEMS = (
    ('DXT5NM', "Fox Engine (DXT5nm)",
     "X from the alpha channel, Y from green, Z reconstructed. "
     "This is how MGSV and Ground Zeroes store tangent-space normals"),
    ('RGB', "Direct RGB",
     "Feed the texture's RGB straight into a Normal Map node. "
     "Only correct if your export was converted to a standard normal map"),
    ('NONE', "Do not connect",
     "Load the normal map but leave it unlinked"),
)


def get(context=None):
    """The add-on preferences block, or ``None`` when running headless."""
    context = context or bpy.context
    try:
        return context.preferences.addons[__package__].preferences
    except (KeyError, AttributeError):
        return None


class FOXB_AddonPreferences(bpy.types.AddonPreferences):
    bl_idname = __package__

    parent_collection: StringProperty(
        name="Parent Collection",
        description="Bulk and recursive imports nest their per-model "
                    "collections under this one. Leave blank to put them at "
                    "the scene root",
        default="FoxBrowser Imports",
    )
    normal_mode: EnumProperty(
        name="Normal Maps",
        items=NORMAL_MODE_ITEMS,
        default='DXT5NM',
    )
    flip_green: BoolProperty(
        name="Flip Green Channel",
        description="Invert the Y axis of the normal map (DirectX-style green)",
        default=False,
    )
    normal_strength: FloatProperty(
        name="Normal Strength", default=1.0, min=0.0, soft_max=2.0,
    )
    srm_red: EnumProperty(
        name="SRM Red",
        description="What the red channel of a _srm map drives",
        items=materials.ROLE_ITEMS, default='SPECULAR',
    )
    srm_green: EnumProperty(
        name="SRM Green",
        description="What the green channel of a _srm map drives",
        items=materials.ROLE_ITEMS, default='ROUGHNESS',
    )
    srm_blue: EnumProperty(
        name="SRM Blue",
        description="What the blue channel of a _srm map drives",
        items=materials.ROLE_ITEMS, default='NONE',
    )
    fuzzy_texture_match: BoolProperty(
        name="Fuzzy Texture Matching",
        description="When no exactly-named sibling map exists, fall back to "
                    "the longest unique shared prefix. Guesses are listed in "
                    "the import log",
        default=True,
    )

    def draw(self, _context):
        layout = self.layout

        box = layout.box()
        box.label(text="Scene", icon='OUTLINER_COLLECTION')
        box.prop(self, "parent_collection")

        box = layout.box()
        box.label(text="Normal Maps", icon='NORMALS_VERTEX')
        box.prop(self, "normal_mode", text="")
        row = box.row(align=True)
        row.enabled = self.normal_mode != 'NONE'
        row.prop(self, "normal_strength")
        row.prop(self, "flip_green", toggle=True)
        col = box.column()
        col.scale_y = 0.8
        col.label(text="Fox Engine packs tangent normals into DXT5 as "
                       "X=alpha, Y=green; RGB is a dummy.")

        box = layout.box()
        box.label(text="Specular / Roughness Map (_srm)", icon='SHADING_RENDERED')
        split = box.split(factor=0.34)
        split.column().label(text="Red")
        split.column().prop(self, "srm_red", text="")
        split = box.split(factor=0.34)
        split.column().label(text="Green")
        split.column().prop(self, "srm_green", text="")
        split = box.split(factor=0.34)
        split.column().label(text="Blue")
        split.column().prop(self, "srm_blue", text="")
        col = box.column()
        col.scale_y = 0.8
        col.label(text="Measured on FoxBrowser exports: red tracks specular "
                       "(0 on alpha cards),")
        col.label(text="green tracks microsurface (0.13 eyes / 0.70 skin / "
                       "0.87 fabric). Flip if yours differ.")

        box = layout.box()
        box.label(text="Texture Discovery", icon='VIEWZOOM')
        box.prop(self, "fuzzy_texture_match")


def apply_defaults(operator, context):
    """Seed an import operator's material settings from the preferences."""
    prefs = get(context)
    if prefs is None:
        return
    for name in ("normal_mode", "flip_green", "normal_strength",
                 "srm_red", "srm_green", "srm_blue", "fuzzy_texture_match"):
        if hasattr(operator, name):
            setattr(operator, name, getattr(prefs, name))
    if hasattr(operator, "parent_collection"):
        operator.parent_collection = prefs.parent_collection


classes = (FOXB_AddonPreferences,)
