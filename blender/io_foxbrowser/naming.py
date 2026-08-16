# SPDX-License-Identifier: GPL-3.0-or-later
"""
Fox Engine texture naming conventions.

FoxBrowser writes textures as ``<stem>_<code>[_alp].dds`` next to the model in a
``<model>_textures`` folder.  Only ``bsm`` and ``nrm``/``hnm`` are actually wired
into the exported FBX; everything else has to be discovered by name.

Verified against the FoxBrowser ``sna2_main0_def`` export (MGSV / Ground Zeroes):

==========  ========  ====================================================
code        format    meaning
==========  ========  ====================================================
``bsm``     DXT1/5    Base Surface Map -> Base Color (sRGB).
``nrm``     DXT5      Normal map, DXT5nm packing: X in ALPHA, Y in GREEN,
                      RGB colour block is a constant dummy.  Z is derived.
``hnm``     DXT5      Second normal map, identical packing to ``nrm``.
                      Used instead of ``nrm`` on some materials.
``srm``     DXT1      R = specular reflectance, G = roughness, B = unused.
``trm``     ?         Translucency / transmission mask.
``ilm``     ?         Illumination (emissive) mask.
``lym``     ?         Layer mask (multi-material blend weights).
``mtl``     ?         Material / metalness mask.
==========  ========  ====================================================

``_alp`` appended after the code means the map carries a meaningful alpha
channel (cutout hair, eyelashes, patches).
"""

from __future__ import annotations

import re

# Codes we understand well enough to wire automatically.
CODE_BASE = "bsm"
CODE_NORMAL = "nrm"
CODE_NORMAL2 = "hnm"
CODE_SPECROUGH = "srm"

#: Every suffix we recognise as a texture *type* code.  Order matters only for
#: display; lookup is dict based.
KNOWN_CODES = {
    CODE_BASE: "Base Colour",
    CODE_NORMAL: "Normal",
    CODE_NORMAL2: "Normal (secondary)",
    CODE_SPECROUGH: "Specular / Roughness",
    "trm": "Translucency",
    "ilm": "Illumination / Emissive",
    "lym": "Layer mask",
    "mtl": "Material mask",
    "dfm": "Deformation mask",
    "occ": "Ambient occlusion",
    "msk": "Mask",
    "vlm": "Volume / thickness",
    "gzm": "Gaze mask",
}

#: Codes that should be loaded as Non-Color data rather than sRGB.
NON_COLOR_CODES = {
    CODE_NORMAL,
    CODE_NORMAL2,
    CODE_SPECROUGH,
    "trm",
    "lym",
    "mtl",
    "dfm",
    "occ",
    "msk",
    "vlm",
    "gzm",
}

#: Codes that are normal maps using the Fox Engine DXT5nm packing.
NORMAL_CODES = (CODE_NORMAL, CODE_NORMAL2)

_ALPHA_TAIL = "_alp"
_DIGITS = re.compile(r"\d+")


class TextureName:
    """A parsed Fox Engine texture base name (no directory, no extension)."""

    __slots__ = ("raw", "stem", "code", "has_alpha")

    def __init__(self, raw: str, stem: str, code: str, has_alpha: bool):
        self.raw = raw
        self.stem = stem
        self.code = code
        self.has_alpha = has_alpha

    def __repr__(self):  # pragma: no cover - debugging aid
        return "<TextureName %s stem=%s code=%s alpha=%s>" % (
            self.raw, self.stem, self.code, self.has_alpha)

    @property
    def is_normal(self) -> bool:
        return self.code in NORMAL_CODES

    @property
    def is_non_color(self) -> bool:
        return self.code in NON_COLOR_CODES

    @property
    def label(self) -> str:
        return KNOWN_CODES.get(self.code, "Unknown (%s)" % self.code)


def parse(name: str) -> TextureName:
    """Split ``sna2_hair0_def_bsm_alp`` into stem/code/alpha-flag.

    Unrecognised names come back with ``code == ''`` and the whole name as the
    stem, which keeps every downstream lookup harmless.
    """
    base = name
    has_alpha = False
    if base.lower().endswith(_ALPHA_TAIL):
        has_alpha = True
        base = base[: -len(_ALPHA_TAIL)]

    head, sep, tail = base.rpartition("_")
    tail_l = tail.lower()
    if sep and tail_l in KNOWN_CODES:
        return TextureName(name, head, tail_l, has_alpha)

    # Not a code we know.  Treat a 3-letter tail as a code anyway so unknown
    # Fox Engine suffixes still group with their siblings, but never invent a
    # code out of a name that has no underscore at all.
    if sep and len(tail_l) == 3 and tail_l.isalpha():
        return TextureName(name, head, tail_l, has_alpha)

    return TextureName(name, base, "", has_alpha)


def normalise(stem: str) -> str:
    """Digit-insensitive form of a stem.

    ``sna0_cnt1_def`` and ``sna0_cnt2_def`` both normalise to ``sna0_cnt#_def``.
    Fox Engine routinely numbers the base map and the normal map differently
    for the same material, so this is how sibling maps get found.
    """
    return _DIGITS.sub("#", stem.lower())


def shorten(stem: str) -> str:
    """Drop the last underscore-separated token, or return '' when there is none."""
    head, sep, _tail = stem.rpartition("_")
    return head if sep else ""
