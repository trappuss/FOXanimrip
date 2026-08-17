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
  --list-rigs          Print the rigs that fit this character, best first, and
                       exit. Use this when clips come out distorted: the top row
                       is the rig that will be used.

  Working animation-first, when you know the animations and want the model:
  --list-sets [text]   Every animation archive in the game, with clip and bone
                       counts. No character needed.
  --list-clips <set>   The clip names inside one archive. Honours --filter-any
                       and --locomotion.
  --why-mtar <set>     Why one archive is or is not offered for the chosen
                       character: indexed, readable, skeleton list, bones matched.
  --for-mtar <set>     The models that can play an archive, best first. This is
                       how you find a base model for a whole animation set.
  --all-models         Widen --for-mtar past character models to everything.
  --model-filter <t>   Narrow --for-mtar to models whose name or path has this.
                       Worth using: a full Phantom Pain sweep is thousands of
                       models and takes a while.
  --filter-any a,b,c   Keep clips whose name contains any of these.
  --locomotion         Shorthand for --filter-any with the standard walk / run /
                       crouch / turn / idle name fragments.

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
  --root-motion        Keep the root's travel. Off by default, which bakes every
                       clip on the spot: better for an Action library, but a
                       character animated from it never leaves the origin.
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
        public bool All, Rescan, Refresh, ListGames, ListModels, ListMtars, ListRigs, Where;
        public bool ListSets, CharactersOnly = true;
        public string SetsFilter = "", ListClips = "", ForMtar = "", ModelFilter = "", WhyMtar = "";
        public string ListGrids = "";
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
                    case "--filter-any":
                        foreach (var part in Next("--filter-any").Split(',',
                                     StringSplitOptions.RemoveEmptyEntries
                                     | StringSplitOptions.TrimEntries))
                            a.Rip.Filters.Add(part);
                        break;
                    case "--locomotion": a.Rip.Filters.AddRange(Locomotion.Tokens); break;
                    case "--root-motion": a.Rip.RootMotion = true; break;
                    case "--list-grids": a.ListGrids = Next("--list-grids"); break;
                    case "--grid": a.Rip.GridOnly = true; break;
                    case "--list-sets":
                        a.ListSets = true;
                        if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                            a.SetsFilter = args[++i];
                        break;
                    case "--list-clips": a.ListClips = Next("--list-clips"); break;
                    case "--for-mtar": a.ForMtar = Next("--for-mtar"); break;
                    case "--why-mtar": a.WhyMtar = Next("--why-mtar"); break;
                    case "--all-models": a.CharactersOnly = false; break;
                    case "--model-filter": a.ModelFilter = Next("--model-filter"); break;
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
                    case "--list-rigs": a.ListRigs = true; break;
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

        // Browsing animation archives does not need a character chosen: these
        // are the commands for when you have the animations and are looking for
        // the model, rather than the other way round.
        if (a.ListSets || a.ListClips.Length > 0 || a.ForMtar.Length > 0 || a.ListGrids.Length > 0)
        {
            catalog = OpenCatalog(root, profile, a.Rescan);
            return BrowseSets(a, catalog, archives);
        }

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

        if (a.ListRigs)
        {
            foreach (var context in contexts)
            {
                Console.WriteLine($"# {context.Name}: {context.BoneCount} bones");
                Console.WriteLine("drives\trig bones\tof the rig\tof the model\tname fit\tpath");
                var ranked = Sources.RankFrigs(archives, context.BoneHashes,
                                               context.Name, a.ModelPath);
                foreach (var rig in ranked.Take(15))
                    Console.WriteLine($"{rig.Matched}\t{rig.RigBones}\t{rig.Precision:P0}\t"
                                      + $"{rig.Coverage:P0}\t{rig.Affinity:0.00}\t{rig.Path}");
                if (ranked.Count == 0) Console.WriteLine("(no rig names any of these bones)");
            }
            return 0;
        }

        // -- rig and help bones, per character
        foreach (var context in contexts)
        {
            var frdv = a.FrdvPath.Length > 0 ? File.ReadAllBytes(a.FrdvPath) : null;

            if (a.FrigPath.Length > 0)
            {
                context.Attach(File.ReadAllBytes(a.FrigPath), frdv);
            }
            else
            {
                var (choice, foundFrdv) = RigCache.Resolve(
                    archives, GameCatalog.FingerprintOf(archives), context.Name,
                    context.BoneHashes, a.ModelPath,
                    m => Log($"  {context.Name}: {m}"));
                context.Attach(choice, frdv ?? foundFrdv);
            }
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

        if (a.WhyMtar.Length > 0)
            return WhyMtar(a, catalog ??= OpenCatalog(root, profile, a.Rescan), contexts);

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

    /// <summary>
    /// The animation-first commands: list archives, list their clips, and find
    /// the models that can play one.
    ///
    /// None of them need a character, which is the point. When the goal is "every
    /// locomotion animation for the player", the archive is the thing you know
    /// and the model is what you are looking for.
    /// </summary>
    private static int BrowseSets(Args a, GameCatalog catalog, List<string> archives)
    {
        if (a.ListSets)
        {
            var sets = SetSurvey.Sets(catalog, a.SetsFilter, deep: true,
                                      progress: name => Log($"  reading {name}"));
            // "tracks" rather than "bones": a v2 archive's table lists the rig
            // units its clips address, not skeleton bones, and calling those
            // bones invites exactly the wrong conclusion when the number is 18.
            Console.WriteLine("clips\ttracks\ttable\tlayer\tname\tarchive\tpath");
            foreach (var set in sets.OrderByDescending(s => s.Clips))
                Console.WriteLine($"{set.Clips}\t{set.BoneHashes.Count}\t"
                                  + $"{(set.HasSkeletonList ? "yes" : "no")}\t"
                                  + $"{set.Layer}\t"
                                  + $"{set.Stem}\t{set.ArchiveName}\t{set.Path}");
            if (sets.Count == 0)
                Console.Error.WriteLine("! no animation archive matched"
                                        + (a.SetsFilter.Length > 0 ? $" '{a.SetsFilter}'" : ""));
            return sets.Count > 0 ? 0 : 3;
        }

        if (a.ListGrids.Length > 0)
        {
            var entry = FindSet(catalog, a.ListGrids);
            if (entry is null) return 66;
            var set = SetSurvey.Describe(GameCatalog.Read(entry), entry.Name, entry.Path,
                                         entry.ArchiveName, withClipNames: true);
            var grids = LocomotionGrids.Find(set.ClipNames);
            Console.WriteLine("clips\tst\tlp\ttn\ted\tcomplete\tangles\tfamily");
            foreach (var g in grids)
                Console.WriteLine($"{g.Clips.Count}\t{g.Count("st")}\t{g.Count("lp")}\t"
                                  + $"{g.Count("tn")}\t{g.Count("ed")}\t"
                                  + $"{(g.Complete ? "yes" : "no")}\t"
                                  + $"{string.Join(" ", g.Angles)}\t{g.Family}_{g.Verb}");
            var covered = grids.Sum(g => g.Clips.Count);
            Log($"{grids.Count} grid(s), {covered} of {set.Clips} clip(s) in {set.Stem}");
            return 0;
        }

        if (a.ListClips.Length > 0)
        {
            var entry = FindSet(catalog, a.ListClips);
            if (entry is null) return 66;
            var set = SetSurvey.Describe(GameCatalog.Read(entry), entry.Name, entry.Path,
                                         entry.ArchiveName, withClipNames: true);
            // The leading number is the clip's position in the archive, which is
            // how the community's hand-written description lists are keyed --
            // paste them side by side and the abbreviations stop being a puzzle.
            Console.WriteLine("#\tclip\tpath");
            var shown = 0;
            for (var i = 0; i < set.ClipNames.Count; i++)
            {
                var clip = set.ClipNames[i];
                if (!a.Rip.Wanted(clip)) continue;
                var full = i < set.ClipPaths.Count ? set.ClipPaths[i] : "";
                Console.WriteLine($"{i:0000}\t{clip}\t{full}");
                shown++;
            }
            Log($"{shown} of {set.Clips} clip(s) in {set.Stem}");
            return 0;
        }

        // --for-mtar
        var target = FindSet(catalog, a.ForMtar);
        if (target is null) return 66;
        var info = SetSurvey.Describe(GameCatalog.Read(target), target.Name, target.Path,
                                      target.ArchiveName);
        if (info.BoneHashes.Count == 0)
        {
            Console.Error.WriteLine($"! {target.Name} names no bones this tool can read");
            return 3;
        }
        Log($"{info.Stem}: {info.Clips} clip(s), {info.BoneHashes.Count} bone(s)"
            + (info.HasSkeletonList ? "" : " (read from the clips; no skeleton list)"));
        Log(a.CharactersOnly
            ? "ranking character models... (--all-models widens this)"
            : "ranking every model in the game...");

        var last = -1;
        var progress = new Progress<(int Done, int Total, string Name)>(p =>
        {
            var percent = p.Total > 0 ? p.Done * 100 / p.Total : 0;
            if (percent == last || percent % 10 != 0) return;
            last = percent;
            Log($"  {percent}% ({p.Done}/{p.Total})");
        });

        var fits = SetSurvey.ModelsFor(catalog, info, a.ModelFilter, a.CharactersOnly,
                                       progress, default, GameCatalog.Read(target),
                                       archives, m => Log("  " + m));
        Console.WriteLine("matched\tof\tcoverage\tmodel bones\tmodel\tarchive\tpath");
        foreach (var fit in fits.Take(30))
            Console.WriteLine($"{fit.Matched}\t{fit.SetBones}\t{fit.Coverage:P0}\t"
                              + $"{fit.Bones}\t{fit.Name}\t{fit.ArchiveName}\t{fit.Path}");
        if (fits.Count == 0)
            Console.Error.WriteLine("! no model shares a bone with this archive");
        return fits.Count > 0 ? 0 : 3;
    }

    /// <summary>
    /// Why a particular animation archive is, or is not, offered for a character.
    ///
    /// "It is not in the list" has at least four different causes -- the archive
    /// is not indexed, it cannot be read, it has no skeleton table and the clip
    /// fallback found nothing, or it simply drives too few of this skeleton's
    /// bones -- and they call for four different responses. Guessing between them
    /// from the outside is miserable, so this prints which one it is.
    /// </summary>
    private static int WhyMtar(Args a, GameCatalog catalog, List<ModelContext> contexts)
    {
        var hits = catalog.Mtars
            .Where(m => m.Stem.Contains(a.WhyMtar, StringComparison.OrdinalIgnoreCase)
                        || m.Path.Contains(a.WhyMtar, StringComparison.OrdinalIgnoreCase))
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (hits.Count == 0)
        {
            Console.WriteLine($"'{a.WhyMtar}' is not in the index at all.");
            Console.WriteLine($"  {catalog.Mtars.Count} animation archive(s) were indexed "
                              + $"from {catalog.Scanned.Count} archive(s)"
                              + (catalog.Complete ? "." : ", and the scan is INCOMPLETE."));
            Console.WriteLine(catalog.Complete
                ? "  Either the name is different -- try --list-sets -- or the archive it "
                  + "lives in was not read. --rescan re-reads everything."
                : "  Finish the scan first: run again, or --rescan to start over.");
            return 3;
        }

        foreach (var entry in hits)
        {
            Console.WriteLine($"{entry.Stem}");
            Console.WriteLine($"  archive : {entry.ArchiveName}");
            Console.WriteLine($"  path    : {entry.Path}");

            byte[] bytes;
            try { bytes = GameCatalog.Read(entry); }
            catch (Exception ex)
            {
                Console.WriteLine($"  ! cannot be read: {ex.Message}");
                continue;
            }

            var info = SetSurvey.Describe(bytes, entry.Name, entry.Path, entry.ArchiveName);
            Console.WriteLine($"  clips   : {info.Clips}");
            Console.WriteLine($"  bones   : {info.BoneHashes.Count}"
                              + (info.HasSkeletonList
                                  ? " (from its own skeleton list)"
                                  : " (read from its clips; the header has no skeleton list, "
                                    + "which is why FoxBrowser's own check finds nothing)"));

            foreach (var context in contexts)
            {
                var match = context.Check(bytes, a.Rip.MinMatch);
                var verdict = match.Fits(a.Rip.MinMatch)
                    ? "offered"
                    : $"NOT offered -- under the --min-match {a.Rip.MinMatch} threshold";
                Console.WriteLine($"  vs {context.Name}: {match.MatchedBones} bone(s) matched "
                                  + $"of {context.BoneCount} -> {verdict}");
                if (!match.Fits(a.Rip.MinMatch))
                    Console.WriteLine("      Naming it with --mtar exports it anyway; the "
                                      + "per-clip check still applies, so lower --min-match "
                                      + "if every clip is skipped.");
            }
        }
        return 0;
    }

    /// <summary>
    /// An .mtar by exact stem, then by substring -- newest patch layer first.
    ///
    /// A file can exist in several archives at once; the game reads the latest,
    /// so this does too, and says when the copies disagree rather than picking
    /// one quietly.
    /// </summary>
    private static CatalogEntry FindSet(GameCatalog catalog, string wanted)
    {
        var exact = catalog.Mtars
            .Where(m => string.Equals(m.Stem, wanted, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.Layer).ThenByDescending(m => m.Size).ToList();
        if (exact.Count > 0)
        {
            if (exact.Count > 1)
                Log($"{wanted}: {exact.Count} copies; using the one in "
                    + $"{exact[0].ArchiveName} (layer {exact[0].Layer}), over "
                    + string.Join(", ", exact.Skip(1).Select(e => e.ArchiveName)));
            return exact[0];
        }

        var hits = catalog.Mtars
            .Where(m => m.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                        || m.Path.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            .GroupBy(m => m.Stem, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(m => m.Layer)
                          .ThenByDescending(m => m.Size).First())
            .ToList();

        if (hits.Count == 1) return hits[0];
        if (hits.Count == 0)
        {
            Console.Error.WriteLine($"! no animation archive matching '{wanted}'. "
                                    + "Try --list-sets to see what there is.");
            return null;
        }
        Console.Error.WriteLine($"! '{wanted}' matches {hits.Count} archives:");
        foreach (var hit in hits.Take(20)) Console.Error.WriteLine("    " + hit.Stem);
        return null;
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
