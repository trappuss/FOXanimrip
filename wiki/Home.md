# The Fox Engine Asset Handbook

Field notes on the internals of **Metal Gear Solid V: Ground Zeroes**, **The
Phantom Pain** (including Metal Gear Online 3) and **Metal Gear Survive** — the
archives, the models, the textures, the rig, the animation system, and the
locomotion data in unusual depth.

Konami's Fox Engine is finished; no further games will be built on it. What is
in these files is all there will ever be. This handbook exists so that the next
person to open them does not have to rediscover the same things.

## Ground rules

Everything here was measured or decoded from shipped game files, and each claim
says how it was established. Where something is inferred rather than proven it
is marked **[inferred]**; where it is unresolved it is marked **[open]**. A few
places correct earlier conclusions of our own that turned out to be overstated —
those are left visible on purpose, because the reasoning is the useful part.

Counts come from a full pass over three installs: **576,760** files in TPP,
**331,470** in Survive, **55,534** in GZ.

## Contents

| Page | What it covers |
|------|----------------|
| [Archives and Hashing](Archives-and-Hashing) | `.dat`/`.g0s` archives, the 64-bit path hash, the extension-code table, name dictionaries, FPK containers |
| [Models and Skeletons](Models-and-Skeletons) | FMDL structure, bone naming, the 53-bone rig standard, FRIG rig units and IK, FRDV help bones, bind-pose properties |
| [Textures and Materials](Textures-and-Materials) | FTEX/FTEXS streamed mips, the map-suffix vocabulary, DXT5nm normal packing, PFTXS, decode pitfalls |
| [Animation System](Animation-System) | MTAR archives, GANI clips, the 59.94 fps basis, rig binding, how a clip is decoded and solved |
| [**Locomotion Deep Dive**](Locomotion-Deep-Dive) | The centrepiece: how MGSV's movement is built, why root travel is zero, how to recover authored speed, the full measured speed table, and the clip-name grammar that encodes the state machine |
| [Characters and Customization](Characters-and-Customization) | Character asset layout, the MGO avatar part system, FOVA form variation, Survive's differences |
| [Toolchain and Methods](Toolchain-and-Methods) | How to extract and verify all of this, including the invariants that catch a wrong result |
| [Open Questions](Open-Questions) | What is still unknown, and the specific next experiment for each |
| [Prior Art and Tools](Prior-Art-and-Tools) | The other projects that have opened these files, what each one knows, and which to reach for |

## Orientation: the three games

| | Ground Zeroes | The Phantom Pain | Survive |
|---|---|---|---|
| Files indexed | 55,534 | 576,760 | 331,470 |
| Asset root | `/Assets/tpp/` | `/Assets/tpp/`, `/Assets/mgo/` | `/Assets/ssd/` (+ reused `tpp`) |
| Extension codes | small, near-sequential | sparse 13-bit | sparse 13-bit (as TPP) |
| Player animation set | `TppGzPlayer_layers` | `player2_resident`, `mgoplayer_resident` | `SsdPlayer_layers` |
| Player skeleton | shares the human rig | 121–138 bones | 126 bones |

GZ is a smaller, earlier build of the same engine; Survive is a later fork that
kept TPP's formats, skeleton and even individual animation clips. The shared
`human_finger` rig — 53 bones — binds player animation across all three, which
is why a TPP clip drives a Survive character correctly.

---

*Compiled from the foxanimrip project. The tooling that produced every number
here is in the repository; see [Toolchain and Methods](Toolchain-and-Methods)
to reproduce any of it. Work by others — FoxBrowser, FoxKit-3, mctrollin's MTAR
tools, mgsv-lookup-strings and Fox_Parser — is credited where it is used and
collected in [Prior Art and Tools](Prior-Art-and-Tools).*
