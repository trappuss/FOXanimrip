# SPDX-License-Identifier: GPL-3.0-or-later
"""
A tiny read-only FBX binary parser, used for one job: recovering the exact
material -> texture links FoxBrowser wrote.

Blender's FBX importer does build image nodes, but which FBX texture slot ends
up on which Principled input has changed between releases, and FoxBrowser puts
the normal map on ``NormalMap`` on some materials and ``Bump`` on others.
Reading the connections straight out of the file removes the guesswork; if
anything at all goes wrong we return ``None`` and the material builder falls
back to inspecting the nodes Blender created.

Only node headers, string/scalar properties and the ``Connections`` table are
decoded.  Vertex arrays are skipped without decompressing, so this is fast even
on large exports.
"""

from __future__ import annotations

import os
import struct
import zlib

_ARRAY_TYPES = {
    b"f"[0]: ("f", 4), b"d"[0]: ("d", 8), b"l"[0]: ("q", 8),
    b"i"[0]: ("i", 4), b"b"[0]: ("B", 1), b"c"[0]: ("B", 1),
}
_MAGIC = b"Kaydara FBX Binary  \x00"

#: FBX material property names that mean "this is the colour map".
BASE_SLOTS = ("DiffuseColor", "Diffuse", "BaseColor", "TransparentColor",
              "DiffuseFactor")
#: ...and the normal map.
NORMAL_SLOTS = ("NormalMap", "Bump", "Normal", "BumpFactor")
#: ...and the spec/roughness map.
SPEC_SLOTS = ("SpecularColor", "Specular", "SpecularFactor", "ShininessExponent",
              "ReflectionColor")


class _Reader:
    __slots__ = ("buf", "pos")

    def __init__(self, buf):
        self.buf = buf
        self.pos = 0

    def unpack(self, fmt, size):
        value = struct.unpack_from(fmt, self.buf, self.pos)[0]
        self.pos += size
        return value


def _read_property(r: _Reader, want_arrays: bool):
    kind = r.buf[r.pos]
    r.pos += 1
    if kind == 0x59:      # Y int16
        return r.unpack("<h", 2)
    if kind == 0x43:      # C bool
        return bool(r.unpack("<B", 1))
    if kind == 0x49:      # I int32
        return r.unpack("<i", 4)
    if kind == 0x46:      # F float32
        return r.unpack("<f", 4)
    if kind == 0x44:      # D float64
        return r.unpack("<d", 8)
    if kind == 0x4C:      # L int64
        return r.unpack("<q", 8)
    if kind in (0x53, 0x52):  # S string, R raw
        n = r.unpack("<I", 4)
        raw = r.buf[r.pos:r.pos + n]
        r.pos += n
        if kind == 0x53:
            return raw.decode("utf-8", "replace")
        return raw
    if kind in _ARRAY_TYPES:
        length = r.unpack("<I", 4)
        encoding = r.unpack("<I", 4)
        comp_len = r.unpack("<I", 4)
        data = r.buf[r.pos:r.pos + comp_len]
        r.pos += comp_len
        if not want_arrays:
            return None
        if encoding == 1:
            data = zlib.decompress(data)
        fmt, size = _ARRAY_TYPES[kind]
        return list(struct.unpack("<%d%s" % (length, fmt), data[:length * size]))
    raise ValueError("unknown FBX property type 0x%02x" % kind)


def _read_node(r: _Reader, version: int, want_arrays: bool):
    if version >= 7500:
        end = r.unpack("<Q", 8)
        num_props = r.unpack("<Q", 8)
        r.unpack("<Q", 8)
    else:
        end = r.unpack("<I", 4)
        num_props = r.unpack("<I", 4)
        r.unpack("<I", 4)
    name_len = r.unpack("<B", 1)
    if end == 0:
        return None
    name = r.buf[r.pos:r.pos + name_len].decode("utf-8", "replace")
    r.pos += name_len
    props = [_read_property(r, want_arrays) for _ in range(num_props)]
    children = []
    while r.pos < end:
        child = _read_node(r, version, want_arrays)
        if child is None:
            break
        children.append(child)
    r.pos = end
    return (name, props, children)


def _split_name(value):
    """FBX object names are ``"name\\x00\\x01Class"``."""
    if not isinstance(value, str):
        return ""
    return value.split("\x00\x01", 1)[0]


def read_links(fbx_path: str):
    """Return ``{material_name: {slot_name: texture_base_name, ...}, ...}``.

    ``texture_base_name`` is the file's base name without directory or
    extension, matching :mod:`.naming`.  Returns ``None`` if the file is not a
    binary FBX or anything else goes wrong.
    """
    try:
        with open(fbx_path, "rb") as fh:
            buf = fh.read()
    except OSError:
        return None

    if not buf.startswith(_MAGIC):
        return None  # ASCII FBX or something else entirely

    try:
        r = _Reader(buf)
        r.pos = 23
        version = r.unpack("<I", 4)
        roots = []
        while True:
            node = _read_node(r, version, want_arrays=False)
            if node is None:
                break
            roots.append(node)
    except Exception:
        return None

    objects = None
    connections = None
    for name, _props, children in roots:
        if name == "Objects":
            objects = children
        elif name == "Connections":
            connections = children
    if objects is None or connections is None:
        return None

    materials = {}
    textures = {}
    for name, props, children in objects:
        if not props:
            continue
        uid = props[0]
        if name == "Material":
            materials[uid] = _split_name(props[1] if len(props) > 1 else "")
        elif name == "Texture":
            filename = ""
            for cname, cprops, _c in children:
                if cname in ("RelativeFilename", "FileName") and cprops:
                    candidate = cprops[0]
                    if isinstance(candidate, str) and candidate:
                        filename = candidate
                        if cname == "RelativeFilename":
                            break
            if filename:
                base = os.path.splitext(os.path.basename(
                    filename.replace("\\", "/")))[0]
            else:
                base = _split_name(props[1] if len(props) > 1 else "")
            textures[uid] = base

    result = {}
    for name, props, _children in connections:
        if name != "C" or len(props) < 4:
            continue
        if props[0] != "OP":
            continue
        child_id, parent_id, slot = props[1], props[2], props[3]
        if child_id in textures and parent_id in materials:
            mat = materials[parent_id]
            if not mat:
                continue
            result.setdefault(mat, {})[str(slot)] = textures[child_id]

    return result or None


def classify(slots: dict):
    """Sort one material's ``{slot: texture}`` dict into base/normal/spec/other."""
    base = normal = spec = None
    other = []
    for slot, texture in slots.items():
        if base is None and slot in BASE_SLOTS:
            base = texture
        elif normal is None and slot in NORMAL_SLOTS:
            normal = texture
        elif spec is None and slot in SPEC_SLOTS:
            spec = texture
        else:
            other.append(texture)
    return base, normal, spec, other
