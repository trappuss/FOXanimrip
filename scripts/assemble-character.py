# SPDX-License-Identifier: GPL-3.0-or-later
"""
Assemble a full character from a base body and its parts, headless.

    blender --background --python assemble-character.py -- \
        --base   path/to/bsm0_main0_def.fbx \
        --part   path/to/hdm0_main0_def.fbx \
        --part   path/to/uam0_main0_def.fbx \
        --out    path/to/character.blend

Every --part is stacked onto the base's skeleton, the way the game builds a
created character. --base may be omitted, in which case the first --part is the
base. --out is optional; without it the assembled scene is left in memory (use
--fbx to write an FBX of the result instead of a .blend).

This is the same assembler the add-on's "Assemble Character" button uses, run
from the command line so a whole set of characters can be built in a loop.
"""

import os
import sys

import bpy

# Make the add-on package importable whether it is installed or sitting beside
# this script's repo (blender/io_foxbrowser).
_HERE = os.path.dirname(os.path.abspath(__file__))
for cand in (os.path.join(_HERE, "..", "blender"),
             os.path.join(_HERE, "blender")):
    cand = os.path.normpath(cand)
    if os.path.isdir(os.path.join(cand, "io_foxbrowser")):
        sys.path.insert(0, cand)
        break

try:
    from io_foxbrowser import assembler
except ImportError:
    sys.stderr.write(
        "could not import io_foxbrowser.assembler -- run this from the repo "
        "(scripts/ beside blender/io_foxbrowser) or with the add-on installed\n")
    raise


def parse_args(argv):
    if "--" in argv:
        argv = argv[argv.index("--") + 1:]
    else:
        argv = []
    base = None
    parts = []
    out = None
    fbx = None
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--base":
            i += 1; base = argv[i]
        elif a == "--part":
            i += 1; parts.append(argv[i])
        elif a == "--out":
            i += 1; out = argv[i]
        elif a == "--fbx":
            i += 1; fbx = argv[i]
        else:
            sys.stderr.write("ignoring unknown argument: %s\n" % a)
        i += 1
    return base, parts, out, fbx


def main():
    base, parts, out, fbx = parse_args(sys.argv)
    if not base and not parts:
        sys.stderr.write("nothing to assemble: pass --base and/or --part\n")
        sys.exit(2)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    name = os.path.splitext(os.path.basename(base or parts[0]))[0]
    coll = bpy.data.collections.new("Character_" + name.replace("_main0_def", ""))
    bpy.context.scene.collection.children.link(coll)

    def report(m):
        print("assemble:", m)

    result = assembler.assemble(base, parts, report=report, link_collection=coll)
    if result.armature is None:
        sys.stderr.write("assembly produced no armature\n")
        sys.exit(1)

    if fbx:
        for o in bpy.data.objects:
            o.select_set(o in ([result.armature] + result.meshes))
        bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True,
                                 add_leaf_bones=False)
        print("assemble: wrote", fbx)
    if out:
        bpy.ops.wm.save_as_mainfile(filepath=out)
        print("assemble: wrote", out)


if __name__ == "__main__":
    main()
