# Prior Art and Tools

Fox Engine has been picked apart by a number of people over the years. This
page exists so the next researcher starts from what already exists rather than
from zero — and so credit sits where it belongs.

Anything in this handbook that came from one of these projects says so at the
point it is used.

---

## FoxBrowser

**The decoder this project is built on.** Reads the archives and decodes FMDL,
GANI, FRIG, FRDV and FTEX, with a rig solve and an FBX writer. `foxanimrip`
drives its assemblies rather than reimplementing any format.

If you want to *look* at assets, start here. If you want to process thousands
of them, drive it headlessly.

- https://www.nexusmods.com/metalgearsolidvtpp/mods/2531

## FoxKit-3 — Joey35233

A Unity-based Fox Engine toolkit, and **the most precise public account of the
animation track format**. `FoxKit/Assets/Fox/Anim/Playback/TrackData.cs` on the
`anim-dev` branch documents, in working code:

- the `TrackData` / `TrackMiniData` header layouts
- the `SegmentType` enum — `Quat, Float, Vector2, Vector3, Vector4, QuatDiff, VectorDiff`
- per-track `ComponentBitSize` and the unaligned bit reader
- the quaternion encoding, half-precision float decode, and the
  `PlaybackRate = 1/(60000/1001)` constant that pins playback to 59.94 fps

Read it before writing any GANI parser. Our [Animation System](Animation-System)
page §3 summarises it, but the code is the reference.

- https://github.com/Joey35233/FoxKit-3

## fox_engine_mtar_tools_blender — mctrollin

A Blender add-on that **imports *and* exports MTAR**, which matters: most tools
are read-only, so this is the one that proves a round trip is possible.

Format knowledge it makes public:

- MTAR as a container of embedded GANI files, with header-based version detection
- the **GANI1 / GANI2** split — GANI1 in Ground Zeroes and TPP facial animation,
  GANI2 newer with improved framing and track sectioning
- GANI2's **Layout Track in the negative frame range**
- that clips carry motion events, motion points and shader parameters, not just
  bone curves
- bone name remaps, rotation/translation offset correction and axis reordering
  as first-class concerns

Its stated limitations are informative in their own right: no big-endian
support, not all track types (animal legs excluded), no twist-bone
reconstruction, and repeated import/export degrades data — which is what you
would predict from per-track bit-packed precision.

- https://github.com/mctrollin/fox_engine_mtar_tools_blender

## mgsv-lookup-strings — kapuragu

**Validated dictionaries for reversing hashed names**, organised by tool
(FmdlTool, GzsTool, LangTool, …) and by `<file type>\<data type>\<game>`,
alongside raw string collections scraped from the executables.

This is the answer to the anonymous-file problem. Fox stores no paths, only
hashes, so recovering a name means having the string beforehand — and this
repository is the community's accumulated corpus, with additions validated
through Hashwrangler and a consistent sort. `_HashStringMatches.txt` files let
you go from a bare hash to a candidate string.

If a third of your textures are coming out hash-named, a fuller dictionary is
the fix, and this is where to get one.

- https://github.com/kapuragu/mgsv-lookup-strings

## Fox_Parser — Frostyman758

A broad multi-format parser — QAR, FPK, MTAR, FTEX, PFTXS, FV2, G0S, SPCH, STP,
RDF, FOX, FSOP, HLSL, TCVP, TWPF, SBP — with **a test suite per file type**,
built so that behaviour cannot silently change against the original.

Notable for reaching formats this handbook lists as unexplored, `.fsop`
(compiled shaders) and `.fv2` among them. If you are chasing one of the
[Open Questions](Open-Questions), check here first.

- https://github.com/Frostyman758/Fox_Parser

---

## Which to reach for

| goal | start with |
|------|-----------|
| look at a model or clip | FoxBrowser |
| bulk export, or measure anything | this project |
| write a GANI parser | FoxKit-3 `TrackData.cs` |
| get animation back *into* the game | mctrollin's MTAR tools |
| recover hashed names | mgsv-lookup-strings |
| a format nobody here covers | Fox_Parser |

## On hash types

Three hash schemes appear across these tools, and confusing them wastes time:

- **PathFileNameCode** — 64-bit, addresses files in the archives: 51 bits of
  CityHash64 over the lowercased extensionless path plus a 13-bit extension
  code. Described in [Archives and Hashing](Archives-and-Hashing).
- **StrCode32** and **StrCode64** — hash *strings* inside files: bone names,
  parameter names, node names. These are what a bone dictionary resolves.

A dictionary is only useful against the hash type it was built for, which is
why mgsv-lookup-strings separates them by tool and data type.
