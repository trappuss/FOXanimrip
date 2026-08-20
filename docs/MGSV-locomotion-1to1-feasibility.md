# Recreating MGSV Locomotion 1:1 — Feasibility & Data Inventory

*Research pass across the TPP/GZ game files, the FoxBrowser/foxanimrip internals, and the
public reverse-engineering record (MGSV Modding Wiki, ZenHAX, GitHub tooling, datamining/
speedrun communities). Sources listed at the end.*

---

## The honest verdict, up front

**Can I be confident of an exact 1:1 recreation of MGSV movement purely from researching the
game files, with no reference footage and no iterative tinkering? No — not honestly, and I'd
be lying to you if I said yes.** But that flat "no" hides a much more useful graded answer,
because the movement system is really three layers stacked on top of each other, and they sit
at very different distances from "1:1 from files alone":

| Layer | What it is | 1:1 from files alone? |
|---|---|---|
| **Kinematic / asset layer** | The actual poses, per-clip travel speed, turn arcs, foot placement, IK chains, look-at drivers | **Yes — essentially 1:1**, and most of it is *already in your rip* |
| **Blend / state layer** | Which clip plays when, cross-fade durations, transition conditions, additive aim layering, momentum | **Partly** — topology is recoverable, exact timings are not (yet) |
| **Controller / physics / feel layer** | Input response curves, dead zones, acceleration ramps, capsule/step/slope, camera coupling | **No** — this is compiled engine code, not data |

So the truthful shape of it: you can get the *look and the kinematics* to genuinely 1:1, you
can get the *blend structure* very close, and the *last mile of "feel"* cannot be lifted out
of files as exact numbers — it has to be either measured from the game or reverse-engineered
out of the executable. Anyone who tells you a from-files-only rebuild will feel frame-for-frame
identical with zero tuning is selling you something. What's real is "indistinguishable to
almost anyone, after a bounded measurement pass" — and I'll lay out exactly what that pass is.

---

## Why: how Fox Engine actually drives locomotion

Fox Engine cleanly separates **compiled C++ modules** from the **data files** they read. The
module list is recovered directly from the executable's own memory tags, and the split matters
enormously here:

- **`Anim`** — decodes and plays raw animation clips (`.mtar` / `.gani`).
- **`MotionGraph`** — the runtime that turns input + game state into *which clips blend, at what
  weights, transitioning how*. This is the "controller" of locomotion. It is a **compiled
  module**, but it reads a data file: the executable has a distinct `MotionGraph File` memory
  tag, which proves the graph itself is loaded from a file (the `.mog` format), not hardcoded.
- **`Character`** and **`Ph`/`Phx`** — the character controller and physics (capsule, collision,
  slopes). Compiled; **no** third-party middleware is named in any credits (PCGamingWiki lists
  only Wwise for audio), so this is a custom in-house physics/controller stack with **no public
  spec**.
- **Lua (5.1)** — sits on top and *gates and triggers* ("disable crawl", "play this motion")
  but contains **no numeric movement tuning**. The deminified `TppPlayer.lua` (3,100+ lines) has
  the stance enums (`Stand`/`Squat`/`Crawl`/`Dash`) and a couple of unrelated timing constants,
  and that's it — the speeds and blend times are *not* in script.

The consequence: the movement *content* is data (recoverable), the movement *logic* is split
between a partly-decoded data file (`.mog`) and un-decompiled engine code, and the movement
*tuning constants* are compiled in.

Crucially, MGSV is a **root-motion-driven** locomotion system — the animations themselves carry
the travel. That single fact is what rescues a lot of the "unknown" numbers, as explained next.

---

## The fidelity ledger — line by line

### Fully recoverable from files, to 1:1 (and mostly already in hand)

- **Every locomotion clip**, both genders, rooted and unrooted — you have ~42k FBX from the
  all-anims rip. This is the ground-truth pose data; foot IK is *baked into the curves*, so the
  terrain-adapted foot placement for each authored motion comes with the clip.
- **Per-clip authored speed, displacement, and turn arc.** Because it's root-motion, the walk /
  jog / crouch-walk / prone-crawl / aim-move *speeds* are literally the root velocities of the
  corresponding clips — which you already ripped in the `root-motion/` trees. The public record
  says "walk/crouch/prone speeds are undocumented," and that's true of the *engine constant*, but
  it's the wrong place to look: the number is measurable off your own root-motion FBX to the
  authored frame. This is a big deal — it moves most of the "unknown speeds" into "already have."
- **The 8-way locomotion blendspaces** (walk/run × stand/crouch) — the tool already detects these
  grids, which is the exact structure a blendspace needs.
- **The IK rig topology** — `.frig` gives the 2-bone leg IK and 3-bone arm IK chains, effectors,
  plane normals, and per-layer mask weights. The foxanimrip/FoxBrowser stack **already parses
  `.frig`** (`FrigFile`).
- **The driver/helper-bone system** — `.frdv` gives look-at, twist, and corrective-rotation
  drivers with their **numeric limits in degrees**, source/target bones, and up/forward vectors.
  The stack **already parses `.frdv`** (`FrdvFile`), and there's a `frdvdump` command.
- **The skeleton + mesh** — `.fmdl`, already ripped; FMDL Studio v2 imports it live into Unity.

### Recoverable in structure, but not in exact timing (the `.mog` gap)

- **The motion-graph topology** — states, blend nodes, additive layers. The community has a 010
  Editor byte-template (`mog.bt`, kapuragu/FoxEngineTemplates) that decodes the *node types*
  (`SINGLE`, `TWO`, `LAYERS`, `SELECT`, `ADD`, `SUBTRACT`, …) and the additive-layer info. So you
  can read *what blends with what* and *how aim overlays onto locomotion*.
- **What's still sealed inside `.mog`:** the transition **conditions** and the **blend-time
  scalars** live in fields the template still labels `UnknownOffset*`. So the graph's *shape* is
  readable; the *exact cross-fade durations and trigger thresholds* are not yet — today.

### Not recoverable from files — engine code or unmeasured

- **Input→motion mapping:** stick dead zones, response curves, the acceleration ramp to top
  speed, sprint (L3) activation behavior, plant-and-turn and turn-in-place thresholds. Some of
  this is *implicit* in start/stop/turn clips (which you have), but the code that schedules them
  is in the `MotionGraph`/`Character` modules.
- **Physics controller:** capsule radius/height, step height, slope limits, ground snapping. Not
  labeled in any readable file; only extractable by inspecting a character's `.sim`/`.ph`/`.tgt`
  as raw FoxData or by reading it out of the running process.
- **Camera coupling:** follow distance/lag, default sensitivity, ADS pull-in timing. Sliders
  exist in-menu; the internal defaults aren't published.
- **Exact engine update/integration loop.** Movement is delta-time based (unlocking framerate
  doesn't speed the player up), which is good for portability, but the precise integration is
  engine code.

### Known public numbers (thin)

Only **sprint top speeds** are publicly documented and cross-corroborated: base Snake ≈ **8.33 m/s
(30 km/h)**, Cyborg Ninja skin ≈ **10.8 m/s (39)**, Raiden suit **12.5 m/s (45)**. Everything
below sprint is undocumented publicly — but, again, derivable from your root-motion clips.

---

## What you already have vs. what's genuinely missing

**Already in hand (from the rip + the tooling that parses these formats):**
clips (both genders, rooted + unrooted) · per-clip travel speed & turn (root motion) · 8-way
blendspace grids · leg/arm IK chains (`.frig`) · look-at/twist drivers with degree limits
(`.frdv`) · skeleton/mesh (`.fmdl`).

**Missing, and how each could be closed:**

1. **Blend/transition timings & conditions** — *partly closable from files.* Build a `.mog`
   reader (the neighboring formats are already parsed; a `mog.bt`-guided extractor would dump the
   player graph's states, node types, and layer wiring). The topology comes out; the exact
   durations in the `UnknownOffset` fields would need either further RE of those fields against
   the real player `.mog`, or one measurement pass.
2. **Sub-sprint speeds / accel ramps** — *closable without the game running,* by measuring root
   displacement on the clips you already exported (a script over the `root-motion/` FBX).
3. **Physics capsule / step / slope / camera** — *not closable from files;* requires reading the
   raw `.sim`/`.ph`/`.tgt` FoxData (uncertain payoff, fields unlabeled) or a short in-engine/
   memory measurement.

---

## The path that gets closest to 1:1 (and the one unturned stone)

Ordered by leverage:

1. **Mine root motion from the clips you already have.** A pass over the `root-motion/` trees
   yields authored per-clip velocity, displacement, and turn rate for every locomotion state and
   direction. This converts most "unknown speeds/turns" into exact numbers, from files only.
2. **Build a `.mog` extractor into foxanimrip.** This is the single highest-value unturned stone.
   The tool already reads the sibling formats (`.mtar`, `.gani`, `.frig`, `.frdv`); a `.mog`
   parser (guided by the public `mog.bt` template) would dump the player's motion-graph topology —
   states, blend-node types, additive aim layers — which is the blend *structure* nobody in the
   community has actually pulled for the player. That takes you from "I have the clips" to "I have
   the clips *and* the graph that sequences them."
3. **Rebuild the IK + drivers 1:1** from `.frig`/`.frdv` (already parsed) in the target engine —
   these are exact, so foot IK behavior and look-at/aim drivers match by construction.
4. **The residual last mile** — exact cross-fade durations, input response curves, capsule/step/
   slope, camera feel — is where "no reference, no tinkering" genuinely breaks down. Realistically
   it needs *one* systematic measurement pass against the game (not open-ended tinkering — a
   defined capture of transition frame-counts and controller response), or a deeper RE effort into
   the `MotionGraph`/`Character` modules and the `.mog` `UnknownOffset` fields.

If steps 1–3 are done, what you have is a movement system built from MGSV's own clips, own
blendspace grids, own IK rig, own driver bones, and own motion-graph topology — which will *read*
as MGSV to essentially anyone. Step 4 is the difference between "reads as MGSV" and "is provably
frame-identical," and that difference cannot be closed from files alone.

---

## Bottom line

- **Kinematics and content: yes, 1:1 from files** — and you're most of the way there already.
- **Blend structure: recoverable** (topology now via a `.mog` reader; exact timings need one
  measurement or deeper RE).
- **Controller feel + physics: not obtainable from files as exact numbers** — this is the part
  that keeps me from honestly promising "1:1 with zero reference and zero iteration."

The most confident *true* statement I can make: with the root-motion mining and a `.mog`
extractor — both of which are file-only and buildable into the tool you already have — you reach
a recreation that is faithful in content, kinematics, blendspace, IK, and graph topology, and
whose *remaining* deviation from the original is confined to blend-durations and controller feel,
closable by a single bounded measurement pass rather than endless tinkering. That's the real
ceiling, and it's a high one — just not the literal "everything, exactly, from files, first try"
that the question asks for.

---

## Sources

Fox Engine module/format record: MGSV Modding Wiki — Fox modules (`Anim`/`MotionGraph`/`Character`/
`Ph`) https://mgsvmoddingwiki.github.io/Fox/ · Memory tags (`MotionGraph File`) https://mgsvmoddingwiki.github.io/Memory_Tags/ ·
File formats https://mgsvmoddingwiki.github.io/File_Formats/ · Lua https://mgsvmoddingwiki.github.io/Lua/
Format tooling & templates: Atvaark/TPP.FileFormats https://github.com/Atvaark/TPP.FileFormats ·
kapuragu/FoxEngineTemplates (`mog.bt`, `frig.bt`, `frdv.bt`, `gani.bt`, …) https://github.com/kapuragu/FoxEngineTemplates ·
youarebritish/FoxLib https://github.com/youarebritish/FoxLib · BobDoleOwndU/MtarTool https://github.com/BobDoleOwndU/MtarTool ·
kapuragu/FrdvTool https://github.com/kapuragu/FrdvTool · BobDoleOwndU/FMDL-Studio-v2 https://github.com/BobDoleOwndU/FMDL-Studio-v2
Animation RE (IK baked into clips, SMD export): ZenHAX id-daemon http://zenhax.com/viewtopic.php@t=3172.html ·
unknown321 motions research https://unknown321.github.io/mgsv_research/motions.html · choc player-motions guide https://chocmake.github.io/guides/mgsv-adding-player-motions/
Player Lua (no tuning constants): TinManTex/mgsv-deminified-lua https://github.com/TinManTex/mgsv-deminified-lua ·
MockFox https://github.com/TinManTex/MockFox
Movement numbers: Advanced Game Mechanics (Steam) https://steamcommunity.com/sharedfiles/filedetails/?id=530056251 ·
GameFAQs speed timings https://gamefaqs.gamespot.com/boards/718564-metal-gear-solid-v-the-phantom-pain/72545106 ·
framerate/delta-time behavior https://www.nexusmods.com/metalgearsolidvtpp/mods/1485
Physics middleware (none named): PCGamingWiki https://www.pcgamingwiki.com/wiki/Metal_Gear_Solid_V:_The_Phantom_Pain
