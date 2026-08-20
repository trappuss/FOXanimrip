# SPDX-License-Identifier: GPL-3.0-or-later
"""
Repairing FoxBrowser's animation blocks so Blender can read them.

FoxBrowser tags its animation objects with the class tokens
``AnimationStack`` and ``AnimationLayer``.  The FBX convention -- and what the
Autodesk SDK, Maya and Blender all expect -- is ``AnimStack`` and ``AnimLayer``;
the long spelling is only used for the *Definitions* object-type name.  Blender
4.x hits this in ``import_fbx.blen_read_animations``::

    stack_name = elem_name_ensure_class(fbx_asdata, b'AnimStack')
    ...
    assert(elem_class == clss)          # AssertionError

which aborts the import part-way through, leaving a half-built scene and no
animation.  It is the exporter's bug, not Blender's, and it affects every
FoxBrowser FBX that carries a clip.

Rather than silently dropping the animation, this module rewrites the two
offending strings into a temporary copy of the file and imports that instead.
The rewrite is a full re-serialisation: FBX node headers store *absolute* end
offsets, so shortening a string by five bytes means every following offset has
to move.  Property payloads (including compressed vertex arrays) are copied
byte for byte and never re-encoded, so nothing but those two strings changes.
"""

from __future__ import annotations

import os
import struct
import tempfile

_MAGIC = b"Kaydara FBX Binary  \x00"

#: ``bad class token`` -> ``what every other FBX consumer expects``.
CLASS_FIXES = (
    (b"\x00\x01AnimationStack", b"\x00\x01AnimStack"),
    (b"\x00\x01AnimationLayer", b"\x00\x01AnimLayer"),
)

_SCALARS = {0x59: 2, 0x43: 1, 0x49: 4, 0x46: 4, 0x44: 8, 0x4C: 8}
_ARRAYS = frozenset((0x66, 0x64, 0x6C, 0x69, 0x62, 0x63))


class _Node:
    __slots__ = ("name", "num_props", "props", "children", "terminated")

    def __init__(self, name, num_props, props, children, terminated):
        self.name = name
        self.num_props = num_props
        self.props = props          # list of raw byte strings, type byte included
        self.children = children
        self.terminated = terminated


def needs_fix(path: str) -> bool:
    """Cheap check: does this file use the non-standard animation classes?"""
    try:
        with open(path, "rb") as fh:
            data = fh.read()
    except OSError:
        return False
    if not data.startswith(_MAGIC):
        return False
    return any(bad in data for bad, _good in CLASS_FIXES)


# -- reading --------------------------------------------------------------

def _split_props(buf, start, count, end):
    """Slice a property block into raw per-property byte strings."""
    props = []
    pos = start
    for _ in range(count):
        if pos >= end:
            break
        kind = buf[pos]
        begin = pos
        pos += 1
        if kind in _SCALARS:
            pos += _SCALARS[kind]
        elif kind in (0x53, 0x52):
            length = struct.unpack_from("<I", buf, pos)[0]
            pos += 4 + length
        elif kind in _ARRAYS:
            comp_len = struct.unpack_from("<I", buf, pos + 8)[0]
            pos += 12 + comp_len
        else:
            raise ValueError("unknown FBX property type 0x%02x" % kind)
        props.append(bytes(buf[begin:pos]))
    return props, pos


def _read_node(buf, pos, version):
    header = 25 if version >= 7500 else 13
    if version >= 7500:
        end, num_props, prop_len = struct.unpack_from("<QQQ", buf, pos)
    else:
        end, num_props, prop_len = struct.unpack_from("<III", buf, pos)
    name_len = buf[pos + header - 1]
    if end == 0:
        return None, pos + header

    name_start = pos + header
    name = bytes(buf[name_start:name_start + name_len])
    props_start = name_start + name_len
    props, _ = _split_props(buf, props_start, num_props, props_start + prop_len)

    children = []
    terminated = False
    cursor = props_start + prop_len
    while cursor < end:
        child, cursor = _read_node(buf, cursor, version)
        if child is None:
            terminated = True
            break
        children.append(child)

    return _Node(name, num_props, props, children, terminated), end


def _parse(path):
    with open(path, "rb") as fh:
        buf = fh.read()
    if not buf.startswith(_MAGIC):
        raise ValueError("not a binary FBX")
    version = struct.unpack_from("<I", buf, 23)[0]
    pos = 27
    roots = []
    while True:
        node, pos = _read_node(buf, pos, version)
        if node is None:
            break
        roots.append(node)
    return version, roots, buf, pos


# -- writing --------------------------------------------------------------

def _write_node(out, node, version):
    header = 25 if version >= 7500 else 13
    start = len(out)
    out += b"\x00" * header
    out += node.name
    props_start = len(out)
    for prop in node.props:
        out += prop
    prop_len = len(out) - props_start
    for child in node.children:
        _write_node(out, child, version)
    if node.terminated:
        out += b"\x00" * header
    end = len(out)
    if version >= 7500:
        struct.pack_into("<QQQ", out, start, end, node.num_props, prop_len)
    else:
        struct.pack_into("<III", out, start, end, node.num_props, prop_len)
    out[start + header - 1] = len(node.name)


# -- the fix --------------------------------------------------------------

def _fix_props(node):
    """Rewrite the class token in this node's name property.  Returns a count."""
    fixed = 0
    for index, prop in enumerate(node.props):
        if not prop or prop[0] != 0x53:          # only 'S' string properties
            continue
        length = struct.unpack_from("<I", prop, 1)[0]
        value = prop[5:5 + length]
        for bad, good in CLASS_FIXES:
            if value.endswith(bad):
                value = value[:-len(bad)] + good
                node.props[index] = (b"S" + struct.pack("<I", len(value)) + value)
                fixed += 1
                break
    return fixed


def _walk_fix(node):
    fixed = _fix_props(node)
    for child in node.children:
        fixed += _walk_fix(child)
    return fixed


def write_fixed(src: str, directory: str = ""):
    """Write a repaired copy of *src* and return ``(path, fixes)``.

    *directory* is where the temporary file goes; it defaults to the source
    folder so the FBX's relative texture paths still resolve.  Falls back to the
    system temp folder when the source folder is not writable.  Raises on a
    malformed file, so callers should guard.
    """
    version, roots, _buf, tail_start = _parse(src)

    fixes = 0
    for root in roots:
        fixes += _walk_fix(root)
    if not fixes:
        return "", 0

    out = bytearray()
    out += _MAGIC
    out += b"\x1a\x00"
    out += struct.pack("<I", version)
    for root in roots:
        _write_node(out, root, version)
    out += b"\x00" * (25 if version >= 7500 else 13)

    # The footer carries no offsets, so it travels verbatim.
    with open(src, "rb") as fh:
        fh.seek(tail_start)
        out += fh.read()

    directory = directory or os.path.dirname(src)
    base = os.path.splitext(os.path.basename(src))[0]
    for folder in (directory, tempfile.gettempdir()):
        try:
            handle, path = tempfile.mkstemp(prefix=base + ".foxfix.",
                                            suffix=".fbx", dir=folder)
            with os.fdopen(handle, "wb") as fh:
                fh.write(out)
            return path, fixes
        except OSError:
            continue
    raise OSError("could not write a repaired copy of %s" % src)
