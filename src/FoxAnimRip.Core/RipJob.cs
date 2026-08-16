// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Text;
using FoxBrowser.Interop;
using FoxBrowser.Models;
using FoxBrowser.Models.Anim;
using FoxBrowser.Models.Export;
using FoxBrowser.Models.Export.Fbx;
using FoxBrowser.Models.Fmdl;
using MgsvModBldr.Tools.Browse;

namespace FoxAnimRip;

public sealed class RipOptions
{
    public string OutDir = "";
    public string Filter = "";
    public int MinMatch = 8;
    public int Limit;
    public int Step = 1;
    public float Fps = 59.94f;
    public bool WithMesh;
    public bool ListOnly;
    public bool NoFbxFix;
    public bool KeepStatic;
    public bool Quiet;
    /// <summary>Fold near-identical clips together instead of writing both.</summary>
    public bool Dedupe;
    public float DedupeRotation = 0.5f;
    /// <summary>Clips per FBX file. 0 writes one file per clip.</summary>
    public int PackSize;
}

/// <summary>One animation archive to export from; bytes are fetched on demand.</summary>
public sealed record MtarSource(string Name, Func<byte[]> Open);

public sealed class RipInput
{
    public byte[] ModelBytes = Array.Empty<byte>();
    public string ModelName = "model";
    public List<MtarSource> Sources = new();
    public byte[] FrigBytes;
    public byte[] FrdvBytes;
}

public sealed record RipProgress(int Done, int Total, string Current,
                                 int Exported, int Skipped, int Static, int Failed);

public sealed class RipResult
{
    public int Exported, Skipped, Static, Failed, Duplicates, Files;
    public double Seconds;
    public string IndexPath = "";
    public List<string> ClipNames = new();
}

/// <summary>
/// The export engine: model + animation archives in, one FBX per clip out.
///
/// Every decode, the FRIG bone drives, the IK jobs and the FRDV help-bone solve
/// come from FoxBrowser's own assemblies, called exactly the way its "rip"
/// button calls them. Nothing about the format is reimplemented here.
/// </summary>
public static class RipJob
{
    /// <summary>
    /// Point FoxBrowser's name dictionaries at the real dict folder.
    ///
    /// Archive path names go through QarNameDictionary, which takes a directory.
    /// Bone and node names go through StrCodeNames, which hardcodes
    /// <c>AppContext.BaseDirectory/dict</c> -- the folder of whatever executable
    /// is running. Without this every bone comes out named <c>bone_&lt;hex&gt;</c>
    /// instead of <c>SKL_000_WAIST</c> and the clips will not bind to a model
    /// imported through the GUI.
    /// </summary>
    public static void UseDictionaries(string fbDir, Action<string> log)
    {
        var source = Path.Combine(fbDir, "dict");
        if (!Directory.Exists(source))
        {
            log($"! no dict folder at {source}; bone names will come out as hashes");
            return;
        }
        QarNameDictionary.SetDir(source);

        var appDict = Paths.DictStaging;
        var wanted = new[] { "bone_dictionary.txt", "fmdl_dictionary.txt" };
        if (!wanted.All(f => File.Exists(Path.Combine(appDict, f))))
        {
            try
            {
                Directory.CreateDirectory(appDict);
                foreach (var file in wanted)
                {
                    var from = Path.Combine(source, file);
                    var to = Path.Combine(appDict, file);
                    if (File.Exists(from) && !File.Exists(to)) File.Copy(from, to);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY",
                                       fbDir + Path.DirectorySeparatorChar);
                }
                catch { }
                log($"! could not stage the name dictionaries ({ex.Message})");
            }
        }

        var count = StrCodeNames.Count;
        if (count == 0)
            log("! name dictionary is empty; bones will be named by hash. Copy "
                + $"{string.Join(" and ", wanted)} from {source} next to foxanimrip.");
        else
            log($"names: {count:N0} entries loaded");
    }

    public static RipResult Run(RipInput input, RipOptions o, Action<string> log,
                                IProgress<RipProgress> progress = null,
                                CancellationToken token = default)
    {
        log ??= _ => { };
        var result = new RipResult();
        var watch = Stopwatch.StartNew();

        var model = FmdlFile.Parse(input.ModelBytes);
        var boneNameIndex = new int[model.Bones.Count];
        for (var i = 0; i < boneNameIndex.Length; i++)
            boneNameIndex[i] = model.Bones[i].NameIndex;

        log($"model {input.ModelName}: {model.Bones.Count} bones, {model.Meshes.Count} meshes");

        FrigFile frig = null;
        if (input.FrigBytes is { Length: > 0 })
        {
            frig = FrigFile.TryParse(input.FrigBytes);
            log(frig is null
                ? "! the rig file could not be parsed"
                : $"rig: {frig.RigUnitCount} units, {frig.SegmentCount} segments");
        }
        else
        {
            log("! no rig found: help bones and IK will not be solved");
        }

        IReadOnlyList<FrdvFile.Op> help = null;
        if (input.FrdvBytes is { Length: > 0 })
        {
            help = FrdvFile.TryParse(input.FrdvBytes)?.Operators;
            log(help is null ? "! the help-bone file could not be parsed"
                             : $"help bones: {help.Count} operators");
        }

        var dedupe = o.Dedupe ? new ClipDedupe(o.DedupeRotation) : null;

        StreamWriter index = null;
        if (!o.ListOnly)
        {
            Directory.CreateDirectory(o.OutDir);
            result.IndexPath = Path.Combine(o.OutDir, "index.tsv");
            index = new StreamWriter(result.IndexPath, false, new UTF8Encoding(false));
            index.WriteLine("mtar\tgani\tframes\tfps\tmatchedBones\tfile");
        }

        try
        {
            for (var si = 0; si < input.Sources.Count; si++)
            {
                token.ThrowIfCancellationRequested();
                var source = input.Sources[si];
                progress?.Report(new RipProgress(si, input.Sources.Count, source.Name,
                    result.Exported, result.Skipped, result.Static, result.Failed));

                MtarAnimSet set;
                try
                {
                    set = MtarAnimSet.Load(source.Open());
                }
                catch (Exception ex)
                {
                    log($"! {source.Name}: cannot load ({ex.Message})");
                    result.Failed++;
                    continue;
                }

                var stem = Path.GetFileNameWithoutExtension(source.Name);
                var dir = o.ListOnly ? "" : Path.Combine(o.OutDir, Safe(stem));
                var madeDir = false;
                var inThisMtar = 0;
                // When packing, clips queue up here and are flushed as one file
                // per PackSize, never spanning two animation sets.
                var pack = new List<(string Take, byte[] Fbx)>();
                var packIndex = 0;
                var pending = new List<string>();

                foreach (var entry in set.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (o.Limit > 0 && result.Exported >= o.Limit) break;

                    var clipName = StripExt(entry.Name);
                    if (o.Filter.Length > 0 &&
                        clipName.IndexOf(o.Filter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    GaniAnimation anim;
                    try
                    {
                        anim = entry.Animation.Value;
                    }
                    catch (Exception ex)
                    {
                        if (!o.Quiet) log($"! {stem}/{clipName}: decode failed ({ex.Message})");
                        result.Failed++;
                        continue;
                    }

                    var match = ResolveBest(anim, model, boneNameIndex, frig,
                                            out var drives, out var ikJobs);
                    if (match < o.MinMatch) { result.Skipped++; continue; }

                    if (o.ListOnly)
                    {
                        result.ClipNames.Add($"{stem}\t{clipName}\t{anim.FrameCount}\t{match}");
                        result.Exported++;
                        inThisMtar++;
                        continue;
                    }

                    try
                    {
                        if (!madeDir) { Directory.CreateDirectory(dir); madeDir = true; }

                        var scene = ExportScene.Build(model, input.ModelName);
                        if (!o.WithMesh)
                        {
                            // Skeleton only. The mesh is identical in every clip;
                            // 4000 copies of it would be ~14 GB for no gain.
                            scene.Meshes.Clear();
                            scene.Materials.Clear();
                        }
                        scene.Clip = ExportBake.FromGani(model, anim, "take", drives,
                                                         ikJobs, frig, help, o.Fps, o.Step);

                        // FoxBrowser's FBX writer only emits a curve for channels
                        // that change, so a clip where nothing moves -- every
                        // single-frame pose snapshot -- lands as a file with no
                        // animation in it at all.
                        if (!o.KeepStatic && !Moves(scene.Clip))
                        {
                            result.Static++;
                            continue;
                        }

                        if (dedupe is not null)
                        {
                            var same = dedupe.DuplicateOf(clipName, scene.Clip);
                            if (same is not null)
                            {
                                result.Duplicates++;
                                if (!o.Quiet) log($"  {clipName}: same motion as {same}");
                                continue;
                            }
                        }

                        var bytes = FbxExporter.Export(scene);
                        if (!o.NoFbxFix) bytes = FbxFix.Apply(bytes, out _);

                        if (o.PackSize > 0)
                        {
                            pack.Add((clipName, bytes));
                            pending.Add($"{stem}\t{clipName}\t{scene.Clip.FrameCount}\t"
                                        + $"{scene.Clip.Fps:0.###}\t{match}\t");
                            if (pack.Count >= o.PackSize)
                                FlushPack(dir, stem, ref packIndex, pack, pending, index,
                                          o.OutDir, result, log);
                        }
                        else
                        {
                            var file = Path.Combine(dir, Safe(clipName) + ".fbx");
                            File.WriteAllBytes(file, bytes);
                            index!.WriteLine($"{stem}\t{clipName}\t{scene.Clip.FrameCount}\t"
                                             + $"{scene.Clip.Fps:0.###}\t{match}\t"
                                             + Path.GetRelativePath(o.OutDir, file));
                            result.Files++;
                        }
                        result.Exported++;
                        result.ClipNames.Add(clipName);
                        inThisMtar++;
                    }
                    catch (Exception ex)
                    {
                        log($"! {stem}/{clipName}: export failed ({ex.Message})");
                        result.Failed++;
                    }
                }

                if (o.PackSize > 0 && pack.Count > 0)
                    FlushPack(dir, stem, ref packIndex, pack, pending, index,
                              o.OutDir, result, log);

                if (inThisMtar > 0) log($"  {stem}: {inThisMtar} clip(s)");
                if (o.Limit > 0 && result.Exported >= o.Limit) break;
            }
        }
        finally
        {
            index?.Flush();
            index?.Dispose();
        }

        result.Seconds = watch.Elapsed.TotalSeconds;
        progress?.Report(new RipProgress(input.Sources.Count, input.Sources.Count, "done",
            result.Exported, result.Skipped, result.Static, result.Failed));
        log($"done: {result.Exported} exported"
            + (o.PackSize > 0 ? $" in {result.Files} file(s)" : "")
            + $", {result.Skipped} skipped (below {o.MinMatch} matched bones), "
            + $"{result.Static} static, "
            + (dedupe is not null ? $"{result.Duplicates} duplicate, " : "")
            + $"{result.Failed} failed, {result.Seconds:0.#}s");
        return result;
    }

    /// <summary>Write the queued clips as one multi-take FBX.</summary>
    private static void FlushPack(string dir, string stem, ref int packIndex,
                                  List<(string Take, byte[] Fbx)> pack,
                                  List<string> pending, StreamWriter index,
                                  string outDir, RipResult result, Action<string> log)
    {
        if (pack.Count == 0) return;
        packIndex++;
        var name = $"{Safe(stem)}_{packIndex:D3}.fbx";
        var file = Path.Combine(dir, name);
        try
        {
            File.WriteAllBytes(file, FbxTakes.Pack(pack));
            result.Files++;
            var relative = Path.GetRelativePath(outDir, file);
            foreach (var line in pending) index?.WriteLine(line + relative);
        }
        catch (Exception ex)
        {
            // Packing is an optimisation; never lose the clips over it.
            log($"! could not pack {name} ({ex.Message}); writing the clips separately");
            foreach (var (take, bytes) in pack)
            {
                var single = Path.Combine(dir, Safe(take) + ".fbx");
                try
                {
                    File.WriteAllBytes(single, bytes);
                    result.Files++;
                }
                catch { }
            }
            for (var i = 0; i < pending.Count && i < pack.Count; i++)
                index?.WriteLine(pending[i] + Path.GetRelativePath(outDir,
                    Path.Combine(dir, Safe(pack[i].Take) + ".fbx")));
        }
        pack.Clear();
        pending.Clear();
    }

    /// <summary>Mirror of FoxBrowser's ResolveBest: prefer FRIG drives when they
    /// cover more of the skeleton than the gani's direct name matches.</summary>
    public static int ResolveBest(GaniAnimation anim, FmdlModel model, int[] boneNameIndex,
                                  FrigFile frig,
                                  out IReadOnlyDictionary<int, FrigFile.BoneDrive> drives,
                                  out IReadOnlyList<FrigFile.IkJob> ikJobs)
    {
        drives = null;
        ikJobs = null;
        anim.ResolveToBones(model.Names, boneNameIndex, out var matchCount);
        if (frig is null) return matchCount;

        var jobs = frig.ResolveIkJobs(model.Names, boneNameIndex, anim.Tracks.Count);
        if (jobs is { Count: > 0 }) ikJobs = jobs;

        var d = frig.ResolveBoneDrives(model.Names, boneNameIndex, anim.Tracks.Count,
                                       out var driveMatch);
        if (driveMatch > matchCount) { drives = d; return driveMatch; }
        return matchCount;
    }

    /// <summary>What a caller outside Core needs to know about a model, without
    /// it having to reference FoxBrowser's assemblies itself.</summary>
    public sealed record ModelInfo(int BoneCount, int MeshCount, HashSet<uint> BoneHashes);

    public static ModelInfo Inspect(byte[] modelBytes)
    {
        var model = FmdlFile.Parse(modelBytes);
        return new ModelInfo(model.Bones.Count, model.Meshes.Count, BoneHashes(model));
    }

    public static HashSet<uint> BoneHashes(FmdlModel model)
    {
        var set = new HashSet<uint>();
        foreach (var bone in model.Bones)
            if (bone.NameIndex >= 0 && bone.NameIndex < model.Names.Count)
                set.Add((uint)(model.Names[bone.NameIndex] & 0xFFFFFFFF));
        return set;
    }

    /// <summary>Does any bone change over the clip?  Mirrors FbxAnim.Moves.</summary>
    private static bool Moves(ExportClip clip)
    {
        foreach (var track in new[] { clip.Translation, clip.RotationEuler })
        {
            if (track is null) continue;
            foreach (var frames in track)
            {
                if (frames is null || frames.Length < 2) continue;
                for (var i = 1; i < frames.Length; i++)
                {
                    var d = frames[i] - frames[0];
                    if (MathF.Abs(d.X) > 1e-5f || MathF.Abs(d.Y) > 1e-5f
                        || MathF.Abs(d.Z) > 1e-5f)
                        return true;
                }
            }
        }
        return false;
    }

    public static string StripExt(string name)
    {
        var i = name.LastIndexOf('.');
        return i > 0 ? name[..i] : name;
    }

    public static string Safe(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name) sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.Length == 0 ? "clip" : sb.ToString();
    }
}

/// <summary>Finding a model's rig and help bones, and mtars, inside archives.</summary>
public static class Sources
{
    public static void Walk(string archivePath, Func<string, Func<byte[]>, bool> onFile)
    {
        FoxArchive archive;
        try { archive = FoxArchive.Open(archivePath); }
        catch { return; }
        try { WalkInner(archive, "", onFile); }
        finally { archive.Dispose(); }
    }

    private static bool WalkInner(FoxArchive archive, string dir,
                                  Func<string, Func<byte[]>, bool> onFile)
    {
        IReadOnlyList<FoxItem> items;
        try { items = archive.List(dir); }
        catch { return true; }

        foreach (var item in items)
        {
            var path = dir.Length == 0 ? item.Name : dir + "/" + item.Name;
            var isMtar = item.Name.EndsWith(".mtar", StringComparison.OrdinalIgnoreCase);

            if (!item.IsFolder)
            {
                var captured = path;
                if (!onFile(path, () => archive.Read(captured))) return false;
            }

            if (item.IsFolder)
            {
                if (!WalkInner(archive, path, onFile)) return false;
            }
            else if (item.IsArchive && !isMtar)
            {
                FoxArchive nested = null;
                try { nested = archive.OpenNested(path); } catch { }
                if (nested is null) continue;
                try { if (!WalkInner(nested, "", onFile)) return false; }
                finally { nested.Dispose(); }
            }
        }
        return true;
    }

    /// <summary>Best-matching .frig for a skeleton, judged the way the GUI does.</summary>
    public static byte[] FindFrig(IEnumerable<string> archives, HashSet<uint> boneHashes,
                                  Action<string> log = null)
    {
        byte[] best = null;
        var bestScore = 0;
        foreach (var archive in archives)
        {
            Walk(archive, (name, read) =>
            {
                if (!name.EndsWith(".frig", StringComparison.OrdinalIgnoreCase)) return true;
                byte[] bytes;
                try { bytes = read(); } catch { return true; }
                FrigFile parsed;
                try { parsed = FrigFile.TryParse(bytes); } catch { return true; }
                if (parsed is null) return true;
                var score = Math.Min(parsed.SegmentCount, boneHashes.Count);
                if (score > bestScore) { bestScore = score; best = bytes; }
                return true;
            });
            if (bestScore >= boneHashes.Count) break;
        }
        log?.Invoke(best is null ? "rig: none found"
                                 : $"rig: matched {bestScore} bones of this skeleton");
        return best;
    }

    public static byte[] FindFrdv(IEnumerable<string> archives, string modelName,
                                  Action<string> log = null)
    {
        var want = modelName + ".frdv";
        byte[] found = null;
        foreach (var archive in archives)
        {
            Walk(archive, (name, read) =>
            {
                if (!name.EndsWith(want, StringComparison.OrdinalIgnoreCase)) return true;
                try { found = read(); } catch { }
                return found is null;
            });
            if (found is not null) break;
        }
        log?.Invoke(found is null ? $"help bones: no {want} found"
                                  : $"help bones: {want}");
        return found;
    }
}

/// <summary>How well an animation archive fits a skeleton.</summary>
public sealed record MtarMatch(int MatchedBones, int Clips, bool Sampled)
{
    public bool Fits(int minMatch) => MatchedBones >= minMatch;
}

/// <summary>
/// A loaded model plus its rig, kept together so compatibility can be judged
/// properly. Callers outside Core never touch FoxBrowser's types through this.
/// </summary>
public sealed class ModelContext
{
    internal FmdlModel Model;
    internal int[] BoneNameIndex;
    internal FrigFile Frig;
    internal IReadOnlyList<FrdvFile.Op> HelpBoneOps;

    public string Name { get; private set; } = "model";
    public byte[] ModelBytes { get; private set; }
    public byte[] FrigBytes { get; private set; }
    public byte[] FrdvBytes { get; private set; }
    public int BoneCount { get; private set; }
    public int MeshCount { get; private set; }
    public HashSet<uint> BoneHashes { get; private set; } = new();
    public int RigUnits { get; private set; }
    public int RigSegments { get; private set; }

    public static ModelContext Create(string name, byte[] modelBytes, byte[] frigBytes = null,
                                      byte[] frdvBytes = null)
    {
        var model = FmdlFile.Parse(modelBytes);
        var context = new ModelContext
        {
            Name = name,
            ModelBytes = modelBytes,
            FrigBytes = frigBytes,
            FrdvBytes = frdvBytes,
            Model = model,
            BoneCount = model.Bones.Count,
            MeshCount = model.Meshes.Count,
            BoneHashes = RipJob.BoneHashes(model),
        };
        context.BoneNameIndex = new int[model.Bones.Count];
        for (var i = 0; i < context.BoneNameIndex.Length; i++)
            context.BoneNameIndex[i] = model.Bones[i].NameIndex;

        if (frigBytes is { Length: > 0 })
        {
            context.Frig = FrigFile.TryParse(frigBytes);
            if (context.Frig is not null)
            {
                context.RigUnits = context.Frig.RigUnitCount;
                context.RigSegments = context.Frig.SegmentCount;
            }
        }
        return context;
    }

    /// <summary>
    /// Does this animation archive drive this skeleton, and by how much?
    ///
    /// The cheap path is the mtar's own bone-hash table, but that table is
    /// optional -- the MTAR header's HAS_SKEL_LIST flag is clear on plenty of
    /// archives, SoldierGz_layers among them, and probing those returns
    /// nothing. So when the cheap path comes up short we actually decode a
    /// handful of clips and resolve them against the skeleton, which is what
    /// FoxBrowser's own compatibility scan does.
    /// </summary>
    public MtarMatch Check(byte[] mtarBytes, int minMatch, int sample = 6)
    {
        var clips = 0;
        try
        {
            if (MtarAnimSet.TryProbe(mtarBytes, out var probe))
            {
                clips = probe.Ganis?.Length ?? 0;
                var overlap = 0;
                foreach (var hash in probe.BoneHashes)
                    if (BoneHashes.Contains(hash)) overlap++;
                if (overlap >= minMatch) return new MtarMatch(overlap, clips, false);
            }
        }
        catch { }

        try
        {
            var set = MtarAnimSet.Load(mtarBytes);
            clips = set.Entries.Count;
            var best = 0;
            var seen = 0;
            foreach (var entry in set.Entries)
            {
                if (seen++ >= sample) break;
                GaniAnimation anim;
                try { anim = entry.Animation.Value; }
                catch { continue; }
                var match = RipJob.ResolveBest(anim, Model, BoneNameIndex, Frig, out _, out _);
                if (match > best) best = match;
                if (best >= minMatch) break;
            }
            return new MtarMatch(best, clips, true);
        }
        catch
        {
            return new MtarMatch(0, clips, true);
        }
    }

    /// <summary>Give this model its rig once it has been located.</summary>
    public void Attach(byte[] frigBytes, byte[] frdvBytes)
    {
        FrdvBytes = frdvBytes;
        if (frdvBytes is { Length: > 0 })
        {
            try { HelpBoneOps = FrdvFile.TryParse(frdvBytes)?.Operators; }
            catch { HelpBoneOps = null; }
        }
        if (frigBytes is not { Length: > 0 }) return;
        FrigBytes = frigBytes;
        Frig = FrigFile.TryParse(frigBytes);
        if (Frig is null) return;
        RigUnits = Frig.RigUnitCount;
        RigSegments = Frig.SegmentCount;
    }

    public RipInput ToInput() => new()
    {
        ModelBytes = ModelBytes,
        ModelName = Name,
        FrigBytes = FrigBytes,
        FrdvBytes = FrdvBytes,
    };
}
