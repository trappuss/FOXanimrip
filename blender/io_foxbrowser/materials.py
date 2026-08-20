# SPDX-License-Identifier: GPL-3.0-or-later
"""Rebuilding imported materials as Principled BSDF trees wired to Fox Engine maps."""

from __future__ import annotations

import os
import re

import bpy

from . import fbxlink, naming, nodes

_DOT_SUFFIX = re.compile(r"\.\d{3}$")

#: Extra map codes we try to locate for every material once the FBX-declared
#: base/normal maps are known.
EXTRA_CODES = (naming.CODE_SPECROUGH, "trm", "ilm", "lym", "mtl", "occ")

#: Roles an SRM channel can be routed to.
ROLE_ITEMS = (
    ('NONE', "Unused", "Leave this channel unconnected"),
    ('ROUGHNESS', "Roughness", "Route straight to Roughness"),
    ('ROUGHNESS_INV', "Roughness (inverted)",
     "Treat the channel as smoothness and invert it into Roughness"),
    ('SPECULAR', "Specular IOR Level", "Route to the Principled specular level"),
    ('METALLIC', "Metallic", "Route to Metallic"),
    ('EMISSION', "Emission Strength", "Route to Emission Strength"),
)

_CHANNEL_NAMES = ("Red", "Green", "Blue")


def strip_dot_suffix(name: str) -> str:
    """``body1.001`` -> ``body1`` (Blender's collision rename)."""
    return _DOT_SUFFIX.sub("", name)


def iter_materials(objects):
    """Unique materials across *objects*, in a stable order."""
    seen = []
    for obj in objects:
        if obj.type != 'MESH':
            continue
        for slot in obj.material_slots:
            mat = slot.material
            if mat is not None and mat not in seen:
                seen.append(mat)
    return seen


def _load_image(path, non_color, channel_packed, report):
    try:
        image = bpy.data.images.load(path, check_existing=True)
    except RuntimeError as exc:
        report.warn("could not load %s (%s)" % (os.path.basename(path), exc))
        return None
    try:
        image.colorspace_settings.name = 'Non-Color' if non_color else 'sRGB'
    except Exception:
        pass
    # A DXT5nm normal map keeps X in alpha; premultiplying would destroy it.
    image.alpha_mode = 'CHANNEL_PACKED' if channel_packed else 'STRAIGHT'
    return image


def _set_blend(mat, blended):
    """Set alpha blending in a way that works on 4.2 EEVEE Next and earlier."""
    if hasattr(mat, "surface_render_method"):
        mat.surface_render_method = 'BLENDED' if blended else 'DITHERED'
    if hasattr(mat, "blend_method"):
        try:
            mat.blend_method = 'BLEND' if blended else 'OPAQUE'
        except TypeError:
            pass
    if blended:
        for attr in ("use_transparent_shadow", "show_transparent_back"):
            if hasattr(mat, attr):
                try:
                    setattr(mat, attr, True)
                except Exception:
                    pass


def _collect_from_existing_nodes(mat):
    """Fallback: read whatever image names the FBX importer already wired up."""
    base = normal = None
    others = []
    if not mat.use_nodes or mat.node_tree is None:
        return base, normal, None, others

    for node in mat.node_tree.nodes:
        if node.type != 'TEX_IMAGE' or node.image is None:
            continue
        name = os.path.splitext(os.path.basename(node.image.filepath))[0]
        if not name:
            name = strip_dot_suffix(node.image.name)
            name = os.path.splitext(name)[0]
        parsed = naming.parse(name)
        if parsed.code == naming.CODE_BASE and base is None:
            base = name
        elif parsed.is_normal and normal is None:
            normal = name
        else:
            others.append(name)
    return base, normal, None, others


class MaterialBuilder:
    """Builds one Principled tree per material for a single export set."""

    def __init__(self, export_set, opts, report):
        self.es = export_set
        self.opts = opts
        self.report = report
        self.links = None
        if export_set.extension == ".fbx":
            self.links = fbxlink.read_links(export_set.model_path)
            if self.links is None:
                report.info("%s: could not read FBX material links, "
                            "falling back to imported nodes" % export_set.name)

    # -- lookup -----------------------------------------------------------

    def _slots_for(self, mat):
        if not self.links:
            return None
        for key in (mat.name, strip_dot_suffix(mat.name)):
            if key in self.links:
                return self.links[key]
        return None

    def _resolve(self, stems, code):
        """``(base, path)`` for a map of type *code*, or ``(None, None)``."""
        hit = self.es.find_texture(stems, code,
                                   fuzzy_fallback=self.opts.fuzzy_texture_match)
        if hit is None:
            return None, None
        base, path, exact = hit
        if not exact:
            self.report.info("guessed %s for %s (no exact name match)"
                             % (base, "/".join(stems) or "?"))
        return base, path

    # -- building ---------------------------------------------------------

    def build(self, mat):
        slots = self._slots_for(mat)
        if slots is not None:
            base_name, normal_name, spec_name, other_names = fbxlink.classify(slots)
        else:
            base_name, normal_name, spec_name, other_names = \
                _collect_from_existing_nodes(mat)

        stems = []
        for name in (base_name, normal_name, spec_name):
            if not name:
                continue
            stem = naming.parse(name).stem
            if stem and stem not in stems:
                stems.append(stem)
        if not stems:
            for name in other_names:
                stem = naming.parse(name).stem
                if stem and stem not in stems:
                    stems.append(stem)
        if not stems:
            self.report.warn("%s: no textures referenced, left untouched" % mat.name)
            return

        exact_index = self.es.texture_index()[0]

        found = {}          # code -> (base_name, path)
        if base_name and base_name in exact_index:
            found[naming.CODE_BASE] = (base_name, exact_index[base_name])
        elif base_name:
            b, p = self._resolve(stems, naming.CODE_BASE)
            if b:
                found[naming.CODE_BASE] = (b, p)

        if normal_name:
            code = naming.parse(normal_name).code or naming.CODE_NORMAL
            if normal_name in exact_index:
                found[code] = (normal_name, exact_index[normal_name])
        if not any(c in found for c in naming.NORMAL_CODES):
            for code in naming.NORMAL_CODES:
                b, p = self._resolve(stems, code)
                if b:
                    found[code] = (b, p)
                    break

        # The tool's role sidecar: when a material's textures are hash-named the
        # spec map (never in the FBX) and a hash-named normal cannot be found by
        # suffix, so look them up by the material's base file, which the sidecar
        # keys on. This is what makes srm load and normals resolve on Survive
        # gear built from shared, unresolved textures.
        sidecar = self.es.map_sidecar()

        # A hash-named base colour (an unresolved face / skin texture) carries no
        # _bsm suffix, so classify() cannot tag it as the base and the material
        # would import untextured -- this is why some avatar faces come in bare.
        # But the tool recorded that texture as a base row in the sidecar. When we
        # still have no base, adopt the material's own texture that the sidecar
        # knows as a base: real extracted image data, just nameless. Its paired
        # normal/spec then wire through the block below.
        if naming.CODE_BASE not in found:
            for cand in other_names:
                stem = naming.parse(cand).stem or cand
                if stem in sidecar and stem in exact_index:
                    found[naming.CODE_BASE] = (stem, exact_index[stem])
                    self.report.info("%s: base colour from sidecar (%s)"
                                     % (mat.name, stem))
                    break

        base_key = found.get(naming.CODE_BASE, (base_name, None))[0]
        if base_key and base_key in sidecar:
            normal_stem, spec_stem = sidecar[base_key]
            if normal_stem and not any(c in found for c in naming.NORMAL_CODES) \
                    and normal_stem in exact_index:
                found[naming.CODE_NORMAL] = (normal_stem, exact_index[normal_stem])
            if spec_stem and naming.CODE_SPECROUGH not in found \
                    and spec_stem in exact_index:
                found[naming.CODE_SPECROUGH] = (spec_stem, exact_index[spec_stem])

        if self.opts.wire_extra_maps:
            for code in EXTRA_CODES:
                if code in found:
                    continue
                b, p = self._resolve(stems, code)
                if b:
                    found[code] = (b, p)

        if not found:
            self.report.warn("%s: no texture files found on disk" % mat.name)
            return

        self._make_tree(mat, found)

    def _make_tree(self, mat, found):
        opts = self.opts
        mat.use_nodes = True
        tree = mat.node_tree
        tree.nodes.clear()

        output = tree.nodes.new("ShaderNodeOutputMaterial")
        output.location = (600, 0)
        bsdf = tree.nodes.new("ShaderNodeBsdfPrincipled")
        bsdf.location = (260, 0)
        tree.links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])

        y = 400
        unconnected = []

        for code in sorted(found, key=lambda c: (c != naming.CODE_BASE, c)):
            base_name, path = found[code]
            parsed = naming.parse(base_name)
            is_normal = code in naming.NORMAL_CODES
            # Colour space follows the map's ROLE (the dict key), not the file
            # name. A normal or spec map whose file is hash-named -- as many
            # Survive textures are, when the source path was unresolved -- has no
            # _nrm/_srm suffix, so parsing the name alone would load it as sRGB
            # and wreck the normals. The code is what we actually filed it under.
            non_color = parsed.is_non_color or code in naming.NON_COLOR_CODES or is_normal
            image = _load_image(path, non_color, is_normal, self.report)
            if image is None:
                continue

            tex = tree.nodes.new("ShaderNodeTexImage")
            tex.image = image
            tex.location = (-720, y)
            tex.label = "%s  (%s)" % (base_name, parsed.label)
            tex.name = "fox_%s" % code
            y -= 320

            if code == naming.CODE_BASE:
                tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
                if parsed.has_alpha and opts.use_alpha:
                    alpha_in = nodes.find_input(bsdf, "Alpha")
                    if alpha_in is not None:
                        tree.links.new(tex.outputs["Alpha"], alpha_in)
                    _set_blend(mat, True)
                    mat.use_backface_culling = False
                else:
                    _set_blend(mat, False)
                    mat.use_backface_culling = opts.backface_culling

            elif is_normal:
                if opts.normal_mode == 'NONE':
                    unconnected.append(tex)
                elif opts.normal_mode == 'RGB':
                    nmap = tree.nodes.new("ShaderNodeNormalMap")
                    nmap.location = (-380, tex.location[1])
                    tree.links.new(tex.outputs["Color"], nmap.inputs["Color"])
                    tree.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
                else:
                    group = tree.nodes.new("ShaderNodeGroup")
                    group.node_tree = nodes.ensure_normal_group()
                    group.location = (-380, tex.location[1])
                    group.width = 200
                    tree.links.new(tex.outputs["Color"], group.inputs["Color"])
                    tree.links.new(tex.outputs["Alpha"], group.inputs["Alpha"])
                    group.inputs["Strength"].default_value = opts.normal_strength
                    group.inputs["Y Sign"].default_value = (
                        -1.0 if opts.flip_green else 1.0)
                    tree.links.new(group.outputs["Normal"], bsdf.inputs["Normal"])

            elif code == naming.CODE_SPECROUGH:
                self._wire_srm(tree, bsdf, tex)

            else:
                unconnected.append(tex)

        for tex in unconnected:
            tex.location = (-720, y)
            y -= 320
            tex.label += "  [not wired]"

        if opts.material_prefix and not mat.name.startswith(self.es.name + "__"):
            mat.name = "%s__%s" % (self.es.name, strip_dot_suffix(mat.name))

    def _wire_srm(self, tree, bsdf, tex):
        """Split an SRM into its channel roles.

        Verified on the FoxBrowser sna2 export: R rises with hard, shiny
        surfaces and sits at 0 on alpha-card eyelashes (specular), G tracks
        microsurface -- 0.13 on eyes, 0.70 on skin, 0.87 on fabric (roughness).
        Blue was constant or zero on every map sampled, so it stays unused.
        Both are remappable in the add-on preferences.
        """
        opts = self.opts
        roles = (opts.srm_red, opts.srm_green, opts.srm_blue)
        if all(role == 'NONE' for role in roles):
            tex.label += "  [not wired]"
            return

        sep = tree.nodes.new("ShaderNodeSeparateColor")
        sep.location = (-380, tex.location[1])
        tree.links.new(tex.outputs["Color"], sep.inputs["Color"])

        used = False
        for index, role in enumerate(roles):
            if role == 'NONE':
                continue
            socket = sep.outputs[_CHANNEL_NAMES[index]]
            if role == 'ROUGHNESS_INV':
                inv = tree.nodes.new("ShaderNodeMath")
                inv.operation = 'SUBTRACT'
                inv.inputs[0].default_value = 1.0
                inv.location = (-200, tex.location[1] - 60 * index)
                inv.label = "smoothness -> roughness"
                tree.links.new(socket, inv.inputs[1])
                socket = inv.outputs[0]
                target = nodes.find_input(bsdf, "Roughness")
            elif role == 'ROUGHNESS':
                target = nodes.find_input(bsdf, "Roughness")
            elif role == 'SPECULAR':
                target = nodes.find_input(bsdf, "Specular IOR Level", "Specular")
            elif role == 'METALLIC':
                target = nodes.find_input(bsdf, "Metallic")
            elif role == 'EMISSION':
                target = nodes.find_input(bsdf, "Emission Strength")
            else:
                target = None
            if target is not None:
                tree.links.new(socket, target)
                used = True

        if not used:
            tree.nodes.remove(sep)
            tex.label += "  [not wired]"


def rebuild(export_set, objects, opts, report):
    """Rebuild every material used by *objects*.  Returns the count."""
    builder = MaterialBuilder(export_set, opts, report)
    count = 0
    for mat in iter_materials(objects):
        try:
            builder.build(mat)
            count += 1
        except Exception as exc:                      # never kill a batch run
            report.warn("material %s failed: %s" % (mat.name, exc))
    return count
