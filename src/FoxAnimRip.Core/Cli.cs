// SPDX-License-Identifier: MIT
using System.Runtime.CompilerServices;

namespace FoxAnimRip;

/// <summary>Command-line front end. The GUI drives the same Core types.</summary>
public static class Cli
{
    public const string Usage = """
foxanimrip - bulk-export Fox Engine animations (MGSV: The Phantom Pain,
             Ground Zeroes, Metal Gear Survive) through FoxBrowser's own
             decoder, rig solve and FBX writer.

  Run with no arguments to open the window. Everything below is the
  command-line equivalent.

  foxanimrip --game gz --character sna2_main0_def --all --out C:\rips\anims

Choosing the game:
  --game <id>          gz | tpp | survive | custom. Auto-detected if omitted.
  --root <folder>      Game folder holding the .dat / .g0s / .qar archives.
                       Falls back to FoxBrowser's own saved folder.
  --list-games         Print the installs found, then exit.

Choosing the model:
  --character <name>   A model in the game, e.g. sna2_main0_def. Repeatable,
                       and comma-separated lists work too. Partial names are
                       fine when they match exactly one model. With more than
                       one character the output gains a folder per character.
  --model <path>       ...or a loose .fmdl on disk. FoxBrowser writes one into
                       <model>_source/ beside every model it rips.
  --list-models [text] Print the character models found, then exit.

Choosing the animations:
  --mtar <file|name>   One animation archive, repeatable.
  --all                Every animation archive in the game that fits the model.
  --list-mtars         Print compatible animation archives, then exit.

Output:
  --out <folder>       Clips land in <out>/<mtar>/<clip>.fbx, plus an index.tsv
  --filter <text>      Only clips whose name contains this.
  --min-match <n>      Bones a clip must drive to count. Default 8.
  --limit <n>          Stop after n clips.
  --list               Print what would be exported; write nothing.
  --with-mesh          Put the mesh in every clip file. Off by default: it is
                       identical in all of them and turns a ~400 KB clip into
                       ~3.4 MB.
  --step <n>           Keep every nth frame. Default 1.
  --fps <f>            Clip frame rate. Default 59.94.
  --keep-static        Also write clips where nothing moves; FoxBrowser's FBX
                       writer emits no curves for those, so they arrive empty.
  --dedupe [deg]       Skip clips whose baked motion matches one already
                       written, within [deg] degrees (default 0.5). Fox Engine
                       ships many near-identical variants.
  --pack <n>           Put n clips in each FBX as separate takes instead of one
                       file per clip. Cuts Blender import time enormously;
                       50 is a good number.
  --export-model       Also rip the character itself -- mesh, skeleton,
                       materials and textures -- next to the animations, in the
                       layout the Blender add-on expects.
  --no-textures        With --export-model, skip the textures.

Other:
  --fb <path>          FoxBrowser.exe. Auto-detected if it is beside this tool
                       or somewhere under your Desktop or user folder.
  --frig <file>        Use this rig file instead of searching the archives.
  --frdv <file>        Use these help-bone operators instead of searching.
  --rescan             Ignore the cached index of the game files.
  --no-fbx-fix         Leave the AnimationStack/AnimationLayer class tokens as
                       FoxBrowser writes them. Blender cannot import those.
  --refresh            Re-unpack FoxBrowser's assemblies after an update.
  --portable           Keep settings and caches beside the executable (default
                       unless it is installed under Program Files).
  --no-portable        Keep them in %LOCALAPPDATA%\foxanimrip instead.
  --where              Print where settings and caches are going, then exit.
  --quiet              Only report failures that lose a clip.
  -h, --help           This text.
""";

    private sealed class Args
    {
        public string Fb = "", Root = "", GameId = "", ModelPath = "";
        public string FrigPath = "", FrdvPath = "";
        public List<string> Characters = new();
        public List<string> Mtars = new();
        public bool All, Rescan, Refresh, ListGames, ListModels, ListMtars, Where;
        public bool ExportModel, NoTextures;
        public string ListModelsFilter = "";
        public RipOptions Rip = new();
    }

    public static int Main(string[] args)
    {
        var a = new Args();
        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                string Next(string flag) => i + 1 < args.Length
                    ? args[++i]
                    : throw new ArgumentException($"{flag} needs a value");

                switch (args[i])
                {
                    case "--fb": a.Fb = Next("--fb"); break;
                    case "--root": a.Root = Next("--root"); break;
                    case "--game": a.GameId = Next("--game"); break;
                    case "--character":
                    case "--characters":
                        a.Characters.AddRange(Next("--character")
                            .Split(',', StringSplitOptions.RemoveEmptyEntries
                                        | StringSplitOptions.TrimEntries));
                        break;
                    case "--model": a.ModelPath = Next("--model"); break;
                    case "--mtar": a.Mtars.Add(Next("--mtar")); break;
                    case "--frig": a.FrigPath = Next("--frig"); break;
                    case "--frdv": a.FrdvPath = Next("--frdv"); break;
                    case "--out": a.Rip.OutDir = Next("--out"); break;
                    case "--filter": a.Rip.Filter = Next("--filter"); break;
                    case "--min-match": a.Rip.MinMatch = int.Parse(Next("--min-match")); break;
                    case "--limit": a.Rip.Limit = int.Parse(Next("--limit")); break;
                    case "--step": a.Rip.Step = Math.Max(1, int.Parse(Next("--step"))); break;
                    case "--fps": a.Rip.Fps = float.Parse(Next("--fps")); break;
                    case "--all": a.All = true; break;
                    case "--scan": a.All = true; break;   // old name
                    case "--with-mesh": a.Rip.WithMesh = true; break;
                    case "--list": a.Rip.ListOnly = true; break;
                    case "--keep-static": a.Rip.KeepStatic = true; break;
                    case "--dedupe":
                        a.Rip.Dedupe = true;
                        if (i + 1 < args.Length && float.TryParse(args[i + 1], out var tol))
                        {
                            a.Rip.DedupeRotation = tol;
                            i++;
                        }
                        break;
                    case "--pack": a.Rip.PackSize = Math.Max(0, int.Parse(Next("--pack"))); break;
                    case "--export-model": a.ExportModel = true; break;
                    case "--no-textures": a.NoTextures = true; break;
                    case "--no-fbx-fix": a.Rip.NoFbxFix = true; break;
                    case "--quiet": a.Rip.Quiet = true; break;
                    case "--rescan": a.Rescan = true; break;
                    case "--refresh": a.Refresh = true; break;
                    case "--list-games": a.ListGames = true; break;
                    case "--where": a.Where = true; break;
                    case "--portable": Paths.Force(true); break;
                    case "--no-portable": Paths.Force(false); break;
                    case "--list-mtars": a.ListMtars = true; break;
                    case "--list-models":
                        a.ListModels = true;
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                            a.ListModelsFilter = args[++i];
                        break;
                    case "-h": case "--help": Console.WriteLine(Usage); return 0;
                    default: throw new ArgumentException($"unknown option '{args[i]}'");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("! " + ex.Message);
            Console.Error.WriteLine("run with --help for usage");
            return 64;
        }

        if (a.Where)
        {
            Console.WriteLine(Paths.Describe());
            Console.WriteLine("assemblies: " + Paths.Assemblies);
            Console.WriteLine("index cache: " + Paths.CatalogCache);
            Console.WriteLine("settings: " + Paths.Settings);
            Console.WriteLine("dictionaries: " + Paths.DictStaging);
            return 0;
        }

        try
        {
            a.Fb = GameFinder.FindFoxBrowser(a.Fb);
            if (a.Fb.Length == 0)
            {
                Console.Error.WriteLine(
                    "! could not find FoxBrowser.exe - pass --fb <path to FoxBrowser.exe>");
                return 66;
            }
            Bundle.Extract(a.Fb, a.Refresh, Log);
            Bundle.Hook(Bundle.Extract(a.Fb, false, _ => { }));
            return Invoke(a);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("! " + ex.Message);
            if (Environment.GetEnvironmentVariable("FOXANIMRIP_TRACE") == "1")
                Console.Error.WriteLine(ex);
            return 1;
        }
    }

    // Kept out of Main so the JIT does not resolve FoxBrowser's types before
    // Bundle.Extract has written them to disk and Hook is installed.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Invoke(Args a)
    {
        RipJob.UseDictionaries(Path.GetDirectoryName(Path.GetFullPath(a.Fb))!, Log);

        // -- which game
        var installs = GameFinder.Detect(GameFinder.FoxBrowserRoots());
        if (a.ListGames)
        {
            if (installs.Count == 0) Console.WriteLine("no Fox Engine installs found");
            foreach (var install in installs)
                Console.WriteLine($"{install.Profile.Id}\t{install.Profile.DisplayName}\t"
                                  + $"{install.ArchiveCount}\t{install.Root}");
            return 0;
        }

        GameProfile profile;
        string root;
        if (a.Root.Length > 0)
        {
            root = a.Root;
            profile = a.GameId.Length > 0 ? GameProfile.ById(a.GameId) : GameFinder.Identify(root);
        }
        else
        {
            var pick = a.GameId.Length > 0
                ? installs.FirstOrDefault(i => i.Profile.Id.Equals(a.GameId,
                      StringComparison.OrdinalIgnoreCase))
                : installs.FirstOrDefault();
            if (pick is null)
            {
                Console.Error.WriteLine("! no game found - pass --root <game folder>, "
                                        + "or --list-games to see what was detected");
                return 66;
            }
            root = pick.Root;
            profile = pick.Profile;
        }
        Log($"game: {profile.DisplayName} at {root}");

        var archives = GameFinder.ArchivesIn(root, profile);
        if (archives.Count == 0)
        {
            Console.Error.WriteLine($"! no archives under {root}");
            return 66;
        }

        // -- the characters
        var contexts = new List<ModelContext>();
        GameCatalog catalog = null;

        if (a.ModelPath.Length > 0)
        {
            var bytes = File.ReadAllBytes(a.ModelPath);
            contexts.Add(ModelContext.Create(
                Path.GetFileNameWithoutExtension(a.ModelPath), bytes));
        }

        if (a.Characters.Count > 0 || a.ListModels)
        {
            catalog = OpenCatalog(root, profile, a.Rescan);
            if (a.ListModels)
            {
                var needle = a.ListModelsFilter;
                foreach (var entry in catalog.CharacterModels
                             .Where(m => needle.Length == 0 || m.Stem.Contains(
                                 needle, StringComparison.OrdinalIgnoreCase))
                             .GroupBy(m => m.Stem, StringComparer.OrdinalIgnoreCase)
                             .Select(g => g.First())
                             .OrderBy(m => m.Stem, StringComparer.OrdinalIgnoreCase))
                    Console.WriteLine($"{entry.Stem}\t{entry.ArchiveName}\t{entry.Path}");
                return 0;
            }

            foreach (var wanted in a.Characters)
            {
                var entry = ResolveCharacter(catalog, wanted);
                if (entry is null) return 66;
                contexts.Add(ModelContext.Create(entry.Stem, GameCatalog.Read(entry)));
            }
        }

        if (contexts.Count == 0)
        {
            Console.Error.WriteLine(
                "! choose a character: --character <name> (repeatable) or --model <file.fmdl>");
            return 64;
        }

        // -- rig and help bones, per character
        foreach (var context in contexts)
        {
            var frig = a.FrigPath.Length > 0
                ? File.ReadAllBytes(a.FrigPath)
                : Sources.FindFrig(archives, context.BoneHashes,
                                   m => Log($"  {context.Name}: {m}"));
            var frdv = a.FrdvPath.Length > 0
                ? File.ReadAllBytes(a.FrdvPath)
                : Sources.FindFrdv(archives, context.Name,
                                   m => Log($"  {context.Name}: {m}"));
            context.Attach(frig, frdv);
        }

        if (a.Rip.OutDir.Length == 0 && !a.Rip.ListOnly)
        {
            Console.Error.WriteLine("! --out <folder> is required");
            return 64;
        }

        // -- the model itself, if asked
        if (a.ExportModel)
        {
            var dictDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(a.Fb))!, "dict");
            foreach (var context in contexts)
            {
                var dir = contexts.Count > 1
                    ? Path.Combine(a.Rip.OutDir, RipJob.Safe(context.Name))
                    : a.Rip.OutDir;
                try
                {
                    ModelExport.Run(context, dir, archives, dictDir,
                                    withTextures: !a.NoTextures, withSource: true, Log);
                }
                catch (Exception ex)
                {
                    Log($"! {context.Name}: model export failed ({ex.Message})");
                }
            }
            if (a.Mtars.Count == 0 && !a.All)
            {
                Log("model(s) exported; no animations requested");
                return 0;
            }
        }

        // -- the animation archives
        if (a.All || a.ListMtars || a.Mtars.Any(m => !File.Exists(m)))
            catalog ??= OpenCatalog(root, profile, a.Rescan);

        if (a.ListMtars)
        {
            foreach (var context in contexts)
            {
                if (contexts.Count > 1) Console.WriteLine($"# {context.Name}");
                foreach (var (entry, match) in CompatibleMtars(catalog, context, a.Rip.MinMatch))
                    Console.WriteLine($"{entry.Stem}\t{match.Clips}\t{match.MatchedBones}\t"
                                      + entry.ArchiveName);
            }
            return 0;
        }

        if (a.Rip.OutDir.Length == 0 && !a.Rip.ListOnly)
        {
            Console.Error.WriteLine("! --out <folder> is required");
            return 64;
        }
        if (a.Mtars.Count == 0 && !a.All)
        {
            Console.Error.WriteLine(
                "! choose animations: --mtar <name> (repeatable), or --all");
            return 64;
        }

        var namedMtars = a.Mtars;
        var wantAll = a.All;
        var minMatch = a.Rip.MinMatch;
        var sharedCatalog = catalog;

        List<MtarSource> SourcesFor(ModelContext context, CancellationToken token)
        {
            var sources = new List<MtarSource>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var wanted in namedMtars)
            {
                if (File.Exists(wanted))
                {
                    var path = wanted;
                    if (seen.Add(Path.GetFileName(path)))
                        sources.Add(new MtarSource(Path.GetFileName(path),
                                                   () => File.ReadAllBytes(path)));
                    continue;
                }
                var hits = sharedCatalog.Mtars
                    .Where(m => m.Stem.Contains(wanted, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (hits.Count == 0) Log($"! no animation archive matching '{wanted}'");
                foreach (var hit in hits)
                {
                    if (!seen.Add(hit.Name)) continue;
                    var captured = hit;
                    sources.Add(new MtarSource(hit.Name, () => GameCatalog.Read(captured)));
                }
            }

            if (wantAll)
            {
                foreach (var (entry, _) in CompatibleMtars(sharedCatalog, context, minMatch, token))
                {
                    if (!seen.Add(entry.Name)) continue;
                    var captured = entry;
                    sources.Add(new MtarSource(entry.Name, () => GameCatalog.Read(captured)));
                }
            }
            return sources;
        }

        var batch = BatchJob.Run(contexts, SourcesFor, a.Rip, Log);
        if (a.Rip.ListOnly)
            foreach (var (_, one) in batch.PerCharacter)
                foreach (var line in one.ClipNames)
                    Console.WriteLine(line);
        else if (batch.Exported > 0)
            Log($"written to {a.Rip.OutDir}");

        return batch.Exported > 0 ? 0 : 3;
    }

    private static CatalogEntry ResolveCharacter(GameCatalog catalog, string wanted)
    {
        var exact = catalog.Models.Where(m => string.Equals(m.Stem, wanted,
                        StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count > 0) return exact[0];

        var partial = catalog.CharacterModels
            .Where(m => m.Stem.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            .GroupBy(m => m.Stem, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        if (partial.Count == 1) return partial[0];
        if (partial.Count == 0)
        {
            Console.Error.WriteLine($"! no model matching '{wanted}'. Try --list-models <text>.");
            return null;
        }
        Console.Error.WriteLine($"! '{wanted}' matches {partial.Count} models:");
        foreach (var m in partial.Take(12)) Console.Error.WriteLine("    " + m.Stem);
        return null;
    }

    private static GameCatalog OpenCatalog(string root, GameProfile profile, bool rescan)
    {
        var last = "";
        var progress = new Progress<ScanProgress>(p =>
        {
            if (p.Archive == last) return;
            last = p.Archive;
            Console.Error.WriteLine($"  indexing {p.Archive} "
                                    + $"({p.ArchiveIndex + 1}/{p.ArchiveCount}) "
                                    + $"models={p.Models} anims={p.Mtars}");
        });
        return GameCatalog.Open(root, profile, progress, rescan);
    }

    public static IEnumerable<(CatalogEntry Entry, MtarMatch Match)> CompatibleMtars(
        GameCatalog catalog, ModelContext context, int minMatch,
        CancellationToken token = default)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in catalog.Mtars)
        {
            token.ThrowIfCancellationRequested();
            if (!seen.Add(entry.Name)) continue;
            byte[] bytes;
            try { bytes = GameCatalog.Read(entry); } catch { continue; }
            var match = context.Check(bytes, minMatch);
            if (match.Fits(minMatch)) yield return (entry, match);
        }
    }

    public static void Log(string message) => Console.Error.WriteLine(message);
}
