# SPDX-License-Identifier: GPL-3.0-or-later
"""
FoxBrowser Import -- Blender add-on for FoxBrowser's Fox Engine model exports
(Metal Gear Solid V: The Phantom Pain / Ground Zeroes).

Handles a single model, a folder of them, or a whole extracted archive tree,
and puts back the parts the FBX loses on its own: Fox Engine's DXT5nm normal
packing, the _srm/_trm/_lym maps the FBX never references, and the bone hashes
and rig-unit grouping from the _rig.json sidecar.
"""

bl_info = {
    "name": "FoxBrowser Import (MGSV / Ground Zeroes)",
    "author": "minmaxmaxminnning",
    "version": (1, 5, 0),
    "blender": (4, 2, 0),
    "location": "File > Import > FoxBrowser, and View3D > Sidebar > FoxBrowser",
    "description": "Import FoxBrowser FMDL exports and bulk animation libraries",
    "category": "Import-Export",
    "doc_url": "https://www.nexusmods.com/metalgearsolidvtpp/mods/2531",
}

import bpy

from . import actions, browser, operators, prefs, ui

_MODULES = (prefs, operators, actions, browser, ui)


def register():
    for module in _MODULES:
        for cls in module.classes:
            bpy.utils.register_class(cls)
    actions.register_props()
    browser.register_props()
    ui.register_menus()


def unregister():
    ui.unregister_menus()
    browser.unregister_props()
    actions.unregister_props()
    for module in reversed(_MODULES):
        for cls in reversed(module.classes):
            try:
                bpy.utils.unregister_class(cls)
            except RuntimeError:
                pass


if __name__ == "__main__":
    register()
