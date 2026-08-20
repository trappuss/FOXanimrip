// SPDX-License-Identifier: MIT
using FoxBrowser.Rendering;

namespace FoxAnimRip;

/// <summary>
/// Rips the files a form variation points at -- above all its textures.
///
/// The inventory can say "this skin tone swaps material slot X to texture #0",
/// but until now nothing could pull texture #0 out of the archives: model export
/// only rips what the model's own materials reference, and a variation's files
/// are by definition not among those. This walks each matching <c>.fv2</c>'s
/// external file list and extracts every entry it can.
///
/// The stored references are 64-bit codes whose exact flavour varies (a plain
/// path code, a path code with the extension folded into the top bits, or
/// Ground Zeroes' own name hash), so each entry is tried every way FoxBrowser
/// can read a file. What worked, and what each code resolved to, goes into
/// <c>ripped-files.tsv</c> -- a row per entry, including the failures, because
/// "this hash could not be read" is an answer too.
/// </summary>
public static class VariationRip
{
    private const ulong PathCodeMask = (1UL << 51) - 1;   // low bits of a PathFileNameCode

    public sealed class Counts
    {
        public int Variations, Entries, Textures, Raw, Missing, PartRefs;
    }

    public static Counts Run(GameCatalog catalog, string filter, string outDir,
                             IReadOnlyList<string> archives, string dictDir,
                             Action<string> log, CancellationToken token = default)
    {
        log ??= _ => { };
        var counts = new Counts();

        var wanted = catalog.Variations
            .Where(v => filter.Length == 0
                        || v.Stem.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || v.Path.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(v => v.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (wanted.Count == 0)
        {
            log($"no form variations match '{filter}'");
            return counts;
        }
        log($"{wanted.Count} form variation(s) match '{filter}'");

        Directory.CreateDirectory(outDir);
        var texDir = Path.Combine(outDir, "textures");
        var manifest = new List<string>
        {
            "variation\tfile\tcode\tresolved\tstatus\toutput"
        };

        // Every character model the catalogue knows, by stem. A variation whose
        // unreadable file is really one of these -- a part model referenced
        // through a packed copy whose inner hash is not separately indexed --
        // is not a loss: the part is exported on its own, by name. This turns
        // those from alarming "unreadable" rows into a pointer to the file that
        // does exist.
        var models = new HashSet<string>(
            catalog.Models.Select(m => m.Stem), StringComparer.OrdinalIgnoreCase);

        // One file on disk per unique code, however many variations share it.
        var seen = new Dictionary<ulong, (string Status, string Output)>();
        // ...and one code per file name. A variation's model and its physics
        // file share a stem, so stem-only names silently overwrote each other:
        // the first run lost ~40 raw files to exactly that.
        var taken = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);

        // Texture archives too, so the variation textures come out full-res.
        var withTex = archives.Concat(GameFinder.TextureArchivesIn(archives))
                              .Distinct(StringComparer.OrdinalIgnoreCase);
        using var assets = FoxAssets.Open(dictDir, withTex);
        assets.BuildIndex();

        var done = 0;
        foreach (var entry in wanted)
        {
            token.ThrowIfCancellationRequested();
            if (++done % 100 == 0) log($"  {done}/{wanted.Count} variation(s)...");

            var reader = Fv2Reader.Read(GameCatalog.Read(entry));
            if (reader is null)
            {
                manifest.Add($"{entry.Stem}\t-\t-\t-\tunreadable\t");
                continue;
            }
            counts.Variations++;

            var files = reader.ExternalFiles;
            for (var i = 0; i < files.Length; i++)
            {
                counts.Entries++;
                var code = files[i];
                // Extract once per unique code; the counters therefore count
                // files, not references.
                if (!seen.TryGetValue(code, out var got))
                {
                    got = Extract(assets, code, texDir, counts, taken);
                    seen[code] = got;
                }
                var resolved = ResolveName(assets, code) ?? "";
                // A file that could not be read, from a variation that assembles
                // a part (arf0_main0_v00 -> arf0_main0_def), is that part model
                // -- which is exported on its own. Say so, per unique code.
                if (got.Status == "missing")
                {
                    var part = PartModel(entry.Stem, models);
                    if (part is not null)
                    {
                        if (!seen[code].Status.Equals("part", StringComparison.Ordinal))
                        {
                            counts.Missing--;
                            counts.PartRefs++;
                            seen[code] = ("part", part);
                        }
                        got = ("part", part);
                    }
                }
                manifest.Add($"{entry.Stem}\t#{i}\t{code:x16}\t{resolved}\t{got.Status}\t{got.Output}");
            }
        }

        File.WriteAllLines(Path.Combine(outDir, "ripped-files.tsv"), manifest);

        log($"{counts.Variations} variation(s) read, {seen.Count} unique file(s): "
            + $"{counts.Textures} texture(s) as DDS, {counts.Raw} raw cop(ies), "
            + $"{counts.PartRefs} part-model reference(s), {counts.Missing} unreadable");
        if (counts.PartRefs > 0)
            log($"part-model references point at part models exported separately "
                + "by name -- not a loss; ripped-files.tsv names each one");
        log($"ripped-files.tsv maps every variation to its files");
        return counts;
    }

    /// <summary>
    /// The part model a variation assembles, if the catalogue has it. Survive's
    /// assembly variations are named <c>&lt;part&gt;_v00</c> and reference
    /// <c>&lt;part&gt;_def</c>; a few other suffixes appear, so try the obvious
    /// ones rather than only the commonest.
    /// </summary>
    private static string PartModel(string variation, HashSet<string> models)
    {
        // arf0_main0_v00 -> arf0_main0_def
        var stripped = System.Text.RegularExpressions.Regex.Replace(
            variation, "_v\\d+$", "");
        foreach (var candidate in new[] { stripped + "_def", stripped, variation + "_def" })
            if (models.Contains(candidate)) return candidate;
        return null;
    }

    /// <summary>Every way a 64-bit code might address a file, most likely first.</summary>
    private static IEnumerable<ulong> Candidates(ulong code)
    {
        yield return code;
        var masked = code & PathCodeMask;
        if (masked != code) yield return masked;
    }

    private static string ResolveName(FoxAssets assets, ulong code)
    {
        foreach (var candidate in Candidates(code))
            if (assets.ResolvePath(candidate) is { Length: > 0 } path) return path;
        try { if (assets.GzNameHashPath(code) is { Length: > 0 } gz) return gz; }
        catch { }
        return null;
    }

    private static (string Status, string Output) Extract(
        FoxAssets assets, ulong code, string texDir, Counts counts,
        Dictionary<string, ulong> taken)
    {
        // A texture first: that is what variation files overwhelmingly are, and
        // the DDS decode also proves the bytes were really an ftex.
        foreach (var candidate in Candidates(code))
        {
            byte[] dds = null;
            try { dds = assets.FtexDds(candidate); } catch { }
            if (dds is null) continue;
            var name = Claim(LeafName(assets, candidate, code) + ".dds", code, taken);
            Directory.CreateDirectory(texDir);
            File.WriteAllBytes(Path.Combine(texDir, name), dds);
            counts.Textures++;
            return ("texture", "textures/" + name);
        }

        // Not a texture (or not decodable): keep the raw bytes rather than
        // nothing, under the file's real extension when the name resolves.
        // Attachments' models and physics files land here.
        foreach (var candidate in Candidates(code))
        {
            byte[] raw = null;
            try { raw = assets.ReadByHash(candidate); } catch { }
            if (raw is null)
                try { raw = assets.ReadByGzNameHash(code); } catch { }
            if (raw is null) continue;
            var ext = LeafExtension(assets, candidate) ?? ".bin";
            var name = Claim(LeafName(assets, candidate, code) + ext, code, taken);
            Directory.CreateDirectory(texDir);
            File.WriteAllBytes(Path.Combine(texDir, name), raw);
            counts.Raw++;
            return ("raw", "textures/" + name);
        }

        counts.Missing++;
        return ("missing", "");
    }

    /// <summary>The name, or the name disambiguated when another code owns it.</summary>
    private static string Claim(string name, ulong code, Dictionary<string, ulong> taken)
    {
        if (taken.TryGetValue(name, out var owner) && owner != code)
            name = Path.GetFileNameWithoutExtension(name)
                   + $"-{code & 0xFFFFFF:x6}" + Path.GetExtension(name);
        taken[name] = code;
        return name;
    }

    private static string LeafName(FoxAssets assets, ulong candidate, ulong code)
    {
        var path = assets.ResolvePath(candidate);
        if (string.IsNullOrEmpty(path)) return code.ToString("x16");
        var leaf = path.Replace('\\', '/').Split('/')[^1];
        return RipJob.Safe(Path.GetFileNameWithoutExtension(leaf));
    }

    /// <summary>The resolved path's extension, when there is one to keep.</summary>
    private static string LeafExtension(FoxAssets assets, ulong candidate)
    {
        var path = assets.ResolvePath(candidate);
        if (string.IsNullOrEmpty(path)) return null;
        var ext = Path.GetExtension(path.Replace('\\', '/').Split('/')[^1]);
        return string.IsNullOrEmpty(ext) ? null : ext.ToLowerInvariant();
    }
}
