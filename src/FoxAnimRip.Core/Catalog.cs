// SPDX-License-Identifier: MIT
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FoxBrowser.Interop;

namespace FoxAnimRip;

/// <summary>Where an entry lives: a root archive plus the chain of nested ones.</summary>
public sealed class CatalogEntry
{
    public string Name { get; set; } = "";
    /// <summary>Absolute path of the root .dat / .g0s / .qar.</summary>
    public string Archive { get; set; } = "";
    /// <summary>Nested archive hops, outermost first (e.g. an .fpk inside the dat).</summary>
    public List<string> Chain { get; set; } = new();
    /// <summary>Path of the entry inside the innermost archive.</summary>
    public string Path { get; set; } = "";
    public long Size { get; set; }

    [JsonIgnore]
    public string ArchiveName => System.IO.Path.GetFileName(Archive);

    [JsonIgnore]
    public string Stem => System.IO.Path.GetFileNameWithoutExtension(Name);

    public override string ToString() => Name;
}

public sealed record ScanProgress(string Archive, int ArchiveIndex, int ArchiveCount,
                                  int Models, int Mtars, int Rigs);

/// <summary>
/// An index of the models, animation sets and rigs in a game install.
///
/// Walking a full Phantom Pain install takes a while, so the result is cached
/// per install (keyed by the archives' sizes and timestamps) and reused until
/// the game files change. Nothing here decodes anything -- it only records
/// where things are, so the GUI can offer names instead of paths.
/// </summary>
public sealed class GameCatalog
{
    public string Root { get; set; } = "";
    public string ProfileId { get; set; } = "custom";
    public string Fingerprint { get; set; } = "";
    public List<CatalogEntry> Models { get; set; } = new();
    public List<CatalogEntry> Mtars { get; set; } = new();
    public List<CatalogEntry> Rigs { get; set; } = new();
    public List<CatalogEntry> HelpBones { get; set; } = new();
    /// <summary>Archives already walked, so an interrupted scan can pick up.</summary>
    public List<string> Scanned { get; set; } = new();
    public bool Complete { get; set; }

    [JsonIgnore]
    public bool IsEmpty => Models.Count == 0 && Mtars.Count == 0;

    /// <summary>Character models, the subset almost everyone actually wants.</summary>
    public IEnumerable<CatalogEntry> CharacterModels =>
        Models.Where(m => m.Path.Contains("/chara/", StringComparison.OrdinalIgnoreCase)
                          || m.Path.Contains("\\chara\\", StringComparison.OrdinalIgnoreCase));

    // -- scanning ---------------------------------------------------------

    /// <summary>
    /// Walk the archives, extending <paramref name="resume"/> if one is given.
    ///
    /// A full Phantom Pain install is 13 archives and tens of gigabytes; the
    /// index is written back to disk after every one of them, so a scan that is
    /// interrupted -- cancelled, closed, killed -- picks up where it stopped
    /// instead of starting over.
    /// </summary>
    public static GameCatalog Scan(string root, GameProfile profile, List<string> archives,
                                   IProgress<ScanProgress> progress = null,
                                   CancellationToken token = default,
                                   GameCatalog resume = null)
    {
        var catalog = resume ?? new GameCatalog
        {
            Root = root,
            ProfileId = profile?.Id ?? "custom",
            Fingerprint = FingerprintOf(archives),
        };
        var done = new HashSet<string>(catalog.Scanned, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < archives.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var archive = archives[i];
            progress?.Report(new ScanProgress(System.IO.Path.GetFileName(archive), i,
                archives.Count, catalog.Models.Count, catalog.Mtars.Count, catalog.Rigs.Count));
            if (done.Contains(archive)) continue;

            FoxArchive handle = null;
            try { handle = FoxArchive.Open(archive); }
            catch { catalog.Scanned.Add(archive); continue; }
            try
            {
                catalog.WalkInto(handle, archive, new List<string>(), "", token);
            }
            catch (OperationCanceledException)
            {
                catalog.SaveCache();
                throw;
            }
            catch { /* one bad archive must not lose the rest */ }
            finally { handle.Dispose(); }

            catalog.Scanned.Add(archive);
            catalog.SaveCache();          // survive a kill mid-scan
        }

        progress?.Report(new ScanProgress("done", archives.Count, archives.Count,
            catalog.Models.Count, catalog.Mtars.Count, catalog.Rigs.Count));

        catalog.Models.Sort(ByName);
        catalog.Mtars.Sort(ByName);
        catalog.Complete = true;
        return catalog;
    }

    private static int ByName(CatalogEntry a, CatalogEntry b) =>
        string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

    private void WalkInto(FoxArchive archive, string rootPath, List<string> chain,
                          string dir, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        IReadOnlyList<FoxItem> items;
        try { items = archive.List(dir); }
        catch { return; }

        foreach (var item in items)
        {
            var path = dir.Length == 0 ? item.Name : dir + "/" + item.Name;

            if (!item.IsFolder)
            {
                var target = Bucket(item.Name);
                target?.Add(new CatalogEntry
                {
                    Name = item.Name,
                    Archive = rootPath,
                    Chain = new List<string>(chain),
                    Path = path,
                    Size = (long)item.Size,
                });
            }

            if (item.IsFolder)
            {
                WalkInto(archive, rootPath, chain, path, token);
            }
            else if (item.IsArchive && !IsLeafArchive(item.Name))
            {
                FoxArchive nested = null;
                try { nested = archive.OpenNested(path); } catch { }
                if (nested is null) continue;
                try
                {
                    chain.Add(path);
                    WalkInto(nested, rootPath, chain, "", token);
                }
                finally
                {
                    chain.RemoveAt(chain.Count - 1);
                    nested.Dispose();
                }
            }
        }
    }

    /// <summary>An .mtar reports as an archive but we want it as a file.</summary>
    private static bool IsLeafArchive(string name) =>
        name.EndsWith(".mtar", StringComparison.OrdinalIgnoreCase);

    private List<CatalogEntry> Bucket(string name)
    {
        if (name.EndsWith(".fmdl", StringComparison.OrdinalIgnoreCase)) return Models;
        if (name.EndsWith(".mtar", StringComparison.OrdinalIgnoreCase)) return Mtars;
        if (name.EndsWith(".frig", StringComparison.OrdinalIgnoreCase)) return Rigs;
        if (name.EndsWith(".frdv", StringComparison.OrdinalIgnoreCase)) return HelpBones;
        return null;
    }

    // -- reading ----------------------------------------------------------

    /// <summary>Pull an indexed entry's bytes back out of the archives.</summary>
    public static byte[] Read(CatalogEntry entry)
    {
        var archive = FoxArchive.Open(entry.Archive);
        var opened = new List<FoxArchive> { archive };
        try
        {
            var current = archive;
            foreach (var hop in entry.Chain)
            {
                var nested = current.OpenNested(hop)
                    ?? throw new IOException($"could not open '{hop}' inside "
                                             + System.IO.Path.GetFileName(entry.Archive));
                opened.Add(nested);
                current = nested;
            }
            return current.Read(entry.Path);
        }
        finally
        {
            for (var i = opened.Count - 1; i >= 0; i--) opened[i].Dispose();
        }
    }

    // -- cache ------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string CacheDir => Paths.CatalogCache;

    /// <summary>Size + timestamp of every archive: changes when the game does.</summary>
    public static string FingerprintOf(IEnumerable<string> archives)
    {
        var sb = new StringBuilder();
        foreach (var path in archives.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var info = new FileInfo(path);
            sb.Append(path.ToLowerInvariant()).Append(':')
              .Append(info.Exists ? info.Length : -1).Append(':')
              .Append(info.Exists ? info.LastWriteTimeUtc.Ticks : 0).Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }

    public static string CachePath(string root, string fingerprint)
    {
        var key = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(root.ToLowerInvariant())))[..12];
        return System.IO.Path.Combine(CacheDir, $"{key}-{fingerprint}.json");
    }

    /// <summary>The cached index, complete or partial, or null.</summary>
    public static GameCatalog LoadCached(string root, string fingerprint)
    {
        var path = CachePath(root, fingerprint);
        if (!File.Exists(path)) return null;
        try
        {
            var catalog = JsonSerializer.Deserialize<GameCatalog>(
                File.ReadAllText(path), JsonOptions);
            return catalog is { Fingerprint: not null } && catalog.Fingerprint == fingerprint
                ? catalog : null;
        }
        catch { return null; }
    }

    public void SaveCache()
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            File.WriteAllText(CachePath(Root, Fingerprint),
                              JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { /* a missing cache only costs time */ }
    }

    /// <summary>Cached index if it is still valid, otherwise a fresh scan.</summary>
    public static GameCatalog Open(string root, GameProfile profile,
                                   IProgress<ScanProgress> progress = null,
                                   bool forceRescan = false,
                                   CancellationToken token = default)
    {
        var archives = GameFinder.ArchivesIn(root, profile);
        if (archives.Count == 0)
            throw new FileNotFoundException(
                $"no .dat / .g0s / .qar archives under '{root}'");

        var fingerprint = FingerprintOf(archives);
        GameCatalog partial = null;
        if (!forceRescan)
        {
            var cached = LoadCached(root, fingerprint);
            if (cached is { Complete: true }) return cached;
            partial = cached;      // resume an interrupted scan
        }

        var scanned = Scan(root, profile, archives, progress, token, partial);
        scanned.SaveCache();
        return scanned;
    }
}
