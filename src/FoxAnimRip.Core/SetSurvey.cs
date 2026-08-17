// SPDX-License-Identifier: MIT
using FoxBrowser.Models.Anim;
using FoxBrowser.Models.Fmdl;

namespace FoxAnimRip;

/// <summary>What an animation archive is, and which skeleton it drives.</summary>
public sealed record AnimSetInfo(string Name, string Path, string ArchiveName, long Size,
                                 int Clips, bool HasSkeletonList, HashSet<uint> BoneHashes,
                                 IReadOnlyList<string> ClipNames,
                                 IReadOnlyList<string> ClipPaths,
                                 int Layer = 0)
{
    public string Stem => System.IO.Path.GetFileNameWithoutExtension(Name);
}

/// <summary>How well one model fits one animation archive.</summary>
public sealed record ModelFit(string Name, string Path, string ArchiveName,
                              int Bones, int Matched, int SetBones)
{
    /// <summary>Fraction of the animation's bones the model has.</summary>
    public double Coverage => SetBones > 0 ? (double)Matched / SetBones : 0;

    /// <summary>Fraction of the model's bones the animation drives.</summary>
    public double Driven => Bones > 0 ? (double)Matched / Bones : 0;
}

/// <summary>
/// The search run backwards: from an animation archive to the models that can
/// play it.
///
/// Everything else in this tool starts at a character and asks what animates it,
/// because that is what you want when you already know whose animations you are
/// after. It is the wrong way round for the other question -- "I want *these*
/// animations, so which character do I load?" -- which is the one you actually
/// have when you are hunting for a base model to attach a whole locomotion set
/// to. Fox Engine binds animation to *skeletons*, not to models, so that
/// question has a real answer: whichever model's skeleton the archive names.
///
/// The bone-hash table an .mtar carries makes this cheap, and where the header's
/// HAS_SKEL_LIST flag is clear -- which is often -- a handful of decoded clips
/// give the same set of hashes from their track names.
/// </summary>
public static class SetSurvey
{
    /// <summary>Read an archive far enough to know its clips and its skeleton.</summary>
    public static AnimSetInfo Describe(byte[] bytes, string name, string path,
                                       string archiveName, int sample = 8,
                                       bool withClipNames = false)
    {
        var hashes = new HashSet<uint>();
        var clipNames = new List<string>();
        // The path the game's own hash dictionary resolves for a clip. Not a
        // guess about what a name means -- it is the name, spelled out.
        var clipPaths = new List<string>();
        var clips = 0;
        var hasList = false;

        try
        {
            if (MtarAnimSet.TryProbe(bytes, out var probe))
            {
                hasList = true;
                foreach (var hash in probe.BoneHashes) hashes.Add(hash);
                clips = probe.Ganis?.Length ?? 0;
                if (withClipNames && probe.Ganis is not null)
                    foreach (var gani in probe.Ganis)
                    {
                        clipNames.Add(RipJob.StripExt(gani.Item1));
                        clipPaths.Add("");
                    }
            }
        }
        catch { /* fall through to the slow path */ }

        if (!hasList || clips == 0 || (withClipNames && clipNames.Count == 0))
        {
            try
            {
                var set = MtarAnimSet.Load(bytes);
                clips = set.Entries.Count;
                if (withClipNames)
                {
                    clipNames.Clear();
                    clipPaths.Clear();
                    foreach (var entry in set.Entries)
                    {
                        clipNames.Add(RipJob.StripExt(entry.Name));
                        clipPaths.Add(entry.FullPath ?? "");
                    }
                }

                // Without a skeleton list, the clips themselves name the bones:
                // every track carries the hash of the bone it drives.
                if (hashes.Count == 0)
                {
                    var seen = 0;
                    foreach (var entry in set.Entries)
                    {
                        if (seen++ >= sample) break;
                        try
                        {
                            foreach (var track in entry.Animation.Value.Tracks)
                                hashes.Add(track.NameHash32);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        return new AnimSetInfo(name, path, archiveName, 0, clips, hasList, hashes,
                               clipNames, clipPaths);
    }

    /// <summary>Every animation archive in a game, optionally narrowed by name or path.</summary>
    public static List<AnimSetInfo> Sets(GameCatalog catalog, string filter, bool deep,
                                         Action<string> progress = null,
                                         CancellationToken token = default)
    {
        var results = new List<AnimSetInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in catalog.Mtars)
        {
            token.ThrowIfCancellationRequested();
            if (filter is { Length: > 0 }
                && entry.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                && entry.Path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (!seen.Add(entry.Path + "|" + entry.ArchiveName)) continue;

            if (!deep)
            {
                results.Add(new AnimSetInfo(entry.Name, entry.Path, entry.ArchiveName,
                                            entry.Size, 0, false, new HashSet<uint>(),
                                            Array.Empty<string>(), Array.Empty<string>(),
                                            entry.Layer));
                continue;
            }

            progress?.Invoke(entry.Name);
            try
            {
                var described = Describe(GameCatalog.Read(entry), entry.Name, entry.Path,
                                         entry.ArchiveName);
                results.Add(described with { Size = entry.Size, Layer = entry.Layer });
            }
            catch
            {
                results.Add(new AnimSetInfo(entry.Name, entry.Path, entry.ArchiveName,
                                            entry.Size, 0, false, new HashSet<uint>(),
                                            Array.Empty<string>(), Array.Empty<string>(),
                                            entry.Layer));
            }
        }
        return results;
    }

    /// <summary>
    /// The models that can play an animation archive, best first.
    ///
    /// Ranked by how much of the *animation's* skeleton the model has, because
    /// that is the question being asked: a model missing a third of the bones an
    /// archive drives will play a third of the clip wrong, however many bones of
    /// its own go unused.
    /// </summary>
    public static List<ModelFit> ModelsFor(GameCatalog catalog, AnimSetInfo set,
                                           string modelFilter = null,
                                           bool charactersOnly = true,
                                           IProgress<(int Done, int Total, string Name)> progress = null,
                                           CancellationToken token = default,
                                           byte[] setBytes = null,
                                           IReadOnlyList<string> archives = null,
                                           Action<string> log = null,
                                           int accurateLimit = 60)
    {
        var candidates = new List<CatalogEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pool = charactersOnly ? catalog.CharacterModels : catalog.Models;

        foreach (var entry in pool)
        {
            if (modelFilter is { Length: > 0 }
                && entry.Name.IndexOf(modelFilter, StringComparison.OrdinalIgnoreCase) < 0
                && entry.Path.IndexOf(modelFilter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            // The same model is copied into several archives; judging it once is
            // enough, and the duplicates would otherwise fill the whole ranking.
            if (!seen.Add(entry.Stem)) continue;
            candidates.Add(entry);
        }

        var fits = new List<ModelFit>();
        for (var i = 0; i < candidates.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var entry = candidates[i];
            progress?.Report((i, candidates.Count, entry.Stem));
            try
            {
                var model = FmdlFile.Parse(GameCatalog.Read(entry));
                if (model.Bones.Count == 0) continue;
                var bones = RipJob.BoneHashes(model);
                var matched = 0;
                foreach (var hash in set.BoneHashes)
                    if (bones.Contains(hash)) matched++;
                if (matched == 0) continue;
                fits.Add(new ModelFit(entry.Stem, entry.Path, entry.ArchiveName,
                                      bones.Count, matched, set.BoneHashes.Count));
            }
            catch { /* a model that will not parse cannot be the answer */ }
        }

        // A rig-driven archive names rig units, not bones, so intersecting its
        // table with a skeleton's bone hashes gives nothing at all -- which is a
        // silent wrong answer, not an empty one. When that happens, ask the same
        // question the player asks: resolve each candidate through its own rig.
        if (fits.Count == 0 && setBytes is not null && archives is { Count: > 0 })
        {
            log?.Invoke("this archive addresses rig units rather than bones, so it "
                        + "cannot be matched by name. Resolving candidates through "
                        + "their rigs instead, which is slower.");
            fits = Accurate(candidates, set, setBytes, archives, progress, log,
                            accurateLimit, token);
        }

        fits.Sort((x, y) =>
        {
            var byCoverage = y.Coverage.CompareTo(x.Coverage);
            if (byCoverage != 0) return byCoverage;
            // Same coverage: prefer the leaner skeleton, which is the one built
            // for this animation rather than a superset that happens to contain it.
            return x.Bones.CompareTo(y.Bones);
        });
        return fits;
    }

    /// <summary>
    /// The slow, correct path: build each model with its rig and ask it.
    ///
    /// Every candidate needs its rig found, which is why this is bounded and why
    /// the bound is announced -- a truncated ranking that looks complete is worse
    /// than a short one that says it is short.
    /// </summary>
    private static List<ModelFit> Accurate(
        List<CatalogEntry> candidates, AnimSetInfo set, byte[] setBytes,
        IReadOnlyList<string> archives,
        IProgress<(int Done, int Total, string Name)> progress, Action<string> log,
        int limit, CancellationToken token)
    {
        var fits = new List<ModelFit>();
        var fingerprint = GameCatalog.FingerprintOf(archives);
        var considered = candidates;
        if (limit > 0 && candidates.Count > limit)
        {
            log?.Invoke($"{candidates.Count} candidates is too many to resolve rigs for; "
                        + $"checking the first {limit}. Narrow it with --model-filter, "
                        + "or raise the cap.");
            considered = candidates.Take(limit).ToList();
        }

        for (var i = 0; i < considered.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var entry = considered[i];
            progress?.Report((i, considered.Count, entry.Stem));
            try
            {
                var context = ModelContext.Create(entry.Stem, GameCatalog.Read(entry));
                var (choice, frdv) = RigCache.Resolve(archives, fingerprint, entry.Stem,
                                                      context.BoneHashes, entry.Path,
                                                      null, token);
                context.Attach(choice, frdv);
                var match = context.Check(setBytes, 1);
                if (match.MatchedBones <= 0) continue;
                fits.Add(new ModelFit(entry.Stem, entry.Path, entry.ArchiveName,
                                      context.BoneCount, match.MatchedBones,
                                      context.BoneCount));
            }
            catch { }
        }
        return fits;
    }
}
