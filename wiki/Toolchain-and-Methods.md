# Toolchain and Methods

How every number in this handbook was produced, and how to check a result before
believing it.

## 1. The stack

- **FoxBrowser** — reads the archives and decodes FMDL, GANI, FRIG, FRDV and
  FTEX. Its assemblies do the format work; nothing here reimplements a Fox
  format parser.
- **foxanimrip** (this repository) — drives those assemblies in bulk: catalogues
  archives, pairs models with animation sets, exports FBX, and measures.
- **io_foxbrowser** — the Blender add-on: imports the results, rebuilds
  materials the FBX cannot carry, and assembles characters from parts.

Dictionaries must be staged before anything else; without them every bone is
`bone_<hex>` and clips will not bind.

## 2. Commands that answer questions

| question | command |
|----------|---------|
| what animation sets exist? | `--list-sets [text]` |
| what clips are in one? | `--list-clips <set>` |
| which models can play this set? | `--for-mtar <set>` |
| which sets fit this model? | `--list-mtars` |
| why is this set not offered? | `--why-mtar <set>` |
| which rig will be used? | `--list-rigs` |
| what file types does this install hold? | `--ext-histogram <f>` |
| every model, texture and variation | `--inventory <dir>` |
| what does a variation actually swap? | `--rip-variations <filter>` |
| how fast was this clip authored? | `--measure` |
| summarise those measurements | `--analyze-locomotion <path>` |

`--why-mtar` and `--list-rigs` are the diagnostic pair: when clips come out
distorted, the top row of `--list-rigs` is the rig being used, and `--why-mtar`
explains a pairing decision.

## 3. Invariants — check results, do not eyeball them

Rendering something and looking at it is a weak test; several genuinely broken
poses look plausible. These checks are cheap and decisive, and run on exported
data with no renderer.

**Hierarchy consistency.** With Fox's translation-only bind pose:

```
world[i] == world[parent[i]] + local[i]      for every bone
```

**Bone length is constant.** Compare each bone's distance to its parent against
the bind pose, across frames. Real `SKL_` bones hold within ~2 %. Only `_HLP`
help bones move, and because their rest lengths are millimetres their relative
deviation is enormous and harmless — expect thousands of percent on
`SKL_511_RHMRS_HLP` and do not panic.

**Head above foot.** A sane pose puts the head 1.5–1.8 m above the planted foot
standing, ~1.0 m crouched. A collapsed hierarchy fails this immediately.

**Matched-bone count.** 44 of 53 is a real bind; 6 is a stub. This one number
catches the most damaging class of error (§5).

**Texture decode means.** See [Textures and Materials](Textures-and-Materials) —
normal maps must land on ≈(132, 127, 132).

## 4. Debugging things that do not throw

Three failure modes cost the most time, all of them silent:

**Native crashes unwind nothing.** A driver-level access violation kills the
process with no exception and no stack. Worse, C `stdio` is block-buffered when
redirected to a file, so *the tail of the log is lost* — the last line written
is not the last line executed. This sent two investigations to the wrong place.
The fix that works is a trace file flushed to disk after every entry, written by
your own code around each suspect step, plus bisect switches (skip textures, cap
mesh count, run N frames and exit) so one run localises the fault.

**Silent data loss looks like a design choice.** A texture read without its
streamed mips is not an error — it is a smaller texture. A clip bound to the
wrong skeleton is not an error — it is a stiffer animation. Neither reports
anything. Only the invariants catch them.

**Reasoning from an unsound test.** The extension histogram appeared to prove
`.mog` does not ship. It did not, because that same histogram reports zero
`.fmdl` — which obviously exists. Always ask what a test says about something
you already know the answer to.

## 5. The `--all-sets` anchor defect

`--all-sets` pairs every animation set with the skeleton that best fits it. In
phase 2 it accepted the **first** candidate clearing the 8-matched-bones bar
instead of the **best** one. Because generic core bones are shared by nearly
every humanoid skeleton, **209 TPP sets — including `player2_resident` — were
bound to `Inf0_main0_def0`, a 15-bone stand-in.** Clips exported that way drive
about six bones and animate no legs.

**Status: unfixed.** The fix is to rank candidates by matched-bone count
(tie-break on cached rig, then bone count) rather than take the first over
threshold, then re-rip.

**Until then:** the `--all-sets` tree is fine for surveying what exists, but
**per-character rips are the trustworthy route for animation work**.
`test-rip-loco-loops.bat` rips the gait loops correctly against real player
models — 287 clips at 53/53 rig bones.

## 6. Headless verification

A GL application can be run and screenshotted with no display, which turns "it
looks wrong" into "look at the pixels":

```
LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe \
  xvfb-run -a -s "-screen 0 1280x720x24" ./app --shot out.png --frames 3
```

It also separates a **data** bug (reproduces everywhere) from a **driver** bug
(reproduces on one GPU) — which is exactly how the compressed-texture crash was
isolated. It cannot reproduce vendor-specific driver behaviour, and it will not
show HiDPI scaling problems.

## 7. Build requirements

The tool targets **.NET 10**. `FoxAnimRip.Core` compiles against FoxBrowser's
assemblies **for API shape only** — they are never shipped, and are read from
the user's own FoxBrowser installation at run time. Point
`-p:FoxBrowserRefDir=<folder>` at a folder holding them;
`tools/extract-refs.py` produces one.

```
dotnet publish src/FoxAnimRip.Headless -c Release -r win-x64 \
    --self-contained false -p:PublishSingleFile=true \
    -p:FoxBrowserRefDir=/path/to/refs
```

## 8. Reproducing the measurements

```
test-measure-locomotion.bat   every player clip in all three games, measured,
                              then summarised into cruise-table.tsv
test-rip-loco-loops.bat       gait loops with a correctly solved rig
test-ext-histogram.bat        file-type census per install
test-rip-all-anims.bat        the full sweep (see §5 before trusting it)
```
