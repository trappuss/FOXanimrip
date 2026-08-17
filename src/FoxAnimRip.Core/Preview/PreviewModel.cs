// SPDX-License-Identifier: MIT
using System.Numerics;
using FoxBrowser.Models.Export;
using FoxBrowser.Models.Fmdl;

namespace FoxAnimRip.Preview;

/// <summary>
/// A character flattened into the few arrays a rasteriser wants.
///
/// One buffer of positions, one of normals, one of triangle indices, and four
/// bone influences per vertex -- laid out so skinning a frame is a linear sweep
/// with no allocation and no indirection through the model's mesh list. The
/// visible meshes are merged; the hidden ones (Fox Engine ships alternates for
/// hands, eyes and faces, flagged invisible) are dropped, since drawing them
/// would show two overlapping pairs of hands.
/// </summary>
public sealed class PreviewModel
{
    public string Name = "model";

    /// <summary>Bind-pose positions, 3 floats per vertex.</summary>
    public float[] Positions = Array.Empty<float>();
    public float[] Normals = Array.Empty<float>();

    /// <summary>Bone index into <see cref="BoneCount"/>, 4 per vertex.</summary>
    public int[] BoneIndices = Array.Empty<int>();
    public float[] BoneWeights = Array.Empty<float>();

    public int[] Triangles = Array.Empty<int>();
    public int VertexCount;
    public int BoneCount;

    /// <summary>Parent of each bone, -1 at a root: the skeleton overlay.</summary>
    public int[] BoneParents = Array.Empty<int>();
    public string[] BoneNames = Array.Empty<string>();

    /// <summary>Bind-pose bone positions, used when there is no clip to play.</summary>
    public Vector3[] BoneRest = Array.Empty<Vector3>();

    /// <summary>Axis-aligned bounds of the bind pose, for framing the camera.</summary>
    public Vector3 Min, Max;
    public Vector3 Centre => (Min + Max) * 0.5f;
    public float Radius => Vector3.Distance(Min, Max) * 0.5f;

    public static PreviewModel Build(FmdlModel model, string name)
    {
        var scene = ExportScene.Build(model, name);
        var result = new PreviewModel { Name = name, BoneCount = model.Bones.Count };

        result.BoneParents = new int[model.Bones.Count];
        result.BoneRest = new Vector3[model.Bones.Count];
        for (var i = 0; i < model.Bones.Count; i++)
        {
            result.BoneParents[i] = model.Bones[i].ParentIndex;
            var w = model.Bones[i].WorldPosition;
            result.BoneRest[i] = new Vector3(w.X, w.Y, w.Z);
        }
        // ExportScene appends a synthetic "[Root]" bone; the model's own bones
        // are the first BoneCount of them and are the ones the palette indexes.
        result.BoneNames = new string[model.Bones.Count];
        for (var i = 0; i < model.Bones.Count && i < scene.Bones.Count; i++)
            result.BoneNames[i] = scene.Bones[i].Name;

        var positions = new List<float>();
        var normals = new List<float>();
        var indices = new List<int>();
        var weights = new List<float>();
        var triangles = new List<int>();
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var mesh in scene.Meshes)
        {
            if (!mesh.Visible) continue;                 // alternate parts
            if (mesh.VertexCount == 0 || mesh.Indices.Length < 3) continue;

            var baseVertex = positions.Count / 3;
            var hasNormals = mesh.Normals.Length == mesh.VertexCount * 3;
            var skin = mesh.Skin;

            for (var v = 0; v < mesh.VertexCount; v++)
            {
                var p = new Vector3(mesh.Positions[v * 3],
                                    mesh.Positions[v * 3 + 1],
                                    mesh.Positions[v * 3 + 2]);
                positions.Add(p.X); positions.Add(p.Y); positions.Add(p.Z);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);

                if (hasNormals)
                {
                    normals.Add(mesh.Normals[v * 3]);
                    normals.Add(mesh.Normals[v * 3 + 1]);
                    normals.Add(mesh.Normals[v * 3 + 2]);
                }
                else { normals.Add(0); normals.Add(1); normals.Add(0); }

                for (var k = 0; k < 4; k++)
                {
                    var bone = 0;
                    var weight = 0f;
                    if (skin is not null)
                    {
                        var slot = skin.Indices[v * 4 + k];
                        weight = skin.Weights[v * 4 + k];
                        if (weight > 0 && slot >= 0 && slot < skin.Palette.Length)
                            bone = skin.Palette[slot];
                        else weight = 0f;
                    }
                    indices.Add(bone);
                    weights.Add(weight);
                }
            }

            for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
            {
                triangles.Add(baseVertex + mesh.Indices[i]);
                triangles.Add(baseVertex + mesh.Indices[i + 1]);
                triangles.Add(baseVertex + mesh.Indices[i + 2]);
            }
        }

        result.Positions = positions.ToArray();
        result.Normals = normals.ToArray();
        result.BoneIndices = indices.ToArray();
        result.BoneWeights = weights.ToArray();
        result.Triangles = triangles.ToArray();
        result.VertexCount = result.Positions.Length / 3;
        if (result.VertexCount == 0) { min = Vector3.Zero; max = Vector3.One; }
        result.Min = min;
        result.Max = max;

        // A vertex with no influences at all would collapse to the origin once
        // skinned. Pin it to the root so it travels with the character instead.
        for (var v = 0; v < result.VertexCount; v++)
        {
            var total = result.BoneWeights[v * 4] + result.BoneWeights[v * 4 + 1]
                      + result.BoneWeights[v * 4 + 2] + result.BoneWeights[v * 4 + 3];
            if (total > 1e-6f) continue;
            result.BoneIndices[v * 4] = 0;
            result.BoneWeights[v * 4] = 1f;
        }
        return result;
    }

    /// <summary>
    /// Deform into <paramref name="outPositions"/> / <paramref name="outNormals"/>
    /// with a bone palette, or into the bind pose when there is none.
    /// </summary>
    public void Skin(Matrix4x4[] palette, float[] outPositions, float[] outNormals)
    {
        if (palette is null)
        {
            Array.Copy(Positions, outPositions, Positions.Length);
            Array.Copy(Normals, outNormals, Normals.Length);
            return;
        }

        var count = palette.Length;
        for (var v = 0; v < VertexCount; v++)
        {
            var p = new Vector3(Positions[v * 3], Positions[v * 3 + 1], Positions[v * 3 + 2]);
            var n = new Vector3(Normals[v * 3], Normals[v * 3 + 1], Normals[v * 3 + 2]);
            var sp = Vector3.Zero;
            var sn = Vector3.Zero;

            for (var k = 0; k < 4; k++)
            {
                var weight = BoneWeights[v * 4 + k];
                if (weight <= 0f) continue;
                var bone = BoneIndices[v * 4 + k];
                if ((uint)bone >= (uint)count) continue;
                var m = palette[bone];
                sp += Vector3.Transform(p, m) * weight;
                sn += Vector3.TransformNormal(n, m) * weight;
            }

            outPositions[v * 3] = sp.X;
            outPositions[v * 3 + 1] = sp.Y;
            outPositions[v * 3 + 2] = sp.Z;
            if (sn.LengthSquared() > 1e-12f) sn = Vector3.Normalize(sn);
            outNormals[v * 3] = sn.X;
            outNormals[v * 3 + 1] = sn.Y;
            outNormals[v * 3 + 2] = sn.Z;
        }
    }
}
