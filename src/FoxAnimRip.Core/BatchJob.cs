// SPDX-License-Identifier: MIT
using System.Diagnostics;

namespace FoxAnimRip;

public sealed record BatchProgress(int CharacterIndex, int CharacterCount, string Character,
                                   int SetIndex, int SetCount, string Set, int Exported);

public sealed class BatchResult
{
    public readonly List<(string Character, RipResult Result)> PerCharacter = new();
    public double Seconds;

    public int Exported => PerCharacter.Sum(p => p.Result.Exported);
    public int Skipped => PerCharacter.Sum(p => p.Result.Skipped);
    public int Static => PerCharacter.Sum(p => p.Result.Static);
    public int Failed => PerCharacter.Sum(p => p.Result.Failed);
}

/// <summary>
/// Runs an export across several characters.
///
/// With one character the output layout is unchanged --
/// <c>&lt;out&gt;/&lt;set&gt;/&lt;clip&gt;.fbx</c>. With more than one, each gets
/// its own folder: <c>&lt;out&gt;/&lt;character&gt;/&lt;set&gt;/&lt;clip&gt;.fbx</c>,
/// which is also what the Blender add-on looks for when deciding which clips
/// belong to the armature you have selected.
/// </summary>
public static class BatchJob
{
    public static BatchResult Run(IReadOnlyList<ModelContext> characters,
                                  Func<ModelContext, CancellationToken, List<MtarSource>> sourcesFor,
                                  RipOptions options, Action<string> log,
                                  IProgress<BatchProgress> progress = null,
                                  CancellationToken token = default)
    {
        log ??= _ => { };
        var result = new BatchResult();
        var watch = Stopwatch.StartNew();
        var perCharacterFolders = characters.Count > 1;

        for (var i = 0; i < characters.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var context = characters[i];
            progress?.Report(new BatchProgress(i, characters.Count, context.Name,
                0, 0, "", result.Exported));

            if (characters.Count > 1)
                log($"--- {context.Name}  ({i + 1} of {characters.Count}) ---");

            List<MtarSource> sources;
            try
            {
                sources = sourcesFor(context, token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                log($"! {context.Name}: could not work out which animations fit ({ex.Message})");
                continue;
            }

            if (sources.Count == 0)
            {
                log($"! {context.Name}: no animation set in this game drives it");
                result.PerCharacter.Add((context.Name, new RipResult()));
                continue;
            }

            var input = context.ToInput();
            input.Sources.AddRange(sources);

            var perCharacter = new RipOptions
            {
                OutDir = perCharacterFolders
                    ? Path.Combine(options.OutDir, RipJob.Safe(context.Name))
                    : options.OutDir,
                Filter = options.Filter,
                MinMatch = options.MinMatch,
                Limit = options.Limit,
                Step = options.Step,
                Fps = options.Fps,
                WithMesh = options.WithMesh,
                ListOnly = options.ListOnly,
                NoFbxFix = options.NoFbxFix,
                KeepStatic = options.KeepStatic,
                Quiet = options.Quiet,
                Dedupe = options.Dedupe,
                DedupeRotation = options.DedupeRotation,
                PackSize = options.PackSize,
            };

            var index = i;
            var inner = new Progress<RipProgress>(p => progress?.Report(
                new BatchProgress(index, characters.Count, context.Name,
                    p.Done, p.Total, p.Current, result.Exported + p.Exported)));

            var one = RipJob.Run(input, perCharacter, log, inner, token);
            result.PerCharacter.Add((context.Name, one));
        }

        result.Seconds = watch.Elapsed.TotalSeconds;
        if (characters.Count > 1)
            log($"all done: {result.Exported} clip(s) across {characters.Count} character(s) "
                + $"in {result.Seconds:0.#}s");
        return result;
    }
}
