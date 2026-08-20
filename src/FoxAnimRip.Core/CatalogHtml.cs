// SPDX-License-Identifier: MIT
using System.Text.Json;
using FoxBrowser.Models.Anim;   // MtarAnimSet

namespace FoxAnimRip;

/// <summary>
/// Turns an inventory into a single navigable HTML page: every model and
/// animation archive, grouped and searchable, with a plain-language description
/// and a built-in "how to use" tab. Written beside the TSVs by every
/// <c>--inventory</c> run so the catalogue is a normal output of the tool, not a
/// thing made once by hand.
/// </summary>
public static class CatalogHtml
{
    private sealed record ModelRow(string Name, string Game, string Category,
        string Desc, int Bones, int Meshes, string Path);

    private sealed record AnimRow(string Name, string Game, string Category,
        string Desc, int Clips, int Tracks, string Path);

    /// <summary>Character codes we can name with confidence.</summary>
    private static readonly Dictionary<string, string> Chars = new()
    {
        ["sna"] = "Snake (Venom / Punished Snake)", ["sna2"] = "Snake — Ground Zeroes",
        ["skl"] = "Player base skeleton", ["qui"] = "Quiet", ["kaz"] = "Kazuhira Miller",
        ["hue"] = "Huey Emmerich", ["hyu"] = "Huey Emmerich", ["paz"] = "Paz Ortega",
        ["dds"] = "Diamond Dogs soldier", ["ddg"] = "Diamond Dogs soldier",
        ["olm"] = "Ocelot", ["ocl"] = "Ocelot", ["dlf"] = "DLC female character",
        ["dlg"] = "DLC gear character", ["dlh"] = "DLC character",
        ["avm"] = "MGO avatar", ["avf"] = "MGO avatar — female", ["avr"] = "MGO avatar stage",
        ["wss"] = "Soviet soldier (GZ)", ["wsp"] = "Soviet patrol (GZ)",
        ["uss"] = "US soldier (GZ)", ["rai"] = "Raiden", ["chi"] = "Chico",
        ["rvn"] = "Raven", ["psy"] = "Psycho Mantis", ["dar"] = "Skull Face",
        ["prs"] = "Prisoner", ["nrs"] = "Medical staff", ["hrs"] = "Horse",
        ["bss"] = "Boss enemy (Survive)", ["kij"] = "Kaiju / creature (Survive)",
        ["zmb"] = "Wanderer (Survive)", ["mbs"] = "Mother Base staff (Survive)",
        ["bsm"] = "Player base — male (Survive)", ["bsf"] = "Player base — female (Survive)",
        ["emm"] = "Base male (Survive)", ["emf"] = "Base female (Survive)",
        ["dmc"] = "Story character (Survive)", ["gnt"] = "Giant enemy (Survive)",
        ["eng"] = "Engineer NPC (Survive)",
    };

    /// <summary>Folder → (category label, description).</summary>
    private static readonly Dictionary<string, (string Cat, string Desc)> Folders = new()
    {
        ["hats"] = ("Headgear", "Hats, helmets and headwear"),
        ["glasses"] = ("Eyewear", "Glasses and goggles"),
        ["inf_chest"] = ("Chest — Infiltrator", "Infiltrator-class chest gear"),
        ["rec_chest"] = ("Chest — Enforcer", "Enforcer-class chest gear"),
        ["tec_chest"] = ("Chest — Scout", "Scout-class chest gear"),
        ["inf_head"] = ("Head — Infiltrator", "Infiltrator-class head"),
        ["rec_head"] = ("Head — Enforcer", "Enforcer-class head"),
        ["tec_head"] = ("Head — Scout", "Scout-class head"),
        ["inf_suit"] = ("Suit — Infiltrator", "Infiltrator bodysuit"),
        ["rec_suit"] = ("Suit — Enforcer", "Enforcer bodysuit"),
        ["tec_suit"] = ("Suit — Scout", "Scout bodysuit"),
        ["cmn_suit"] = ("Suit — Common", "Shared bodysuit"),
        ["inf_cloth"] = ("Outfit — Infiltrator", "Infiltrator outfit"),
        ["rec_cloth"] = ("Outfit — Enforcer", "Enforcer outfit"),
        ["tec_cloth"] = ("Outfit — Scout", "Scout outfit"),
        ["avm"] = ("Avatar", "Created-character body / head / hair"),
        ["base"] = ("Base body", "Base skeleton / body"),
        ["arm"] = ("Arms", "Arm / glove part"), ["head"] = ("Head", "Head / face part"),
        ["leg"] = ("Legs", "Leg / boot part"), ["body"] = ("Body", "Torso / base body part"),
        ["up_armor"] = ("Upper armor", "Chest armor"),
        ["chest_rig"] = ("Chest rig", "Webbing / chest rig"),
        ["boss"] = ("Boss", "Boss enemy"), ["kaiju"] = ("Kaiju", "Large creature"),
        ["zmb"] = ("Wanderer", "Zombie enemy"), ["insect"] = ("Insect", "Insect creature"),
        ["tank"] = ("Vehicle", "Tank / vehicle"), ["npc"] = ("NPC", "Non-player character"),
        ["mbs"] = ("Mother Base", "Mother Base staff"),
    };

    public static void Write(string outDir, GameCatalog catalog, string gameName,
                             Action<string> log = null)
    {
        log ??= _ => { };
        var models = ReadModels(outDir, gameName);
        var anims = catalog.Mtars.Count > 0 ? ReadAnims(catalog, gameName)
                                            : new List<AnimRow>();
        if (models.Count == 0 && anims.Count == 0) return;

        var opts = new JsonSerializerOptions { WriteIndented = false };
        var html = Template
            .Replace("__GAME__", HtmlEscape(gameName))
            .Replace("__MODELS__", JsonSerializer.Serialize(models, opts))
            .Replace("__ANIMS__", JsonSerializer.Serialize(anims, opts));
        var path = System.IO.Path.Combine(outDir, "catalog.html");
        File.WriteAllText(path, html);
        log($"catalog.html        {models.Count} model(s), {anims.Count} animation archive(s)");
    }

    private static List<ModelRow> ReadModels(string outDir, string game)
    {
        var rows = new List<ModelRow>();
        var tsv = System.IO.Path.Combine(outDir, "models.tsv");
        if (!File.Exists(tsv)) return rows;
        foreach (var line in File.ReadLines(tsv).Skip(1))
        {
            var c = line.Split('\t');
            if (c.Length < 8) continue;
            var (stem, path) = (c[0], c[7]);
            var folder = FolderOf(path);
            var cat = Categorize(stem, folder);
            int.TryParse(c[1], out var bones);
            int.TryParse(c[2], out var meshes);
            rows.Add(new ModelRow(stem, game, cat, Describe(stem, folder), bones, meshes, path));
        }
        return rows;
    }

    /// <summary>
    /// A model's category from its name, falling back to the rip folder. The
    /// name is the reliable signal: <c>hd[fm]*</c> is worn headgear (helmets /
    /// masks), not the head itself, and the real head/face models are the avatar
    /// presets <c>av[mf]N_typeN_def</c>; <c>av[mf]N_bodyN</c> is the base body.
    /// </summary>
    private static string Categorize(string stem, string folder)
    {
        bool Rx(string p) => System.Text.RegularExpressions.Regex.IsMatch(
            stem, p, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (stem.Contains("hair", StringComparison.OrdinalIgnoreCase)) return "Hair";
        if (Rx(@"^av[mf]\d+_type\d+")) return "Head";
        if (Rx(@"^av[mf]\d+_body\d+")) return "Base body";
        if (stem.Contains("hone", StringComparison.OrdinalIgnoreCase)) return "Headgear";
        if (Rx(@"^hd[fm]\d")) return "Headgear";
        if (Rx(@"^ar[fm]\d")) return "Arms";
        if (Rx(@"^lg[fm]\d")) return "Legs";
        if (Rx(@"^ua[fm]\d")) return "Upper armor";
        if (Rx(@"^bd[fm]\d")) return "Body";
        if (Rx(@"^(cr|rg)[fm]\d")) return "Chest rig";
        return Folders.TryGetValue(folder, out var f) ? f.Cat : "Characters";
    }

    private static List<AnimRow> ReadAnims(GameCatalog catalog, string game)
    {
        var rows = new List<AnimRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in catalog.Mtars.OrderByDescending(m => m.Layer))
        {
            if (!seen.Add(entry.Stem + "|" + entry.Path)) continue;
            int clips = 0, tracks = 0;
            try
            {
                if (MtarAnimSet.TryProbe(GameCatalog.Read(entry), out var probe))
                {
                    clips = probe.Ganis?.Length ?? 0;
                    tracks = probe.BoneHashes?.Count() ?? 0;
                }
            }
            catch { /* an unreadable archive still lists, just without counts */ }
            rows.Add(new AnimRow(entry.Stem, game, AnimCategory(entry.Stem),
                                 AnimDesc(entry.Stem), clips, tracks, entry.Path));
        }
        return rows.OrderByDescending(r => r.Clips).ToList();
    }

    private static string FolderOf(string path)
    {
        var segs = path.Replace('\\', '/').Split('/');
        return segs.Length > 3 ? segs[3] : "";
    }

    private static string Gender(string stem)
    {
        var m = System.Text.RegularExpressions.Regex.Match(stem, "^(ar|hd|lg|ua|bd|cr)([fm])\\d");
        if (m.Success) return m.Groups[2].Value == "f" ? "Female" : "Male";
        if (System.Text.RegularExpressions.Regex.IsMatch(stem, "_def_f\\b|_f$")) return "Female";
        if (stem.StartsWith("avf")) return "Female";
        if (stem.StartsWith("avm")) return "Male";
        return "";
    }

    private static string Describe(string stem, string folder)
    {
        var bits = new List<string>();
        string known = null;
        foreach (var key in new[] { stem.Split('_')[0], Take(stem, 3), Take(stem, 4), Take(stem, 2) })
            if (Chars.TryGetValue(key, out var v)) { known = v; break; }
        if (Folders.TryGetValue(folder, out var f))
        {
            bits.Add(f.Desc);
            if (known is not null) bits.Add($"({known})");
        }
        else if (known is not null) bits.Add(known);
        var g = Gender(stem);
        if (g.Length > 0) bits.Add(g);
        if (stem.Contains("_cov")) bits.Add("attachment/cover");
        if (stem.Contains("hair")) bits.Add("hairstyle");
        if (System.Text.RegularExpressions.Regex.IsMatch(stem, "skin\\d")) bits.Add("skin set");
        return bits.Count > 0 ? string.Join(", ", bits) : "character model";
    }

    private static string Take(string s, int n) => s.Length >= n ? s[..n] : s;

    private static string AnimCategory(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("player") || n.Contains("resident")) return "Player motion";
        if (n.Contains("facial") || n.Contains("face")) return "Facial";
        if (n.Contains("soldier")) return "Soldier / NPC";
        if (n.Contains("zombie")) return "Wanderer";
        if (n.Contains("walkergear")) return "Walker Gear";
        if (n.Contains("heli")) return "Helicopter";
        foreach (var a in new[] { "wolf", "bear", "goat", "zebra", "horse", "dog" })
            if (n.Contains(a)) return "Animal";
        foreach (var c in new[] { "kaiju", "gluttony", "aerial", "spider" })
            if (n.Contains(c)) return "Creature / boss";
        return "Other";
    }

    private static string AnimDesc(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("player") || n.Contains("resident")) return "Player motion set (locomotion lives here)";
        if (n.Contains("facial")) return "Facial animation";
        if (n.Contains("vram")) return "Streamed resident subset";
        if (n.Contains("soldier")) return "Soldier / NPC motion";
        if (n.Contains("cqc")) return "CQC";
        if (n.Contains("vehicle")) return "Vehicle";
        if (n.Contains("horse")) return "Horse riding";
        return "Animation archive";
    }

    private static string HtmlEscape(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // Interactive page. Placeholders __GAME__ / __MODELS__ / __ANIMS__ are filled
    // in above. Kept as one string so the whole catalogue is a self-contained file.
    private const string Template = """
<!DOCTYPE html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Fox Engine Asset Catalog — __GAME__</title>
<style>
:root{--bg:#0f1216;--panel:#171b21;--panel2:#1e242c;--line:#2a323c;--ink:#e6edf3;--dim:#8b98a5;--accent:#6cc0ff;--accent2:#7ee0b8;--chip:#232b34}
@media(prefers-color-scheme:light){:root{--bg:#f6f8fa;--panel:#fff;--panel2:#f0f3f6;--line:#d8dee4;--ink:#1c2128;--dim:#5a6673;--accent:#0969da;--accent2:#1a7f52;--chip:#eef1f4}}
*{box-sizing:border-box}body{margin:0;font:14px/1.5 -apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,Arial,sans-serif;background:var(--bg);color:var(--ink)}
header{position:sticky;top:0;z-index:10;background:var(--panel);border-bottom:1px solid var(--line);padding:14px 20px}
h1{margin:0 0 2px;font-size:18px}.sub{color:var(--dim);font-size:12.5px}
.tabs{display:flex;gap:6px;margin-top:12px}.tab{padding:7px 14px;border-radius:8px 8px 0 0;border:1px solid var(--line);border-bottom:none;background:var(--panel2);color:var(--dim);cursor:pointer;font-size:13px;font-weight:600}.tab.active{background:var(--bg);color:var(--accent);border-color:var(--accent)}
.controls{display:flex;flex-wrap:wrap;gap:8px;margin-top:10px;align-items:center}
input[type=search]{flex:1;min-width:220px;padding:9px 12px;border-radius:8px;border:1px solid var(--line);background:var(--panel2);color:var(--ink);font-size:14px}
select{padding:9px 10px;border-radius:8px;border:1px solid var(--line);background:var(--panel2);color:var(--ink);font-size:13px}
.count{color:var(--dim);font-size:12.5px;margin-left:auto}
main{padding:12px 20px 60px;max-width:1150px;margin:0 auto}
details.cat{margin:0 0 2px;border:1px solid var(--line);border-radius:8px;overflow:hidden;background:var(--panel)}
details.cat>summary{cursor:pointer;padding:9px 14px;font-size:13.5px;font-weight:600;color:var(--accent2);list-style:none;display:flex;gap:8px;align-items:center;background:var(--panel2)}
details.cat>summary::-webkit-details-marker{display:none}
.cnt{margin-left:auto;color:var(--dim);font-weight:400}
table{width:100%;border-collapse:collapse;font-size:13px}tbody tr{border-top:1px solid var(--line)}tbody tr:hover{background:var(--panel2)}
td{padding:6px 10px;vertical-align:top}td.name{font-family:ui-monospace,Menlo,monospace;font-size:12.5px;white-space:nowrap}td.meta{color:var(--dim);white-space:nowrap;font-size:12px}td.path{color:var(--dim);font-family:ui-monospace,monospace;font-size:11.5px;word-break:break-all;max-width:340px}
th{text-align:left;padding:6px 10px;color:var(--dim);font-weight:600;font-size:11px;text-transform:uppercase;letter-spacing:.4px;border-bottom:1px solid var(--line)}
mark{background:rgba(108,192,255,.28);border-radius:2px}.empty{padding:24px;text-align:center;color:var(--dim)}
.doc{max-width:760px}.doc h2{font-size:15px;margin:18px 0 6px;color:var(--accent2)}.doc p{margin:6px 0}.doc code{background:var(--chip);padding:1px 5px;border-radius:4px;font-size:12.5px}.doc ol{padding-left:20px}.doc li{margin:4px 0}
</style></head><body>
<header>
  <h1>Fox Engine Asset Catalog</h1>
  <div class="sub">__GAME__ — models and animation archives, with plain-language descriptions. Generated by foxanimrip --inventory.</div>
  <div class="tabs" id="tabs"></div>
  <div class="controls" id="ctrls">
    <input id="q" type="search" placeholder="Search name, description or path…">
    <select id="cat"><option value="">All categories</option></select>
    <span class="count" id="count"></span>
  </div>
</header>
<main id="out"></main>
<script>
const MODELS=__MODELS__, ANIMS=__ANIMS__;
const HELP=`<div class="doc">
<h2>What this is</h2><p>Every character model and animation archive the inventory found in this game, described in plain language. The <b>Models</b> and <b>Animations</b> tabs are searchable and grouped by category.</p>
<h2>Getting a character into Blender</h2>
<p>Fox Engine does not store a finished character as one file. A created soldier is a minimal <b>base body</b> plus interchangeable parts (head, arms, legs, chest, armour, hats), all on one player skeleton.</p>
<ol>
<li>Install the FoxBrowser Import add-on, then in the sidebar (<code>N</code> panel → FoxBrowser) click <b>Model(s)</b> and import a base body — <code>bsm0/bsf0</code> for Survive, <code>skl0</code> for MGO.</li>
<li>Leave the base's <b>armature selected</b>.</li>
<li>Click <b>Add Part(s) to Active Body</b> and pick the parts — from any folder, one or many. Run it again to add more. Each part snaps onto the shared skeleton.</li>
<li>Select the finished rig and use <b>Rewire Materials</b> for the full Fox Engine map treatment.</li>
</ol>
<p>Ground Zeroes characters are usually a single complete model — just import it.</p>
<h2>Animations</h2>
<p>Player locomotion lives in the player motion archive — <code>SsdPlayer_layers</code> (Survive), <code>player2_resident</code> / <code>mgoplayer_resident</code> (MGO), <code>TppGzPlayer_layers</code> (Ground Zeroes). Export clips with <code>foxanimrip --character &lt;base&gt; --mtar &lt;archive&gt; --locomotion --out &lt;folder&gt;</code>, add <code>--root-motion</code> for travelling versions.</p>
<h2>Customisation textures</h2>
<p>Skin tones and other options are texture swaps. Pull the files with <code>foxanimrip --rip-variations &lt;filter&gt; --out &lt;folder&gt;</code> (e.g. <code>ssd/fova/chara</code> for the Survive creator).</p>
</div>`;
const TABS={
 help:{label:"How to use",doc:HELP},
 models:{label:"Models",data:MODELS,cols:[["Filename","name","name"],["Description","desc","desc"],["Bones/Meshes",r=>r.bones+"b · "+r.meshes+"m","meta"],["Path","path","path"]],search:r=>r.name+" "+r.desc+" "+r.path},
 anims:{label:"Animations",data:ANIMS,cols:[["Archive","name","name"],["What it is","desc","desc"],["Clips",r=>r.clips,"meta"],["Tracks",r=>r.tracks,"meta"],["Path","path","path"]],search:r=>r.name+" "+r.desc+" "+r.path},
};
let cur="help";
const out=document.getElementById('out'),q=document.getElementById('q'),catSel=document.getElementById('cat'),countEl=document.getElementById('count'),tabsEl=document.getElementById('tabs'),ctrls=document.getElementById('ctrls');
Object.entries(TABS).forEach(([k,t])=>{const b=document.createElement('div');b.className='tab'+(k==cur?' active':'');b.textContent=t.label+(t.data?" ("+t.data.length+")":"");b.onclick=()=>{cur=k;switchTab();};b.dataset.k=k;tabsEl.appendChild(b);});
function esc(s){return (s+'').replace(/[&<>]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]));}
function hi(s,t){s=s+'';if(!t)return esc(s);const i=s.toLowerCase().indexOf(t);return i<0?esc(s):esc(s.slice(0,i))+'<mark>'+esc(s.slice(i,i+t.length))+'</mark>'+esc(s.slice(i+t.length));}
function switchTab(){[...tabsEl.children].forEach(b=>b.classList.toggle('active',b.dataset.k==cur));const t=TABS[cur];
 if(t.doc){ctrls.style.display='none';out.innerHTML=t.doc;return;}
 ctrls.style.display='flex';const cats=[...new Set(t.data.map(r=>r.cat))].sort();catSel.innerHTML='<option value="">All categories</option>'+cats.map(c=>'<option>'+esc(c)+'</option>').join('');q.value='';render();}
function render(){const t=TABS[cur];if(t.doc)return;const term=q.value.trim().toLowerCase(),fc=catSel.value;
 let rows=t.data.filter(r=>!fc||r.cat===fc);if(term)rows=rows.filter(r=>t.search(r).toLowerCase().includes(term));
 countEl.textContent=rows.length+' of '+t.data.length;out.innerHTML='';if(!rows.length){out.innerHTML='<div class="empty">No matches.</div>';return;}
 const byCat={};rows.forEach(r=>{(byCat[r.cat]=byCat[r.cat]||[]).push(r);});
 const names=Object.keys(byCat).sort((a,b)=>byCat[b].length-byCat[a].length||a.localeCompare(b));
 for(const c of names){const list=byCat[c];const d=document.createElement('details');d.className='cat';d.open=term.length>0||names.length<=5;
  let h='<table><thead><tr>'+t.cols.map(x=>'<th>'+x[0]+'</th>').join('')+'</tr></thead><tbody>';
  list.sort((a,b)=>(a[t.cols[0][1]]+'').localeCompare(b[t.cols[0][1]]+'')).forEach(r=>{h+='<tr>'+t.cols.map(col=>{const v=typeof col[1]==='function'?col[1](r):r[col[1]];return '<td class="'+col[2]+'">'+hi(v,term)+'</td>';}).join('')+'</tr>';});
  h+='</tbody></table>';d.innerHTML='<summary>'+esc(c)+'<span class="cnt">'+list.length+'</span></summary>'+h;out.appendChild(d);}}
q.oninput=render;catSel.onchange=render;switchTab();
</script></body></html>
""";
}
