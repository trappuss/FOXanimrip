// SPDX-License-Identifier: MIT
using FoxBrowser.Models.Export;
using FoxBrowser.Models.Export.Fbx;
using FoxBrowser.Models.Fmdl;
using FoxBrowser.Rendering;

namespace FoxAnimRip;

public sealed class ModelExportResult
{
    public string FbxPath = "";
    public string RigJsonPath = "";
    public int Textures;
    public int Materials;
    public int Meshes;
    public int Bones;
}

/// <summary>
/// Rips the character itself: mesh, skeleton, materials and textures.
///
/// This is the same thing FoxBrowser's own "rip" button produces -- the model
/// through <see cref="FbxExporter"/> with an
/// <see cref="ExportTexSet"/> per material, the textures decoded out of their
/// FTEX form, and the <c>_rig.json</c> manifest beside them. Doing it here
/// means one tool covers the whole job instead of asking people to rip the
/// model by hand before they can use the animations.
///
/// The layout matches what the Blender add-on expects:
/// <code>
///   &lt;name&gt;.fbx
///   &lt;name&gt;_rig.json
///   &lt;name&gt;_textures/*.dds
///   &lt;name&gt;_source/&lt;name&gt;.fmdl
/// </code>
/// </summary>
public static class ModelExport
{
    public static ModelExportResult Run(ModelContext context, string outDir,
                                        IEnumerable<string> archives, string dictDir,
                                        bool withTextures, bool withSource,
                                        Action<string> log, CancellationToken token = default)
    {
        log ??= _ => { };
        var result = new ModelExportResult();
        Directory.CreateDirectory(outDir);

        var model = FmdlFile.Parse(context.ModelBytes);
        result.Meshes = model.Meshes.Count;
        result.Bones = model.Bones.Count;

        var texDirName = context.Name + "_textures";
        var texDir = Path.Combine(outDir, texDirName);
        var texSets = new Dictionary<int, ExportTexSet>();

        if (withTextures)
        {
            FoxAssets assets = null;
            try
            {
                assets = FoxAssets.Open(dictDir, archives);
                assets.BuildIndex();
                texSets = CollectTextures(model, assets, texDir, texDirName,
                                          out var written, log, token);
                result.Textures = written;
                log($"textures: {written} file(s) into {texDirName}/");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                log($"! textures could not be extracted ({ex.Message}); "
                    + "the model will still export");
            }
            finally { assets?.Dispose(); }
        }

        var scene = ExportScene.Build(model, context.Name, texSets);
        result.Materials = scene.Materials.Count;

        var bytes = FbxExporter.Export(scene);
        bytes = FbxFix.Apply(bytes, out _);
        result.FbxPath = Path.Combine(outDir, RipJob.Safe(context.Name) + ".fbx");
        File.WriteAllBytes(result.FbxPath, bytes);

        try
        {
            var manifest = RipManifest.Build(context.Name, model, context.Frig,
                                             context.HelpBoneOps,
                                             Array.Empty<(string, string)>(), null);
            result.RigJsonPath = Path.Combine(outDir, context.Name + "_rig.json");
            File.WriteAllText(result.RigJsonPath, manifest);
        }
        catch (Exception ex)
        {
            log($"! could not write the rig manifest ({ex.Message})");
        }

        if (withSource)
        {
            try
            {
                var srcDir = Path.Combine(outDir, context.Name + "_source");
                Directory.CreateDirectory(srcDir);
                File.WriteAllBytes(Path.Combine(srcDir, context.Name + ".fmdl"),
                                   context.ModelBytes);
            }
            catch (Exception ex)
            {
                log($"! could not write the source copy ({ex.Message})");
            }
        }

        log($"model: {result.Meshes} meshes, {result.Bones} bones, "
            + $"{result.Materials} materials -> {Path.GetFileName(result.FbxPath)}");
        return result;
    }

    /// <summary>
    /// Pull every base / normal / spec map a material references out of the
    /// archives and write it beside the model as DDS.
    /// </summary>
    private static Dictionary<int, ExportTexSet> CollectTextures(
        FmdlModel model, FoxAssets assets, string texDir, string texDirName,
        out int written, Action<string> log, CancellationToken token)
    {
        var sets = new Dictionary<int, ExportTexSet>();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        written = 0;

        foreach (var pair in model.MaterialTextures)
        {
            token.ThrowIfCancellationRequested();
            string baseMap = null, normalMap = null, specMap = null;

            foreach (var entry in pair.Value)
            {
                var role = entry.Item1;
                var hash = entry.Item2;
                var path = entry.Item3;
                if (role != "base" && role != "normal" && role != "spec") continue;

                var relative = WriteTexture(assets, hash, path, texDir, texDirName,
                                            seen, ref written);
                if (relative is null) continue;
                switch (role)
                {
                    case "base": baseMap ??= relative; break;
                    case "normal": normalMap ??= relative; break;
                    default: specMap ??= relative; break;
                }
            }

            sets[pair.Key] = new ExportTexSet(baseMap, normalMap, specMap);
        }
        return sets;
    }

    /// <summary>One texture, decoded and written once however many materials use it.</summary>
    private static string WriteTexture(FoxAssets assets, ulong hash, string path,
                                       string texDir, string texDirName,
                                       Dictionary<string, string> seen, ref int written)
    {
        var key = hash != 0 ? hash.ToString("x16") : path;
        if (string.IsNullOrEmpty(key)) return null;
        if (seen.TryGetValue(key, out var cached)) return cached;

        string relative = null;
        try
        {
            var dds = hash != 0
                ? assets.FtexDds(hash)
                : (path.Length > 0 ? assets.FtexDdsByPath(path) : null);

            if (dds is not null)
            {
                var source = hash != 0
                    ? (assets.ResolvePath(hash) ?? $"{hash:x16}.ftex")
                    : path;
                var leaf = source.Replace('\\', '/').Split('/')[^1];
                var name = RipJob.Safe(Path.GetFileNameWithoutExtension(leaf)) + ".dds";

                Directory.CreateDirectory(texDir);
                File.WriteAllBytes(Path.Combine(texDir, name), dds);
                relative = texDirName + "/" + name;
                written++;
            }
        }
        catch { /* one missing texture must not lose the model */ }

        seen[key] = relative;
        return relative;
    }
}
