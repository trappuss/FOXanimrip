// SPDX-License-Identifier: MIT
// Catalog HTML generation from a real models.tsv (no game files needed).
using System.Text.RegularExpressions;
using FoxAnimRip;

var failures = new List<string>();
void Check(bool ok, string what){ Console.WriteLine((ok?"  ok   ":"  FAIL ")+what); if(!ok) failures.Add(what);}

var src = "/mnt/user-data/uploads/FOXanimtool/test-logs/survive-inventory/models.tsv";
var tmp = Path.Combine(Path.GetTempPath(), "catalog-test-" + Environment.ProcessId);
Directory.CreateDirectory(tmp);
File.Copy(src, Path.Combine(tmp, "models.tsv"), true);

var cat = new GameCatalog { ProfileId = "survive", Root = "X" };  // empty Mtars -> no anims tab data
CatalogHtml.Write(tmp, cat, "Metal Gear Survive", m => Console.WriteLine("  " + m));

var html = File.ReadAllText(Path.Combine(tmp, "catalog.html"));
Check(File.Exists(Path.Combine(tmp,"catalog.html")), "catalog.html was written");
Check(!html.Contains("__MODELS__") && !html.Contains("__ANIMS__") && !html.Contains("__GAME__"),
      "all placeholders were filled");
Check(html.Contains("Metal Gear Survive"), "game name is present");
Check(html.Contains("How to use") && html.Contains("Add Part(s) to Active Body"),
      "instructions tab with the assemble workflow is embedded");
// data present + translation applied
Check(html.Contains("bsm0_main0_def"), "a known model appears");
Check(html.Contains("Player base") || html.Contains("Arm / glove"), "descriptions were translated");
// well-formed: MODELS json parses
var m = Regex.Match(html, @"const MODELS=(\[.*?\]), ANIMS=", RegexOptions.Singleline);
Check(m.Success, "MODELS array is embedded");
if (m.Success)
{
    var doc = System.Text.Json.JsonDocument.Parse(m.Groups[1].Value);
    var n = doc.RootElement.GetArrayLength();
    Console.WriteLine($"  ({n} models embedded)");
    Check(n > 300, "hundreds of models embedded");
}
try { Directory.Delete(tmp, true); } catch {}
Console.WriteLine();
Console.WriteLine(failures.Count==0 ? "PASSED (0 failures)" : $"FAILED ({failures.Count})");
return failures.Count==0 ? 0 : 1;
