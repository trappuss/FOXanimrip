// SPDX-License-Identifier: MIT
using System.Text;
using FoxBrowser.Models;
using FoxBrowser.Models.Fmdl;

namespace FoxAnimRip;

/// <summary>
/// Everything a game has, written down: models, their textures, and the
/// variations that change how they look.
///
/// The reason this needs three tables rather than one is that Fox Engine does
/// not store a character as a finished thing. A model is a mesh with named mesh
/// groups; a **form variation** (<c>.fv2</c>) then hides some groups, shows
/// others, swaps individual textures, sets shader parameters and bolts extra
/// models onto bones. One <c>.fmdl</c> plus a folder of <c>.fv2</c> files is how
/// a handful of files becomes hundreds of appearances.
///
/// So a customisation option is not a file you can rip -- it is an instruction,
/// and this reads those instructions out. It also answers the question that
/// prompts it most often: whether an option like skin tone is a different
/// texture or just a shader value. Both mechanisms exist, they sit in different
/// tables in the same file, and the file says which one an option uses.
/// </summary>
public static class Inventory
{
    public sealed record Counts(int Models, int Materials, int Textures,
                                int Variations, int Swaps, int Parameters,
                                int Attachments, int Unresolved);

    /// <summary>
    /// Walk a game and write models.tsv, textures.tsv and variations.tsv.
    /// </summary>
    public static Counts Write(GameCatalog catalog, string outDir, string filter,
                               bool charactersOnly, Action<string> log = null,
                               IProgress<(int Done, int Total, string Name)> progress = null,
                               CancellationToken token = default)
    {
        log ??= _ => { };
        Directory.CreateDirectory(outDir);

        var models = 0; var materials = 0; var textures = 0;
        var variations = 0; var swaps = 0; var parameters = 0;
        var attachments = 0; var unresolved = 0;

        var modelRows = Open(outDir, "models.tsv",
            "model\tbones\tmeshes\tmaterials\tmeshGroups\tarchive\tlayer\tpath");
        var textureRows = Open(outDir, "textures.tsv",
            "model\tmaterial\trole\ttexture\tpath");
        var variationRows = Open(outDir, "variations.tsv",
            "variation\tkind\tdetail\tvalue\tarchive\tpath");

        try
        {
            // -- models and their textures
            var pool = charactersOnly ? catalog.CharacterModels : catalog.Models;
            var wanted = Narrow(pool, filter);
            log($"{wanted.Count} model(s) to read");

            for (var i = 0; i < wanted.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var entry = wanted[i];
                progress?.Report((i, wanted.Count, entry.Stem));
                try
                {
                    var model = FmdlFile.Parse(GameCatalog.Read(entry));
                    var groups = model.Groups.Count;
                    var materialCount = model.MaterialNameHash.Count;
                    modelRows.WriteLine(string.Join('\t', entry.Stem, model.Bones.Count,
                        model.Meshes.Count, materialCount, groups,
                        entry.ArchiveName, entry.Layer, entry.Path));
                    models++;
                    materials += materialCount;

                    foreach (var pair in model.MaterialTextures)
                    {
                        var material = Label(model.MaterialNameHash.TryGetValue(pair.Key,
                            out var hash) ? hash : 0, $"mat{pair.Key}");
                        foreach (var item in pair.Value)
                        {
                            // (role, hash, path, _) -- the path is often empty and
                            // the hash is what actually identifies the texture.
                            var name = item.Item3.Length > 0
                                ? item.Item3
                                : HashNames.ResolveFull(item.Item2) ?? "";
                            if (name.Length == 0) unresolved++;
                            textureRows.WriteLine(string.Join('\t', entry.Stem, material,
                                item.Item1, Leaf(name, item.Item2), name));
                            textures++;
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { log($"! {entry.Stem}: {ex.Message}"); }
            }

            // -- variations
            var variationEntries = Narrow(catalog.Variations, filter);
            log($"{variationEntries.Count} form-variation file(s) to read");
            foreach (var entry in variationEntries)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var read = Fv2Reader.Read(GameCatalog.Read(entry));
                    if (read is null) continue;
                    variations++;
                    foreach (var line in read.Describe(entry, ref unresolved))
                    {
                        variationRows.WriteLine(line.Line);
                        switch (line.Kind)
                        {
                            case "textureSwap": swaps++; break;
                            case "materialParameter": parameters++; break;
                            case "attachModel": attachments++; break;
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { log($"! {entry.Name}: {ex.Message}"); }
            }
        }
        finally
        {
            modelRows.Dispose();
            textureRows.Dispose();
            variationRows.Dispose();
        }

        return new Counts(models, materials, textures, variations, swaps,
                          parameters, attachments, unresolved);
    }

    private static List<CatalogEntry> Narrow(IEnumerable<CatalogEntry> pool, string filter)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<CatalogEntry>();
        // Newest patch layer first, so the copy the game loads is the one kept.
        foreach (var entry in pool.OrderByDescending(e => e.Layer)
                                  .ThenByDescending(e => e.Size))
        {
            if (filter is { Length: > 0 }
                && entry.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                && entry.Path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (!seen.Add(entry.Stem)) continue;
            kept.Add(entry);
        }
        kept.Sort((a, b) => string.Compare(a.Stem, b.Stem, StringComparison.OrdinalIgnoreCase));
        return kept;
    }

    private static StreamWriter Open(string dir, string name, string header)
    {
        var writer = new StreamWriter(Path.Combine(dir, name), false, new UTF8Encoding(false));
        writer.WriteLine(header);
        return writer;
    }

    internal static string Label(ulong hash, string fallback) =>
        hash != 0 ? (StrCodeNames.Label(hash, "name") ?? fallback) : fallback;

    internal static string Leaf(string path, ulong hash)
    {
        if (path.Length == 0) return hash != 0 ? $"{hash:x16}" : "";
        var cut = path.Replace('\\', '/').LastIndexOf('/');
        return cut >= 0 ? path[(cut + 1)..] : path;
    }

    /// <summary>
    /// Generate a script that rips every model the inventory found.
    ///
    /// The inventory is a list; this is the list turned into the thing you
    /// actually wanted, without pasting a thousand commands by hand. It exports
    /// in batches rather than one process per model, because process startup
    /// dwarfs the work for a single character.
    /// </summary>
    public static void WriteRipScript(string outDir, string gameId, string gameRoot,
                                      IEnumerable<string> modelNames, int batch = 25)
    {
        var names = modelNames.ToList();
        var path = Path.Combine(outDir, "rip-all-models.bat");
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));

        writer.WriteLine("@echo off");
        writer.WriteLine("rem  Generated by foxanimrip --inventory. Rips every model listed");
        writer.WriteLine($"rem  in models.tsv ({names.Count} of them), with textures.");
        writer.WriteLine("rem");
        writer.WriteLine("rem  Models are exported in batches: one process per character would");
        writer.WriteLine("rem  spend more time starting up than working. Each batch writes a");
        writer.WriteLine("rem  folder per character under OUT.");
        writer.WriteLine("rem");
        writer.WriteLine("rem  This will be large. Check the free space on OUT first.");
        writer.WriteLine();
        writer.WriteLine("setlocal");
        writer.WriteLine("cd /d \"%~dp0\"");
        writer.WriteLine();
        writer.WriteLine("set OUT=C:\\rips\\all-models");
        writer.WriteLine("set TOOL=%~dp0foxanimrip-cli.exe");
        writer.WriteLine("if not exist \"%TOOL%\" set TOOL=%~dp0..\\foxanimrip-cli.exe");
        writer.WriteLine("if not exist \"%TOOL%\" set TOOL=foxanimrip-cli.exe");
        writer.WriteLine();
        writer.WriteLine($"set GAME=--game {gameId}");
        if (!string.IsNullOrEmpty(gameRoot))
            writer.WriteLine($"set GAME=%GAME% --root \"{gameRoot}\"");
        writer.WriteLine();

        var batches = 0;
        for (var i = 0; i < names.Count; i += batch)
        {
            var slice = names.Skip(i).Take(batch).ToList();
            batches++;
            writer.WriteLine($"echo   batch {batches} of "
                             + $"{(names.Count + batch - 1) / batch} "
                             + $"({slice.Count} models)");
            writer.WriteLine($"\"%TOOL%\" %GAME% --character {string.Join(",", slice)} ^");
            writer.WriteLine("         --export-model --list ^");
            writer.WriteLine("         --out \"%OUT%\"");
            writer.WriteLine("if errorlevel 1 echo   ** batch " + batches + " reported a problem");
            writer.WriteLine();
        }

        writer.WriteLine("echo.");
        writer.WriteLine("echo   Done. Models are under %OUT%");
        writer.WriteLine("echo.");
        writer.WriteLine("pause");
        writer.WriteLine("endlocal");
    }
}
