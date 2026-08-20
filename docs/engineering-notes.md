# Engineering notes: Fox Engine data, and debugging it

Hard-won specifics from building (and then scrapping) a standalone renderer for
this data. Kept because every one of these cost real time to find, and all of
them apply to any future tool that consumes FoxBrowser's output — including the
Blender add-on.

---

## FoxBrowser's export scene is not the model's skeleton

`ExportScene.Build(model, ...)` returns a bone list that differs from
`FmdlModel.Bones` in two ways that both bite:

1. **It appends a synthetic `[Root]`** (identity, at the origin). The mesh's
   skin indices and any manifest you write from the scene count it; the model
   has no transform for it. Writing per-frame data over `model.Bones` while
   publishing `scene.Bones` puts every frame one bone out of step — the symptom
   is a character folded flat on the floor, not an error.
2. **`[Root]` goes on the END of the list**, so bone 0 (`SKL_000_WAIST`) has a
   parent index of 121/138 — a bone that appears *after* it. Anything chaining
   parents in array order reads a matrix that has not been computed yet.

Fixes: bake over the scene's bone list, mapping back to the model by resolved
bone name and writing identity for the synthetic root; and evaluate bones in
depth order (parents first), never array order.

Useful invariant for validating a hierarchy: with a translation-only bind pose,
`world[i] == world[parent] + local[i]` must hold for every bone.

## Fox bind poses carry no rotation

Bone rest is a position only — which is why `ExportBone` exposes `Local` and
`World` as `Vector3`. Skinning can therefore use `translation(-bindWorld) *
animWorld` as the inverse-bind. If that ever stops being true, this breaks
silently and looks like shearing.

## Rig quality is not optional, and `--min-match 8` is a low bar

Generic core bones (waist, spine, chest) are shared by nearly every humanoid
skeleton, so a 15-bone prop skeleton can "match" a full player animation set.
Always check how many bones a clip actually drives — 44 of 53 is a real bind,
6 is a stub. `--list-rigs` shows the rig ranking for a model, and the export log
prints `N of M bones ... (X % of the rig)`.

## Verifying a pose without eyeballing it

Two cheap checks catch most reconstruction errors:

- **Bone length must be constant.** Compare each bone's distance from its
  parent against the bind pose across frames. Real `SKL_` bones stay within a
  couple of percent; only `_HLP` help bones (muscle/twist, driven by the FRDV
  operators) move, and their bind lengths are millimetres so their *relative*
  deviation is huge and harmless.
- **Head above foot.** A sane pose has the head 1.5–1.8 m above the planted
  foot standing, ~1.0 m crouched. A collapsed hierarchy shows up immediately.

Both run on exported data alone, with no renderer.

## DXT textures: decode them yourself

Handing raylib (or anything calling `glCompressedTexImage2D` per mip level) a
compressed DDS **with a full mip chain** killed the NVIDIA driver outright —
process gone, no GL error, no managed exception, nothing to catch. The same
bytes upload fine under Mesa/llvmpipe, so it reproduces on one machine and not
another.

The data was not at fault: every DDS was verified byte-exact against its own
header (`sum over mips of blocks × blockBytes` + 128-byte header).

Decoding BC1/BC2/BC3 by hand is ~150 lines and removes the entire failure
class. Sanity values for Fox textures, useful as a decoder self-check:

| suffix | meaning | expected mean RGB |
|--------|---------|-------------------|
| `_bsm` | base / albedo | scene-dependent; skin ≈ (117, 87, 75) |
| `_nrm` | normal, DXT5nm packed | ≈ (132, 127, 132) — flat normal |
| `_srm` | spec / roughness | channel-separated, often one channel ≈ 0 |
| `*_alp` | alpha-bearing | low mean alpha (hair ≈ 46, eyelashes ≈ 19) |

## Debugging a process that dies without unwinding

A native access violation takes the process down with no stack unwind, and —
because C `stdio` is block-buffered when redirected to a file — **the tail of
the log is lost**. Twice this pointed the investigation at the wrong line: the
last line written was not the last line executed.

The fix that actually worked: a trace file flushed to disk after every entry
(`StreamWriter.Flush()` *and* `BaseStream.Flush()`), written by managed code
around each suspect step. The last line in that file is then genuinely the last
thing that happened. Pair it with bisect switches (skip textures, cap mesh
count, run N frames and exit) so one run localises the fault.

## Headless rendering is available in this container

`Xvfb` and Mesa are installed, so a GL application can be run and
**screenshotted** without a display:

```
LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe \
  xvfb-run -a -s "-screen 0 1280x720x24" ./app --shot out.png --frames 3
```

This turned "the user says it looks wrong" into "look at the pixels". It also
proves the difference between a data bug (reproduces everywhere) and a driver
bug (reproduces on one GPU) — which is exactly how the DXT crash above was
isolated.

Note it cannot reproduce vendor-specific driver behaviour, and a HiDPI desktop
introduces its own scaling that a headless run will not show.

## HiDPI on Windows

A window created at 1600×900 that screenshots at 2560×1440 is 160 % display
scaling. Without a high-DPI window flag the framebuffer is physical while the
toolkit reasons in logical pixels, and the 3D view is stretched while the 2D
overlay looks correct. Enable the toolkit's high-DPI flag; do not attempt to
"fix" it by substituting a hand-built projection matrix — `System.Numerics`
builds **left-handed** matrices and most GL wrappers expect right-handed, which
puts the scene behind the camera.

## Building this repo

The projects target **.NET 10**; a container with only the .NET 8 SDK cannot
build them. `dotnet-install.sh --channel 10.0 --install-dir /opt/dotnet10`
provides it, then `DOTNET_ROOT=/opt/dotnet10 PATH=/opt/dotnet10:$PATH`.

`FoxAnimRip.Core` compiles against FoxBrowser's assemblies for API shape only
(they are never shipped; they are read from the user's own FoxBrowser at run
time). Point `-p:FoxBrowserRefDir=<folder of those DLLs>` at a folder holding
them — `tools/extract-refs.py` produces one.
