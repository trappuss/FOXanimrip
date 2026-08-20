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

    /// <summary>
    /// Which patch layer this copy came from. Higher wins.
    ///
    /// The Phantom Pain ships a file more than once and the later copy is the
    /// one the game loads: <c>player2_resident.mtar</c> exists in
    /// <c>master\chunk0.dat</c> with 1,253 clips and again in
    /// <c>master\0\00.dat</c> with 1,285. Taking whichever copy the scan reached
    /// first silently exports a version of the game nobody is playing.
    /// </summary>
    [JsonIgnore]
    public int Layer => LayerOf(Archive);

    internal static int LayerOf(string archivePath)
    {
        if (string.IsNullOrEmpty(archivePath)) return 0;
        var parts = archivePath.Replace('\\', '/')
                               .Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Everything after the named data folder decides the layer: files sitting
        // directly in it are the base game, a numbered folder is a patch, and a
        // named folder inside that (MGSVTUPDATEV0110) is newer still.
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!parts[i].Equals("master", StringComparison.OrdinalIgnoreCase)
                && !parts[i].Equals("mgo", StringComparison.OrdinalIgnoreCase))
                continue;

            var below = parts.Length - 1 - i - 1;      // folders between it and the file
            if (below <= 0) return 0;                  // master\chunk0.dat
            var rank = 0;
            if (int.TryParse(parts[i + 1], out var numbered)) rank = 10 + numbered;
            else rank = 10;
            if (below > 1) rank += 10;                 // master\0\SOMEUPDATE\x.dat
            return rank;
        }
        return 0;
    }

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
    /// <summary>
    /// What this index records. Bumped whenever a new kind of file starts being
    /// collected, because the archive fingerprint cannot notice that: the game
    /// has not changed, only what we look for in it. Without this, adding a
    /// bucket would load an old cache, see it marked complete, and report the
    /// new list as legitimately empty.
    /// </summary>
    public const int CurrentSchema = 3;

    /// <summary>
    /// Deliberately defaults to 0, not to <see cref="CurrentSchema"/>.
    ///
    /// An index written before this field existed has no such property in its
    /// JSON, and the deserialiser leaves a missing property at whatever the
    /// declaration initialises it to. Initialising it to the current schema
    /// therefore made every old file claim to be current -- which is precisely
    /// the staleness the field was added to catch. Only <see cref="Scan"/> sets
    /// it, so anything that did not come from a real scan reads as 0 and is
    /// rescanned.
    /// </summary>
    public int Schema { get; set; }
    public string Root { get; set; } = "";
    public string ProfileId { get; set; } = "custom";
    public string Fingerprint { get; set; } = "";
    public List<CatalogEntry> Models { get; set; } = new();
    public List<CatalogEntry> Mtars { get; set; } = new();

    /// <summary>Motion-graph files. Never named in any dictionary, so they are
    /// found by extension code (the top bits of each entry's path hash), not by
    /// name -- this is the blend/state logic behind player locomotion.</summary>
    public List<CatalogEntry> Mogs { get; set; } = new();
    public List<CatalogEntry> Rigs { get; set; } = new();
    public List<CatalogEntry> HelpBones { get; set; } = new();

    /// <summary>Form-variation files: what a model's customisation options do.</summary>
    public List<CatalogEntry> Variations { get; set; } = new();

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
            Schema = CurrentSchema,
            Root = root,
            ProfileId = profile?.Id ?? "custom",
            Fingerprint = FingerprintOf(archives),
        };
        catalog.Schema = CurrentSchema;      // a resumed index is being extended
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

    /// <summary>
    /// Walk every file entry across a set of archives, handing the callback each
    /// file's name and its 64-bit path hash. A diagnostic hook: the top bits of
    /// the hash are the extension code, so this is how we see what file types an
    /// install actually contains without depending on any dictionary.
    /// </summary>
    public static void WalkFileHashes(IEnumerable<string> archives,
                                      Action<string, ulong> onFile,
                                      CancellationToken token = default)
    {
        foreach (var archive in archives)
        {
            token.ThrowIfCancellationRequested();
            FoxArchive handle = null;
            try { handle = FoxArchive.Open(archive); }
            catch { continue; }
            try { WalkHashes(handle, "", onFile, token); }
            catch (OperationCanceledException) { throw; }
            catch { }
            finally { handle.Dispose(); }
        }
    }

    private static void WalkHashes(FoxArchive archive, string dir,
                                   Action<string, ulong> onFile, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        IReadOnlyList<FoxItem> items;
        try { items = archive.List(dir); }
        catch { return; }

        foreach (var item in items)
        {
            var path = dir.Length == 0 ? item.Name : dir + "/" + item.Name;
            if (!item.IsFolder)
                onFile(item.Name, item.PathHash);
            if (item.IsFolder)
                WalkHashes(archive, path, onFile, token);
            else if (item.IsArchive && !IsLeafArchive(item.Name))
            {
                FoxArchive nested = null;
                try { nested = archive.OpenNested(path); } catch { }
                if (nested is null) continue;
                try { WalkHashes(nested, "", onFile, token); }
                finally { nested.Dispose(); }
            }
        }
    }

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
                // .mog files are hash-named (in no dictionary), so bucket by the
                // extension code carried in the top bits of the path hash.
                if (target is null && ((item.PathHash >> 51) & 0x1FFF) == MogExtCode)
                    target = Mogs;
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

    /// <summary>Fox path-hash extension code for <c>.mog</c> (motion graph).</summary>
    private const ulong MogExtCode = 4752;

    /// <summary>An .mtar reports as an archive but we want it as a file.</summary>
    private static bool IsLeafArchive(string name) =>
        name.EndsWith(".mtar", StringComparison.OrdinalIgnoreCase);

    private List<CatalogEntry> Bucket(string name)
    {
        if (name.EndsWith(".fmdl", StringComparison.OrdinalIgnoreCase)) return Models;
        if (name.EndsWith(".mtar", StringComparison.OrdinalIgnoreCase)) return Mtars;
        if (name.EndsWith(".frig", StringComparison.OrdinalIgnoreCase)) return Rigs;
        if (name.EndsWith(".frdv", StringComparison.OrdinalIgnoreCase)) return HelpBones;
        if (name.EndsWith(".fv2", StringComparison.OrdinalIgnoreCase)) return Variations;
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

    /// <summary>Set when a cached index was rejected for being an older shape,
    /// so the caller can explain the unexpected rescan.</summary>
    public static bool Stale { get; private set; }

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
            if (catalog is null || catalog.Fingerprint != fingerprint) return null;
            // An index written before a bucket existed is not wrong, just
            // incomplete in a way it cannot report. Rescan instead.
            if (catalog.Schema == CurrentSchema) return catalog;
            Stale = true;
            return null;
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
