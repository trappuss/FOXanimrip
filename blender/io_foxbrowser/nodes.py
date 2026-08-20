# SPDX-License-Identifier: GPL-3.0-or-later
"""Reusable shader node groups."""

from __future__ import annotations

import bpy

NORMAL_GROUP_NAME = "FoxEngine Normal (DXT5nm)"


def _new_socket(tree, name, in_out, socket_type):
    """Create an interface socket (Blender 4.0+ interface API)."""
    return tree.interface.new_socket(name=name, in_out=in_out,
                                     socket_type=socket_type)


def ensure_normal_group():
    """Node group that unpacks a Fox Engine DXT5nm normal map.

    Fox Engine stores tangent-space normals in a DXT5 texture as
    ``X -> ALPHA``, ``Y -> GREEN``; the RGB colour block is a constant dummy
    (131, 125, 131 in every export checked) and carries no information.  Z is
    reconstructed as ``sqrt(1 - x^2 - y^2)``.

    Feeding such a texture straight into a Normal Map node produces the classic
    flat-blue-ish, faintly wrong shading, because the real X never reaches the
    red channel.

    Inputs
        Color     -- the image texture's Color output
        Alpha     -- the image texture's Alpha output
        Strength  -- passed through to the internal Normal Map node
        Y Sign    -- 1.0 for OpenGL-style green, -1.0 to flip (DirectX)
    Output
        Normal
    """
    group = bpy.data.node_groups.get(NORMAL_GROUP_NAME)
    if group is not None:
        return group

    group = bpy.data.node_groups.new(NORMAL_GROUP_NAME, "ShaderNodeTree")

    _new_socket(group, "Color", "INPUT", "NodeSocketColor")
    s_alpha = _new_socket(group, "Alpha", "INPUT", "NodeSocketFloat")
    s_strength = _new_socket(group, "Strength", "INPUT", "NodeSocketFloat")
    s_ysign = _new_socket(group, "Y Sign", "INPUT", "NodeSocketFloat")
    _new_socket(group, "Normal", "OUTPUT", "NodeSocketVector")

    s_alpha.default_value = 0.5
    s_strength.default_value = 1.0
    s_strength.min_value = 0.0
    s_strength.max_value = 10.0
    s_ysign.default_value = 1.0
    s_ysign.min_value = -1.0
    s_ysign.max_value = 1.0

    nodes = group.nodes
    links = group.links

    gin = nodes.new("NodeGroupInput")
    gin.location = (-900, 0)
    gout = nodes.new("NodeGroupOutput")
    gout.location = (500, 0)

    sep = nodes.new("ShaderNodeSeparateColor")
    sep.location = (-700, -140)
    links.new(gin.outputs["Color"], sep.inputs["Color"])

    def math(op, x=-500, y=0, label=""):
        node = nodes.new("ShaderNodeMath")
        node.operation = op
        node.location = (x, y)
        if label:
            node.label = label
        return node

    # x = alpha * 2 - 1
    nx = math("MULTIPLY_ADD", -700, 160, "X = A*2-1")
    nx.inputs[1].default_value = 2.0
    nx.inputs[2].default_value = -1.0
    links.new(gin.outputs["Alpha"], nx.inputs[0])

    # y = green * 2 - 1, then apply the sign flip
    ny = math("MULTIPLY_ADD", -520, -140, "Y = G*2-1")
    ny.inputs[1].default_value = 2.0
    ny.inputs[2].default_value = -1.0
    links.new(sep.outputs["Green"], ny.inputs[0])

    ny_signed = math("MULTIPLY", -340, -140, "Y * sign")
    links.new(ny.outputs[0], ny_signed.inputs[0])
    links.new(gin.outputs["Y Sign"], ny_signed.inputs[1])

    nx2 = math("MULTIPLY", -340, 220, "X^2")
    links.new(nx.outputs[0], nx2.inputs[0])
    links.new(nx.outputs[0], nx2.inputs[1])

    ny2 = math("MULTIPLY", -340, 60, "Y^2")
    links.new(ny_signed.outputs[0], ny2.inputs[0])
    links.new(ny_signed.outputs[0], ny2.inputs[1])

    one_minus = math("SUBTRACT", -160, 220, "1 - X^2")
    one_minus.inputs[0].default_value = 1.0
    links.new(nx2.outputs[0], one_minus.inputs[1])

    minus_y = math("SUBTRACT", -160, 60, "- Y^2")
    links.new(one_minus.outputs[0], minus_y.inputs[0])
    links.new(ny2.outputs[0], minus_y.inputs[1])

    clamp = math("MAXIMUM", 0, 220, "clamp >= 0")
    clamp.inputs[1].default_value = 0.0
    links.new(minus_y.outputs[0], clamp.inputs[0])

    zsqrt = math("SQRT", 0, 60, "Z")
    links.new(clamp.outputs[0], zsqrt.inputs[0])

    z_enc = math("MULTIPLY_ADD", 0, -100, "Z * 0.5 + 0.5")
    z_enc.inputs[1].default_value = 0.5
    z_enc.inputs[2].default_value = 0.5
    links.new(zsqrt.outputs[0], z_enc.inputs[0])

    y_enc = math("MULTIPLY_ADD", 0, -260, "Y * 0.5 + 0.5")
    y_enc.inputs[1].default_value = 0.5
    y_enc.inputs[2].default_value = 0.5
    links.new(ny_signed.outputs[0], y_enc.inputs[0])

    combine = nodes.new("ShaderNodeCombineColor")
    combine.location = (200, 0)
    links.new(gin.outputs["Alpha"], combine.inputs["Red"])
    links.new(y_enc.outputs[0], combine.inputs["Green"])
    links.new(z_enc.outputs[0], combine.inputs["Blue"])

    normal_map = nodes.new("ShaderNodeNormalMap")
    normal_map.location = (340, 0)
    normal_map.space = "TANGENT"
    links.new(combine.outputs["Color"], normal_map.inputs["Color"])
    links.new(gin.outputs["Strength"], normal_map.inputs["Strength"])
    links.new(normal_map.outputs["Normal"], gout.inputs["Normal"])

    return group


def find_input(node, *names):
    """First matching input socket, tolerating Blender renaming sockets."""
    for name in names:
        socket = node.inputs.get(name)
        if socket is not None:
            return socket
    return None
