// SPDX-License-Identifier: MIT
//
// The cached index has to notice when it is an older shape than the code.
//
//     dotnet run --project tests/index
//
// Needs no game files: it writes cache JSON by hand and asks the loader what it
// makes of it.
//
// Why this test exists. When .fv2 files were added to the index, existing caches
// had no variations bucket and no way to know they were missing one -- the
// fingerprint only watches the game's archives, and the game had not changed.
// A schema number was added for exactly that, and it did not work: the property
// was declared
//
//     public int Schema { get; set; } = CurrentSchema;
//
// and System.Text.Json leaves a property that is absent from the JSON at
// whatever the declaration initialised it to. So every old cache -- which has no
// Schema property at all -- deserialised claiming to be current, passed the
// guard, and was reused. The symptom was an inventory run reporting
// "0 form-variation file(s) to read" on a game full of them.
//
// The rule this pins: an absent Schema must read as 0, never as current.

using System.Text.Json;
using FoxAnimRip;

var failures = new List<string>();
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
    if (!ok) failures.Add(what);
}

// -- 1. the deserialiser's own behaviour, which is the whole trap ------------

var legacy = JsonSerializer.Deserialize<GameCatalog>(
    """{"Root":"C:\\game","Fingerprint":"AAAA","Models":[],"Mtars":[]}""");

Console.WriteLine($"schema of a cache written before the field existed: {legacy.Schema}");
Check(legacy.Schema == 0,
      "a cache with no schema property reads as 0, not as the current schema");
Check(GameCatalog.CurrentSchema != 0,
      "...and the current schema is not 0, so the two are distinguishable");

// A cache that does carry a number keeps it, rather than being overwritten.
var stamped = JsonSerializer.Deserialize<GameCatalog>(
    """{"Schema":1,"Root":"C:\\game","Fingerprint":"AAAA"}""");
Check(stamped.Schema == 1, "a schema number in the file survives the round trip");

// -- 2. the loader acting on it ---------------------------------------------

// A root that no game will ever be under, so the cache key is ours alone.
var root = Path.Combine(Path.GetTempPath(), "foxanimrip-schema-test-" + Environment.ProcessId);
const string fingerprint = "SCHEMATEST000000";
var cachePath = GameCatalog.CachePath(root, fingerprint);
Directory.CreateDirectory(Path.GetDirectoryName(cachePath));

try
{
    // An old index, correct fingerprint and all: it must still be refused.
    File.WriteAllText(cachePath,
        $"{{\"Root\":{JsonSerializer.Serialize(root)}," +
        $"\"Fingerprint\":\"{fingerprint}\",\"Models\":[],\"Mtars\":[]}}");

    var loaded = GameCatalog.LoadCached(root, fingerprint);
    Check(loaded is null, "an index older than the code is refused, not reused");
    Check(GameCatalog.Stale, "...and says so, so the rescan can be explained");

    // The same file with the current number is accepted, or every run rescans.
    File.WriteAllText(cachePath,
        $"{{\"Schema\":{GameCatalog.CurrentSchema}," +
        $"\"Root\":{JsonSerializer.Serialize(root)}," +
        $"\"Fingerprint\":\"{fingerprint}\",\"Models\":[],\"Mtars\":[]}}");

    var current = GameCatalog.LoadCached(root, fingerprint);
    Check(current is not null, "an index of the current shape is reused");

    // And a fingerprint mismatch still wins, schema or no schema.
    Check(GameCatalog.LoadCached(root, "DIFFERENT0000000") is null,
          "a changed game still invalidates the index");

    // A cache written by this version must pass its own guard -- otherwise the
    // number is stamped in one place and read in another.
    var fresh = new GameCatalog { Root = root, Fingerprint = fingerprint };
    Check(JsonSerializer.Deserialize<GameCatalog>(JsonSerializer.Serialize(fresh))
              .Schema == fresh.Schema,
          "a hand-built catalogue serialises and reloads with its schema intact");
}
finally
{
    try { File.Delete(cachePath); } catch { }
}

Console.WriteLine();
Console.WriteLine(failures.Count == 0 ? "PASSED (0 failures)"
                                      : $"FAILED ({failures.Count})");
return failures.Count == 0 ? 0 : 1;
