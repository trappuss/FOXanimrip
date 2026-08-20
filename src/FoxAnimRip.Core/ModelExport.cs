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
                                        Action<string> log, CancellationToken token = default,
                                        object sharedAssets = null)
    {
        // Typed as object so callers that never pass one (the window) need no
        // reference to FoxBrowser.Core just to see this method.
        var sharedFox = sharedAssets as FoxAssets;
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
            // Opening the archives and building their index costs more than
            // ripping one model's textures. A batch export passes one shared
            // FoxAssets in so the price is paid once, not per model.
            var assets = sharedFox;
            try
            {
                if (assets is null)
                {
                    // Include the texture archives so streamed high-resolution
                    // mips can be assembled -- the useful-archive set skips them.
                    var withTex = archives.Concat(GameFinder.TextureArchivesIn(archives))
                                          .Distinct(StringComparer.OrdinalIgnoreCase);
                    assets = FoxAssets.Open(dictDir, withTex);
                    assets.BuildIndex();
                }
                texSets = CollectTextures(model, assets, texDir, texDirName,
                                          out var written, out var maps, log, token);
                result.Textures = written;
                var big = MaxDimension(texDir);
                log($"textures: {written} file(s) into {texDirName}/"
                    + (big > 0 ? $" (up to {big}px)" : ""));
                WriteMapSidecar(outDir, context.Name, maps);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                log($"! textures could not be extracted ({ex.Message}); "
                    + "the model will still export");
            }
            finally { if (assets is not null && !ReferenceEquals(assets, sharedFox)) assets.Dispose(); }
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
    /// The texture rip on its own: open the archives, decode every map this
    /// model references into <paramref name="texDir"/>, and hand back the
    /// per-material sets. The runtime-pack exporter shares the model rip's
    /// decode this way instead of growing a second one.
    /// </summary>
    internal static Dictionary<int, ExportTexSet> RipTextures(
        FmdlModel model, IEnumerable<string> archives, string dictDir,
        string texDir, string texDirName, out int written,
        Action<string> log, CancellationToken token)
    {
        var withTex = archives.Concat(GameFinder.TextureArchivesIn(archives))
                              .Distinct(StringComparer.OrdinalIgnoreCase);
        using var assets = FoxAssets.Open(dictDir, withTex);
        assets.BuildIndex();
        return CollectTextures(model, assets, texDir, texDirName,
                               out written, out _, log, token);
    }

    /// <summary>
    /// Pull every base / normal / spec map a material references out of the
    /// archives and write it beside the model as DDS.
    /// </summary>
    private static Dictionary<int, ExportTexSet> CollectTextures(
        FmdlModel model, FoxAssets assets, string texDir, string texDirName,
        out int written, out List<(string Base, string Normal, string Spec)> maps,
        Action<string> log, CancellationToken token)
    {
        var sets = new Dictionary<int, ExportTexSet>();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        maps = new List<(string, string, string)>();
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
            // Record the role of each map, keyed later by the base file name, so
            // the Blender add-on can wire the normal and spec even when a texture
            // came out hash-named and its role is no longer readable from the
            // file name alone.
            if (baseMap is not null || normalMap is not null || specMap is not null)
                maps.Add((Leaf(baseMap), Leaf(normalMap), Leaf(specMap)));
        }
        return sets;
    }

    private static string Leaf(string relative) =>
        string.IsNullOrEmpty(relative) ? "" : relative.Replace('\\', '/').Split('/')[^1];

    /// <summary>Largest texture edge just written, read straight from the DDS
    /// headers, so a run reports whether the streamed high mips came through.</summary>
    private static int MaxDimension(string texDir)
    {
        var max = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(texDir, "*.dds"))
            {
                try
                {
                    var head = new byte[20];
                    using var fs = File.OpenRead(f);
                    if (fs.Read(head, 0, 20) < 20) continue;
                    if (head[0] != 'D' || head[1] != 'D' || head[2] != 'S') continue;
                    var height = BitConverter.ToInt32(head, 12);
                    var width = BitConverter.ToInt32(head, 16);
                    max = Math.Max(max, Math.Max(width, height));
                }
                catch { }
            }
        }
        catch { }
        return max;
    }

    /// <summary>
    /// Write <c>&lt;name&gt;_maps.tsv</c>: base, normal and spec file names per
    /// material, keyed by the base file. Fox Engine only wires base and normal
    /// into the FBX, and an unresolved texture comes out hash-named, so the spec
    /// map and a hash-named normal are otherwise unidentifiable at import. The
    /// add-on reads this to wire them by the material's base file, which both
    /// sides know.
    /// </summary>
    private static void WriteMapSidecar(string outDir, string name,
        List<(string Base, string Normal, string Spec)> maps)
    {
        if (maps is null || maps.Count == 0) return;
        try
        {
            var lines = new List<string> { "base\tnormal\tspec" };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (b, n, s) in maps)
            {
                if (b.Length == 0 || !seen.Add(b)) continue;   // key by base file
                lines.Add($"{b}\t{n}\t{s}");
            }
            if (lines.Count > 1)
                File.WriteAllLines(Path.Combine(outDir, name + "_maps.tsv"), lines);
        }
        catch { /* the sidecar is an optimisation, never fatal */ }
    }

    /// <summary>
    /// The full-resolution DDS, streamed mips and all, or null when there is no
    /// streamed part to assemble (then the caller uses the inline DDS).
    ///
    /// The reliable route is <c>FtexSourceFiles</c>, which returns the .ftex and
    /// every numbered .ftexs companion as (leaf, bytes) straight from the
    /// archives by hash -- so it works even for a hash-named texture whose path
    /// does not resolve, which is exactly the case that left avatar faces at
    /// 512. Those bytes feed the assembler through an in-memory reader. The
    /// path-based assembler is kept as a fallback.
    /// </summary>
    private static byte[] FullResDds(FoxAssets assets, ulong hash, string path)
    {
        try
        {
            List<(string Name, byte[] Bytes)> sources = null;
            try
            {
                sources = hash != 0
                    ? assets.FtexSourceFiles(hash)
                    : (path.Length > 0 ? assets.FtexSourceFilesByPath(path) : null);
            }
            catch { sources = null; }

            if (sources is { Count: > 0 })
            {
                var bytesByLeaf = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                string mainLeaf = null;
                foreach (var (name, bytes) in sources)
                {
                    var leaf = name.Replace('\\', '/').Split('/')[^1];
                    bytesByLeaf[leaf] = bytes;
                    // the .ftex is the master; .N.ftexs are the streamed mips
                    if (leaf.EndsWith(".ftex", StringComparison.OrdinalIgnoreCase))
                        mainLeaf ??= leaf;
                }
                mainLeaf ??= sources[0].Name.Replace('\\', '/').Split('/')[^1];

                byte[] Read(string name)
                {
                    if (bytesByLeaf.TryGetValue(name, out var b)) return b;
                    var leaf = name.Replace('\\', '/').Split('/')[^1];
                    return bytesByLeaf.TryGetValue(leaf, out var b2) ? b2 : null;
                }
                var built = FoxBrowser.Imaging.FtexAssembleCore.FullDds(mainLeaf, Read);
                if (built is { Length: > 0 }) return built;
            }

            // fallback: assemble by path out of the archives
            var full = path.Length > 0 ? path : assets.ResolvePath(hash);
            if (string.IsNullOrEmpty(full)) return null;
            full = full.Replace('\\', '/');
            var slash = full.LastIndexOf('/');
            var dir = slash >= 0 ? full[..slash] : "";
            var fleaf = slash >= 0 ? full[(slash + 1)..] : full;
            byte[] ReadPath(string name)
            {
                try { return assets.ReadByPath(dir.Length > 0 ? dir + "/" + name : name); }
                catch { return null; }
            }
            var dds = FoxBrowser.Imaging.FtexAssembleCore.FullDds(fleaf, ReadPath);
            return dds is { Length: > 0 } ? dds : null;
        }
        catch { return null; }
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
            // Full resolution first: a Fox Engine .ftex holds only the lower
            // mips inline, with the high-resolution mips streamed in numbered
            // .ftexs companion files. FtexDds returns just the inline part, so
            // Survive characters came out at 512 or less; FullDds pulls the
            // streamed mips in through a reader over the archives. Fall back to
            // the inline DDS when a texture has no streamed part or its path is
            // unresolved (hash-named).
            var dds = FullResDds(assets, hash, path)
                      ?? (hash != 0 ? assets.FtexDds(hash)
                          : (path.Length > 0 ? assets.FtexDdsByPath(path) : null));

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
