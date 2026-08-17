// SPDX-License-Identifier: MIT
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FoxAnimRip;

/// <summary>
/// Remember which rig belongs to which character.
///
/// Finding one means walking every archive in the game and parsing every
/// <c>.frig</c> inside — on The Phantom Pain that is thirteen archives and tens
/// of gigabytes, and it happened again on every single run, before anything the
/// user actually asked for. A command like <c>--why-mtar</c>, which prints six
/// lines, appeared to hang for minutes with nothing on screen.
///
/// The result is small — a rig is a few kilobytes — so it is kept beside the
/// archive index and keyed the same way, by the model and a fingerprint of the
/// game files. Change the game and the key changes with it.
/// </summary>
public static class RigCache
{
    private sealed class Entry
    {
        public string Model { get; set; } = "";
        public string Fingerprint { get; set; } = "";
        public string FrigPath { get; set; } = "";
        public int Matched { get; set; }
        public int RigBones { get; set; }
        public int ModelBones { get; set; }
        public double Affinity { get; set; }
        public string Frig { get; set; } = "";     // base64, empty when none fits
        public string Frdv { get; set; } = "";
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    private static string PathFor(string model, string fingerprint)
    {
        var key = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(model.ToLowerInvariant())))[..12];
        return Path.Combine(Paths.RigCache, $"{key}-{fingerprint}.json");
    }

    /// <summary>The cached choice, or null. A cached "no rig fits" is a result too.</summary>
    public static (Sources.FrigChoice Choice, byte[] Frdv)? Load(string model, string fingerprint)
    {
        try
        {
            var path = PathFor(model, fingerprint);
            if (!File.Exists(path)) return null;
            var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(path), Options);
            if (entry is null || entry.Fingerprint != fingerprint) return null;

            var frdv = entry.Frdv.Length > 0 ? Convert.FromBase64String(entry.Frdv) : null;
            if (entry.Frig.Length == 0) return (null, frdv);

            return (new Sources.FrigChoice(Convert.FromBase64String(entry.Frig),
                                           entry.FrigPath, entry.Matched, entry.RigBones,
                                           entry.ModelBones, entry.Affinity), frdv);
        }
        catch { return null; }
    }

    public static void Save(string model, string fingerprint,
                            Sources.FrigChoice choice, byte[] frdv)
    {
        try
        {
            Directory.CreateDirectory(Paths.RigCache);
            var entry = new Entry
            {
                Model = model,
                Fingerprint = fingerprint,
                FrigPath = choice?.Path ?? "",
                Matched = choice?.Matched ?? 0,
                RigBones = choice?.RigBones ?? 0,
                ModelBones = choice?.ModelBones ?? 0,
                Affinity = choice?.Affinity ?? 0,
                Frig = choice is null ? "" : Convert.ToBase64String(choice.Bytes),
                Frdv = frdv is { Length: > 0 } ? Convert.ToBase64String(frdv) : "",
            };
            File.WriteAllText(PathFor(model, fingerprint),
                              JsonSerializer.Serialize(entry, Options));
        }
        catch { /* a missing cache only costs time */ }
    }

    /// <summary>
    /// The cached rig for a model, or find it and remember.
    /// </summary>
    public static (Sources.FrigChoice Choice, byte[] Frdv) Resolve(
        IReadOnlyList<string> archives, string fingerprint, string model,
        HashSet<uint> boneHashes, string modelPath = null,
        Action<string> log = null, CancellationToken token = default)
    {
        var cached = Load(model, fingerprint);
        if (cached is not null)
        {
            log?.Invoke(cached.Value.Choice is null
                ? "rig: none (remembered from an earlier run)"
                : $"rig: {cached.Value.Choice.Describe()} (remembered)");
            return (cached.Value.Choice, cached.Value.Frdv);
        }

        log?.Invoke($"rig: searching {archives.Count} archive(s) for {model}'s rig "
                    + "— this happens once per character, then it is remembered");
        var choice = Sources.ChooseFrig(archives, boneHashes, model, modelPath,
                                        log: log, token: token);
        var frdv = Sources.FindFrdv(archives, model, log);
        Save(model, fingerprint, choice, frdv);
        return (choice, frdv);
    }
}
