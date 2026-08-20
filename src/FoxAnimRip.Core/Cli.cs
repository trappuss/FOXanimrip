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
  --all-sets           No model needed: rip EVERY animation archive in the game,
                       each bound to the model whose skeleton best fits it, laid
                       out mirroring where each set lives in the archives (implies
                       --tree). Writes all-sets-report.tsv listing every set, the
                       model it used, and any it could not place. --all-models
                       widens the candidate skeletons past character models to
                       everything (vehicles, gear, creatures). This is the
                       "rip everything" sweep; expect it to take a while.
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
  --dump-mog <dir>     Extract every motion-graph (.mog) file -- the blend/state
                       logic behind locomotion. They are hash-named (in no
                       dictionary), so they are found by extension code. Saves the
                       raw files and a mogs.tsv header summary.
  --ext-histogram <f>  Diagnostic: count every file in the archives by extension
                       code (the top bits of its path hash), to <f>. Settles what
                       file types an install actually holds -- e.g. whether .mog
                       is present at all.
  --inventory <dir>    Write models.tsv, textures.tsv and variations.tsv for the
                       whole game, plus a rip-all-models.bat that exports every
                       model listed. Honours --model-filter and --all-models.
                       This is how you enumerate every character asset and every
                       customisation option, including whether an option swaps a
                       texture or only sets a shader value.
  --rip-variations <f> Extract the files that form variations point at -- the
                       swap textures the inventory can only name. <f> filters by
                       variation name or path (e.g. mgo/fova/chara for the MGO
                       avatar customisation set). Needs --out; writes textures/
                       and a ripped-files.tsv mapping every variation to its
                       files, including the ones that could not be read.
  --filter-any a,b,c   Keep clips whose name contains any of these.
  --locomotion         Shorthand for --filter-any with the standard walk / run /
                       crouch / turn / idle name fragments.

Output:
  --out <folder>       Clips land in <out>/<mtar>/<clip>.fbx, plus an index.tsv
  --tree               Mirror each set's origin path instead of a flat folder:
                       <out>/Assets/.../<mtar>/<clip>.fbx. index.tsv gains a
                       sourcePath column either way. Implied by --all-sets.
  --filter <text>      Only clips whose name contains this.
  --min-match <n>      Bones a clip must drive to count. Default 8.
  --limit <n>          Stop after n clips.
  --list               Print what would be exported; write nothing.
  --measure            Measure locomotion instead of exporting FBX: writes
                       locomotion-params.tsv with each clip's root travel
                       distance, speed (m/s), net turn and turn rate -- the
                       authored numbers a 1:1 movement rebuild needs. Cycles
                       authored in place (the root barely moves; walk, run and
                       dash loops all are) also get gait_mps: the stance foot's
                       speed under the root, which IS the authored travel speed,
                       read off the curves.
  --analyze-locomotion <path>
                       Read locomotion-params.tsv files (a file, or a folder to
                       search; repeatable) and write cruise-table.tsv: median
                       measured speed and turn rate per family, gait, phase and
                       angle, plus the forward-loop cruise speeds on the
                       console. No game needed. --out picks the folder.
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
  --no-rig             Skip the rig search entirely. Right for model-only
                       exports of gear and attachments, which have no rig and
                       otherwise pay a full archive walk each to learn that.
  --skip-existing      With --export-model, skip characters whose FBX is
                       already in the output folder, so an interrupted batch
                       resumes instead of repeating.

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
  -V, --version        Print the build number and exit. Every run also prints
                       it as its first line.
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
        public string ListGrids = "", Inventory = "", RipVariations = "";
        public bool ExportModel, NoTextures, NoRig, SkipExisting;
        public bool AllSets;
        public string DumpMog = "";
        public string ExtHistogram = "";
        public List<string> AnalyzeLoco = new();
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
                    case "--inventory": a.Inventory = Next("--inventory"); break;
                    case "--rip-variations": a.RipVariations = Next("--rip-variations"); break;
                    case "--min-match": a.Rip.MinMatch = int.Parse(Next("--min-match")); break;
                    case "--limit": a.Rip.Limit = int.Parse(Next("--limit")); break;
                    case "--step": a.Rip.Step = Math.Max(1, int.Parse(Next("--step"))); break;
                    case "--fps": a.Rip.Fps = float.Parse(Next("--fps")); break;
                    case "--all": a.All = true; break;
                    case "--scan": a.All = true; break;   // old name
                    case "--all-sets": a.AllSets = true; break;
                    case "--tree": a.Rip.Tree = true; break;
                    case "--measure": a.Rip.Measure = true; break;
                    case "--dump-mog": a.DumpMog = Next("--dump-mog"); break;
                    case "--ext-histogram": a.ExtHistogram = Next("--ext-histogram"); break;
                    case "--analyze-locomotion":
                        a.AnalyzeLoco.Add(Next("--analyze-locomotion")); break;
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
                    case "--no-rig": a.NoRig = true; break;
                    case "--skip-existing": a.SkipExisting = true; break;
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
                    case "-h": case "--help":
                        Console.WriteLine($"foxanimrip {AppVersion.Current}");
                        Console.WriteLine();
                        Console.WriteLine(Usage);
                        return 0;
                    case "-V": case "--version":
                        Console.WriteLine(AppVersion.Current);
                        return 0;
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

        // First line of every run, on stderr so it never pollutes a piped list.
        // Terminal output gets pasted into bug reports without the build number
        // otherwise, and "which copy were you running?" is the first question.
        Log($"foxanimrip {AppVersion.Current}");

        try
        {
            // Reads measured tables, no game or FoxBrowser needed.
            if (a.AnalyzeLoco.Count > 0)
                return LocomotionAnalyze.Run(a.AnalyzeLoco, a.Rip.OutDir, Log);

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
        if (a.RipVariations.Length > 0)
        {
            if (a.Rip.OutDir.Length == 0)
            {
                Console.Error.WriteLine("! --rip-variations needs --out <folder>");
                return 64;
            }
            catalog = OpenCatalog(root, profile, a.Rescan);
            var dict = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(a.Fb))!, "dict");
            var got = VariationRip.Run(catalog, a.RipVariations, a.Rip.OutDir,
                                       archives, dict, Log);
            return got.Variations > 0 ? 0 : 3;
        }

        if (a.AllSets)
        {
            if (a.Rip.OutDir.Length == 0)
            {
                Console.Error.WriteLine("! --all-sets needs --out <folder>");
                return 64;
            }
            catalog = OpenCatalog(root, profile, a.Rescan);
            return AllSets(a, catalog, archives);
        }

        if (a.DumpMog.Length > 0)
        {
            catalog = OpenCatalog(root, profile, a.Rescan);
            return DumpMog(catalog, a.DumpMog);
        }

        if (a.ExtHistogram.Length > 0)
            return ExtHistogramCmd(archives, a.ExtHistogram);

        if (a.ListSets || a.ListClips.Length > 0 || a.ForMtar.Length > 0
            || a.ListGrids.Length > 0 || a.Inventory.Length > 0)
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
        //
        // Skipped wholesale with --no-rig: a model-only export never plays a
        // clip, and the rig search is by far the slowest step for a model the
        // cache has not seen. 177 gear pieces at a full archive walk each is
        // hours of work for rigs that gear does not have.
        foreach (var context in a.NoRig ? Enumerable.Empty<ModelContext>() : contexts)
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

        // --list-mtars and --why-mtar are answered below and write nothing, so
        // they must not be stopped here for lacking an export folder. That
        // happened: a --why-mtar run got as far as printing its rig line, then
        // died on "--out is required" for an export it was never going to do.
        var writesNothing = a.ListMtars || a.WhyMtar.Length > 0;
        if (a.Rip.OutDir.Length == 0 && !a.Rip.ListOnly && !writesNothing)
        {
            Console.Error.WriteLine("! --out <folder> is required");
            return 64;
        }

        // -- the model itself, if asked
        if (a.ExportModel)
        {
            var dictDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(a.Fb))!, "dict");
            // One archive index for the whole batch: opening the archives per
            // model made a 20-model batch pay the same startup cost 20 times.
            FoxBrowser.Rendering.FoxAssets shared = null;
            if (!a.NoTextures && contexts.Count > 1)
            {
                try
                {
                    // With texture archives, so streamed high-res mips assemble.
                    var withTex = archives.Concat(GameFinder.TextureArchivesIn(archives))
                                          .Distinct(StringComparer.OrdinalIgnoreCase);
                    shared = FoxBrowser.Rendering.FoxAssets.Open(dictDir, withTex);
                    shared.BuildIndex();
                }
                catch { shared = null; }
            }
            try
            {
                foreach (var context in contexts)
                {
                    var dir = contexts.Count > 1
                        ? Path.Combine(a.Rip.OutDir, RipJob.Safe(context.Name))
                        : a.Rip.OutDir;
                    // Skip a finished model -- but not one exported before the
                    // texture-role sidecar existed. Re-exporting those (they have
                    // an FBX and a textures folder but no _maps.tsv) is how a rip
                    // picks up the sidecar the Blender add-on needs, without a
                    // full from-scratch redo.
                    if (a.SkipExisting && !a.NoTextures)
                    {
                        var stem = RipJob.Safe(context.Name);
                        var fbx = File.Exists(Path.Combine(dir, stem + ".fbx"));
                        var texDir = Path.Combine(dir, context.Name + "_textures");
                        var hasTex = Directory.Exists(texDir);
                        var hasSidecar = File.Exists(Path.Combine(dir, context.Name + "_maps.tsv"));
                        if (fbx && (!hasTex || hasSidecar))
                        {
                            Log($"  {context.Name}: already exported, skipped");
                            continue;
                        }
                    }
                    else if (a.SkipExisting
                        && File.Exists(Path.Combine(dir, RipJob.Safe(context.Name) + ".fbx")))
                    {
                        Log($"  {context.Name}: already exported, skipped");
                        continue;
                    }
                    try
                    {
                        ModelExport.Run(context, dir, archives, dictDir,
                                        withTextures: !a.NoTextures, withSource: true, Log,
                                        sharedAssets: shared);
                    }
                    catch (Exception ex)
                    {
                        Log($"! {context.Name}: model export failed ({ex.Message})");
                    }
                }
            }
            finally { shared?.Dispose(); }
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

        // Animation sets are resolved through the catalog, which a --model run
        // (no --character) would otherwise never open -- and then every set
        // lookup below would fault on a null.
        if (catalog is null
            && (wantAll || namedMtars.Any(m => !File.Exists(m))))
            catalog = OpenCatalog(root, profile, a.Rescan);
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
                    sources.Add(new MtarSource(hit.Name, () => GameCatalog.Read(captured),
                                               captured.Path));
                }
            }

            if (wantAll)
            {
                foreach (var (entry, _) in CompatibleMtars(sharedCatalog, context, minMatch, token))
                {
                    if (!seen.Add(entry.Name)) continue;
                    var captured = entry;
                    sources.Add(new MtarSource(entry.Name, () => GameCatalog.Read(captured),
                                               captured.Path));
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
        if (a.Inventory.Length > 0) return WriteInventory(a, catalog);

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
    /// Rip every animation archive in the game, each bound to the model whose
    /// skeleton best fits it, laid out mirroring where each set lives in the
    /// archives. This is the "no stone unturned" sweep: rather than pick one base
    /// model and take only what fits it, it pairs every set with a compatible
    /// skeleton and reports what it could not place.
    ///
    /// Every candidate model's skeleton is read once; each set is then assigned
    /// to the single best-covering skeleton by bone overlap (ties go to the
    /// leaner skeleton, the one built for that animation rather than a superset).
    /// Sets that share no bones with any model -- typically ones addressing rig
    /// units rather than bones -- are listed as uncovered rather than dropped
    /// silently. A per-set coverage report is written next to the clips.
    /// </summary>
    private static int AllSets(Args a, GameCatalog catalog, IReadOnlyList<string> archives)
    {
        var minMatch = a.Rip.MinMatch;
        var outDir = a.Rip.OutDir;
        Directory.CreateDirectory(outDir);

        // A re-run must not append to a previous run's index; start it clean.
        var indexPath = Path.Combine(outDir, "index.tsv");
        var reportPath = Path.Combine(outDir, "all-sets-report.tsv");
        try { if (File.Exists(indexPath)) File.Delete(indexPath); } catch { }

        // 1. Every candidate model's skeleton, read once. Character models by
        //    default; --all-models widens to everything the game ships.
        var pool = a.CharactersOnly ? catalog.CharacterModels : catalog.Models;
        var seenModel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var models = new List<(CatalogEntry Entry, HashSet<uint> Bones)>();
        foreach (var entry in pool)
        {
            if (!seenModel.Add(entry.Stem)) continue;
            try
            {
                var info = RipJob.Inspect(GameCatalog.Read(entry));
                if (info.BoneCount > 0 && info.BoneHashes.Count > 0)
                    models.Add((entry, info.BoneHashes));
            }
            catch { /* a model that will not parse cannot be the answer */ }
        }
        Log($"{models.Count} candidate skeleton(s) read"
            + (a.CharactersOnly ? " (character models; --all-models widens this)" : ""));
        if (models.Count == 0)
        {
            Console.Error.WriteLine("! no model skeletons could be read to bind animations to");
            return 3;
        }

        // 2. Every animation archive, one copy each -- the highest patch layer.
        var sets = catalog.Mtars
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(m => m.Layer).First())
            .OrderBy(m => m.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Log($"{sets.Count} animation archive(s) to place");

        var fingerprint = GameCatalog.FingerprintOf(archives);
        var assigned = new Dictionary<string, List<CatalogEntry>>(StringComparer.OrdinalIgnoreCase);
        var modelByStem = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (e, _) in models) modelByStem[e.Stem] = e;

        void AssignTo(string modelStem, CatalogEntry set)
        {
            if (!assigned.TryGetValue(modelStem, out var l)) assigned[modelStem] = l = new();
            l.Add(set);
        }
        string Row(CatalogEntry s, object clips, int setBones, string model,
                   int matched, int cov, string status)
            => $"{s.Stem}\t{s.Path}\t{clips}\t{setBones}\t{model}\t{matched}\t{cov}%\t{status}";

        var rows = new Dictionary<CatalogEntry, string>();
        var deferred = new List<(CatalogEntry Set, int Clips, int Inter)>();

        // 3. Phase 1 -- cheap. A set that carries a skeleton list matches a model
        //    by bone-hash overlap. Rig-driven sets name rig units, not bones, so
        //    they intersect nothing here; those are deferred to the rigged check.
        var done = 0;
        foreach (var set in sets)
        {
            if (++done % 100 == 0) Log($"  scanned {done}/{sets.Count}...");

            byte[] bytes;
            AnimSetInfo info;
            try
            {
                bytes = GameCatalog.Read(set);
                info = SetSurvey.Describe(bytes, set.Name, set.Path, set.ArchiveName);
            }
            catch
            {
                rows[set] = Row(set, "?", 0, "", 0, 0, "UNREADABLE");
                continue;
            }

            string bestModel = null;
            int bestMatched = 0, bestBones = int.MaxValue;
            foreach (var (entry, bones) in models)
            {
                var matched = 0;
                foreach (var h in info.BoneHashes) if (bones.Contains(h)) matched++;
                if (matched > bestMatched
                    || (matched == bestMatched && matched > 0 && bones.Count < bestBones))
                {
                    bestModel = entry.Stem;
                    bestMatched = matched;
                    bestBones = bones.Count;
                }
            }

            if (bestModel is not null && bestMatched >= minMatch)
            {
                AssignTo(bestModel, set);
                var cov = bestMatched * 100 / Math.Max(1, info.BoneHashes.Count);
                rows[set] = Row(set, info.Clips, info.BoneHashes.Count, bestModel,
                                bestMatched, cov, "assigned");
            }
            else
            {
                deferred.Add((set, info.Clips, bestMatched));
                rows[set] = Row(set, info.Clips, info.BoneHashes.Count, "", bestMatched,
                                bestMatched * 100 / Math.Max(1, info.BoneHashes.Count),
                                "UNCOVERED");
            }
        }
        Log($"phase 1: {assigned.Values.Sum(v => v.Count)} placed by bone overlap, "
            + $"{deferred.Count} need the rigged check");

        // 4. Phase 2 -- rigged. The deferred sets are mostly rig-driven (player
        //    and character motion). Build the skeletons the most models share --
        //    the player/human rig ranks first -- resolve each rig once, then ask
        //    each rigged model whether it can play the set, the same decode-and-
        //    resolve the normal --all path uses. One shared rig cache serves every
        //    game and motion, so the cold rig searches are paid once overall.
        const int anchorCap = 16;
        var built = new Dictionary<string, ModelContext>(StringComparer.OrdinalIgnoreCase);
        if (deferred.Count > 0)
        {
            var anchors = SelectAnchors(models, fingerprint, anchorCap);
            Log($"phase 2: rigging up to {anchors.Count} anchor skeleton(s) "
                + $"to place {deferred.Count} deferred set(s)");
            var anchorCtx = new List<ModelContext>();
            foreach (var entry in anchors)
            {
                try
                {
                    var ctx = ModelContext.Create(entry.Stem, GameCatalog.Read(entry));
                    var (choice, frdv) = RigCache.Resolve(archives, fingerprint, entry.Stem,
                                                          ctx.BoneHashes, "", _ => { });
                    ctx.Attach(choice, frdv);
                    anchorCtx.Add(ctx);
                    built[entry.Stem] = ctx;
                    Log($"  anchor ready: {entry.Stem} ({ctx.BoneCount} bones)");
                }
                catch (Exception ex) { Log($"  ! anchor {entry.Stem}: {ex.Message}"); }
            }

            var placed2 = 0;
            var chec01 = 0;
            foreach (var (set, clips, inter) in deferred)
            {
                if (++chec01 % 50 == 0) Log($"  rigged-checked {chec01}/{deferred.Count}...");
                byte[] bytes;
                try { bytes = GameCatalog.Read(set); } catch { continue; }

                string bestModel = null;
                var bestMatched = inter;
                foreach (var ctx in anchorCtx)
                {
                    MtarMatch match;
                    try { match = ctx.Check(bytes, minMatch); } catch { continue; }
                    if (match.MatchedBones > bestMatched)
                    { bestModel = ctx.Name; bestMatched = match.MatchedBones; }
                    if (match.MatchedBones >= minMatch)
                    { bestModel = ctx.Name; bestMatched = match.MatchedBones; break; }
                }

                if (bestModel is not null && bestMatched >= minMatch)
                {
                    AssignTo(bestModel, set);
                    rows[set] = Row(set, clips, 0, bestModel, bestMatched, 0, "assigned (rig)");
                    placed2++;
                }
                else
                {
                    var why = bestMatched > 0 ? $"best {bestMatched} < min {minMatch}"
                                              : "no model can play it";
                    rows[set] = Row(set, clips, 0, "", bestMatched, 0, $"UNCOVERED: {why}");
                }
            }
            Log($"phase 2: {placed2} more set(s) placed");
        }

        // 5. Coverage report, in archive order.
        var report = new List<string>
            { "mtar\tpath\tclipsInSet\tsetBones\tmodel\tmatchedBones\tcoverage\tstatus" };
        foreach (var set in sets)
            if (rows.TryGetValue(set, out var r)) report.Add(r);
        File.WriteAllLines(reportPath, report);
        var totalAssigned = assigned.Values.Sum(v => v.Count);
        Log($"{totalAssigned} set(s) assigned to {assigned.Count} skeleton(s), "
            + $"{sets.Count - totalAssigned} uncovered -- see all-sets-report.tsv");

        // 6. Rip. One model at a time, all its sets, into the shared origin tree.
        a.Rip.Tree = true;
        a.Rip.IndexAppend = true;
        var totalExported = 0;
        var mi = 0;
        foreach (var pair in assigned.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            mi++;
            var modelStem = pair.Key;
            var list = pair.Value;
            Log($"--- {modelStem}: {list.Count} set(s)  ({mi} of {assigned.Count}) ---");

            if (!built.TryGetValue(modelStem, out var context))
            {
                try
                {
                    context = ModelContext.Create(modelStem, GameCatalog.Read(modelByStem[modelStem]));
                    var (choice, frdv) = RigCache.Resolve(archives, fingerprint, modelStem,
                                                          context.BoneHashes, "",
                                                          msg => Log($"  {modelStem}: {msg}"));
                    context.Attach(choice, frdv);
                    built[modelStem] = context;
                }
                catch (Exception ex)
                {
                    Log($"! {modelStem}: could not prepare its rig ({ex.Message}); its sets are skipped");
                    continue;
                }
            }

            var input = context.ToInput();
            foreach (var set in list)
            {
                var captured = set;
                input.Sources.Add(new MtarSource(captured.Name,
                    () => GameCatalog.Read(captured), captured.Path));
            }

            var one = RipJob.Run(input, a.Rip, Log);
            totalExported += one.Exported;
        }

        Log($"all-sets done: {totalExported} clip(s) into {outDir}");
        Log($"coverage report: {reportPath}");
        return totalExported > 0 ? 0 : 3;
    }

    /// <summary>
    /// The skeletons to rig-resolve for the rigged compatibility check, best
    /// first. Models are grouped by their exact skeleton; each group's
    /// representative is the richest model in it. Groups are ordered so the ones
    /// whose rig is already cached come first (cheap), then the ones the most
    /// models share -- which puts the player/human skeleton near the top, since
    /// most character models use it -- then the richest. Capped, because each
    /// uncached rig means an archive walk.
    /// </summary>
    private static List<CatalogEntry> SelectAnchors(
        List<(CatalogEntry Entry, HashSet<uint> Bones)> models, string fingerprint, int cap)
    {
        var groups = new Dictionary<string, (CatalogEntry Rep, int RepBones, int Count)>();
        foreach (var (entry, bones) in models)
        {
            var sig = SkeletonSig(bones);
            if (groups.TryGetValue(sig, out var g))
            {
                g.Count++;
                if (bones.Count > g.RepBones) { g.Rep = entry; g.RepBones = bones.Count; }
                groups[sig] = g;
            }
            else groups[sig] = (entry, bones.Count, 1);
        }

        return groups.Values
            .OrderByDescending(g => RigCache.Load(g.Rep.Stem, fingerprint) is not null)
            .ThenByDescending(g => g.Count)
            .ThenByDescending(g => g.RepBones)
            .Take(cap)
            .Select(g => g.Rep)
            .ToList();
    }

    /// <summary>A stable key for a skeleton: its sorted bone hashes, hashed.</summary>
    private static string SkeletonSig(HashSet<uint> bones)
    {
        var arr = new uint[bones.Count];
        bones.CopyTo(arr);
        Array.Sort(arr);
        var hc = new HashCode();
        hc.Add(arr.Length);
        foreach (var h in arr) hc.Add(h);
        return hc.ToHashCode().ToString();
    }

    /// <summary>Fox path-hash extension codes to names, for the histogram diag.</summary>
    private static readonly Dictionary<uint, string> ExtNames = new()
    {
        [71] = "gskl", [239] = "qar", [479] = "phsd", [562] = "evf", [685] = "ftex",
        [783] = "lani", [796] = "lua", [1172] = "geom", [1591] = "fox", [1682] = "sim",
        [1752] = "bnk", [2276] = "frig", [2311] = "aib", [2481] = "vfxdata", [2609] = "fox2",
        [2629] = "fpk", [3035] = "des", [3089] = "fv2", [3131] = "fsm", [3296] = "mtar",
        [3527] = "spch", [3609] = "json", [3832] = "subp", [4235] = "fova", [4244] = "fmdl",
        [4752] = "mog", [5180] = "nta", [5387] = "clo", [5527] = "ph", [5533] = "xml",
        [5719] = "txt", [5727] = "pftxs", [5785] = "fclo", [5980] = "sbp", [6407] = "sani",
        [6588] = "frdv", [6589] = "lng", [6686] = "aig", [7164] = "htre", [7189] = "parts",
        [7314] = "tgt", [7347] = "ftexs", [7359] = "gpfp", [7415] = "fsml", [7594] = "fpkd",
        [7684] = "nav2", [7741] = "lba", [8069] = "mas", [8074] = "gani",
    };

    /// <summary>
    /// What file types the archives actually hold, by extension code (the top
    /// bits of each entry's path hash). A diagnostic: it settles whether a given
    /// extension -- .mog above all -- is present at all, without any dictionary.
    /// </summary>
    private static int ExtHistogramCmd(IReadOnlyList<string> archives, string outFile)
    {
        var counts = new Dictionary<uint, int>();
        var sample = new Dictionary<uint, string>();
        var total = 0;
        GameCatalog.WalkFileHashes(archives, (name, hash) =>
        {
            var ext = (uint)((hash >> 51) & 0x1FFF);
            counts[ext] = counts.GetValueOrDefault(ext) + 1;
            if (!sample.ContainsKey(ext)) sample[ext] = name;
            total++;
        });

        var rows = new List<string> { "extCode\text\tcount\tsampleName" };
        foreach (var kv in counts.OrderByDescending(k => k.Value))
            rows.Add($"{kv.Key}\t{(ExtNames.TryGetValue(kv.Key, out var n) ? n : "?")}\t"
                     + $"{kv.Value}\t{sample[kv.Key]}");
        File.WriteAllLines(outFile, rows);

        Log($"{total} file(s), {counts.Count} distinct extension code(s) -> {outFile}");
        Log($".mog (4752): {counts.GetValueOrDefault(4752u)}  |  "
            + $".mas (8069): {counts.GetValueOrDefault(8069u)}  |  "
            + $".fsm (3131): {counts.GetValueOrDefault(3131u)}  |  "
            + $".fsml (7415): {counts.GetValueOrDefault(7415u)}");
        return 0;
    }

    /// <summary>
    /// Extract every motion-graph (.mog) file and index them. These are the
    /// blend/state logic behind locomotion; they are hash-named (in no
    /// dictionary), so the catalogue finds them by extension code. The raw bytes
    /// are saved so the format can be parsed properly, and a first-bytes/header
    /// summary is written for each -- deliberately not over-interpreted here,
    /// since the graph body format is only partly documented.
    /// </summary>
    private static int DumpMog(GameCatalog catalog, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var rawDir = Path.Combine(outDir, "raw");
        Directory.CreateDirectory(rawDir);

        var rows = new List<string>
        {
            "name\tsize\tarchive\tlayers\tgraphCount\tpad@26\tpad@28\tfirst32hex"
        };
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var done = 0;

        foreach (var e in catalog.Mogs.OrderByDescending(m => m.Layer))
        {
            byte[] b;
            try { b = GameCatalog.Read(e); }
            catch (Exception ex) { Log($"! {e.Name}: cannot read ({ex.Message})"); continue; }

            var stem = RipJob.Safe(e.Stem);
            var name = stem;
            var i = 1;
            while (!taken.Add(name)) name = $"{stem}-{i++}";
            File.WriteAllBytes(Path.Combine(rawDir, name + ".mog"), b);

            // Best-effort header read, reported not trusted: the exact offset of
            // the 0xA7A7 padding marker disambiguates the header layout, so both
            // candidate positions are printed for the real files to settle it.
            var layers = b.Length > 24 ? b[24] : 0;
            var graphs = b.Length >= 32 ? BitConverter.ToInt32(b, 28) : 0;
            var pad26 = b.Length >= 28 && b[26] == 0xA7 && b[27] == 0xA7;
            var pad28 = b.Length >= 30 && b[28] == 0xA7 && b[29] == 0xA7;
            var hex = Convert.ToHexString(b.AsSpan(0, Math.Min(32, b.Length)));
            rows.Add($"{e.Name}\t{b.Length}\t{e.ArchiveName}\t{layers}\t{graphs}\t"
                     + $"{pad26}\t{pad28}\t{hex}");
            done++;
            if (done % 50 == 0) Log($"  {done} .mog extracted...");
        }

        File.WriteAllLines(Path.Combine(outDir, "mogs.tsv"), rows);
        Log($"{done} motion-graph file(s) extracted to {rawDir}");
        Log($"index: {Path.Combine(outDir, "mogs.tsv")}");
        return done > 0 ? 0 : 3;
    }

    /// <summary>
    /// Write out everything the game has: models, textures, and the variations
    /// that change how a model looks.
    /// </summary>
    private static int WriteInventory(Args a, GameCatalog catalog)
    {
        var last = -1;
        var progress = new Progress<(int Done, int Total, string Name)>(p =>
        {
            var percent = p.Total > 0 ? p.Done * 100 / p.Total : 0;
            if (percent == last || percent % 5 != 0) return;
            last = percent;
            Log($"  {percent}% ({p.Done}/{p.Total})");
        });

        Log(a.CharactersOnly
            ? "reading character models (--all-models widens this)"
            : "reading every model in the game");
        var counts = Inventory.Write(catalog, a.Inventory, a.ModelFilter,
                                     a.CharactersOnly, Log, progress);

        var names = new List<string>();
        try
        {
            foreach (var line in File.ReadLines(Path.Combine(a.Inventory, "models.tsv")).Skip(1))
            {
                var tab = line.IndexOf('\t');
                if (tab > 0) names.Add(line[..tab]);
            }
            Inventory.WriteRipScript(a.Inventory, catalog.ProfileId, catalog.Root, names);
        }
        catch (Exception ex) { Log("! could not write the rip script: " + ex.Message); }

        // The navigable catalogue: same data, as one searchable HTML page with a
        // built-in how-to. A normal output of every inventory run.
        try
        {
            var gameName = GameProfile.ById(catalog.ProfileId).DisplayName;
            CatalogHtml.Write(a.Inventory, catalog, gameName, Log);
        }
        catch (Exception ex) { Log("! could not write catalog.html: " + ex.Message); }

        Console.WriteLine();
        Console.WriteLine($"models.tsv          {counts.Models} model(s), "
                          + $"{counts.Materials} material(s)");
        Console.WriteLine($"textures.tsv        {counts.Textures} texture reference(s)");
        Console.WriteLine($"variations.tsv      {counts.Variations} form variation(s): "
                          + $"{counts.Swaps} texture swap(s), {counts.Parameters} "
                          + $"material parameter(s), {counts.Attachments} attachment(s)");
        Console.WriteLine($"rip-all-models.bat  exports the {names.Count} model(s) listed");

        if (counts.Unresolved > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"{counts.Unresolved} name(s) could not be resolved and appear as "
                              + "hex. The game's dictionaries do not cover everything; those "
                              + "entries are real, just unnamed.");
        }

        Console.WriteLine();
        Console.WriteLine("In variations.tsv: a textureSwap row means the option points at a "
                          + "different texture file. A materialParameter row means it only "
                          + "changes a shader value. Something like a skin tone can be built "
                          + "either way -- this is where you find out which.");
        return counts.Models > 0 ? 0 : 3;
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
        var catalog = GameCatalog.Open(root, profile, progress, rescan);
        if (GameCatalog.Stale)
            Log("the cached index predates a file type this version collects, "
                + "so it was rebuilt once");
        return catalog;
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
