# Textures and Materials

## 1. FTEX and the streamed mip problem

`.ftex` (extension code 685) is Fox Engine's texture container: a header plus
the **lower** mip levels inline. The high-resolution levels live in separate
numbered companion files:

```
hrs0_body0_def_bsm.ftex        header + low mips
hrs0_body0_def_bsm.1.ftexs     streamed mip data     (code 5720)
hrs0_body0_def_bsm.2.ftexs                            (code 5806)
hrs0_body0_def_bsm.3.ftexs                            (code 2787)
```

TPP ships **114,192 `.ftex`** and exactly **114,192 `.1.ftexs`** — every texture
has at least one streamed level. Survive: 72,183 of each.

**This is the single most common way to get bad results.** Read the `.ftex`
alone and you get whatever low mips it happens to hold — often 512 px or less
for an asset that ships at 2048. The symptom is a character that looks blurry
but not broken, which is easy to accept as "the game's textures".

Two further traps:

- The `.ftexs` companions frequently live in the **texture archives**, not
  beside the model. Open those archives too.
- The assembly must work **by hash** as well as by path, because roughly a third
  of textures resolve to no name. Avatar face textures are in that third: a
  path-only assembler silently leaves them at low resolution.

Assembled output is standard **DDS**, with a complete mip chain.

## 2. The map-suffix vocabulary

The last token of a texture name states its role. Census over the 388,376-entry
path dictionary:

| suffix | count | meaning |
|--------|-------|---------|
| `_bsm` | 14,159 | **base** / albedo |
| `_nrm` | 12,361 | **normal**, DXT5nm-packed |
| `_alp` | 10,375 | carries meaningful alpha |
| `_srm` | 9,822 | **specular / roughness**, channel-packed |
| `_hnm` | 872 | height / detail normal **[inferred]** |
| `_mtm` | 806 | material mask **[inferred]** |
| `_dtm` | 773 | detail **[inferred]** |
| `_lym` | 756 | layer mask |
| `_trm` | 679 | translucency **[inferred]** |
| `_msk` | 31 | mask |
| `_sdm` | 10 | — |
| `_spm` | 3 | — |

The four large families are certain. The smaller ones are named consistently but
their exact shader consumption was not confirmed.

Only **base** and **normal** are wired into an exported FBX by Fox's own
exporter; `_srm`, `_trm` and `_lym` exist on disk but nothing references them,
so a downstream tool must re-associate them by name. When a texture is
hash-named that is impossible from the name alone, which is why our exporter
writes a `*_maps.tsv` sidecar recording base/normal/spec per material.

## 3. Normal maps are DXT5nm

Fox packs normals the two-channel way: the **X** component in alpha, **Y** in
green, and Z reconstructed in the shader. Blue is unused.

The signature is unmistakable once you know it — decode any `_nrm` and its mean
RGB lands on approximately **(132, 127, 132)**, flat-normal, with alpha around
126. A naive RGB interpretation gives a washed-out purple-grey normal map that
lights almost, but not quite, correctly.

Useful decoder self-check values, measured across Snake's texture set:

| kind | expected mean |
|------|---------------|
| `_nrm` | RGB ≈ (132, 127, 132), A ≈ 126 |
| `_bsm` skin | RGB ≈ (117, 87, 75) |
| `_srm` | channel-separated; one channel often ≈ 0 |
| `*_alp` hair | A ≈ 46 |
| `*_alp` eyelashes | A ≈ 19 |

If a decoder produces those numbers on those files, it is correct — not merely
non-crashing.

## 4. Compression formats

Everything observed in character texture sets is plain **DXT1** (BC1) or
**DXT5** (BC3), with full mip chains — no BC7, no DX10 extended headers. Sizes
run 128² to 2048², with 1024×2048 common for body atlases.

Snake's 31-texture set, as an example distribution: two 2048², seven at 1024
or 1024×2048, the remainder 512² and below.

### The compressed-upload hazard

Handing a graphics API a compressed DDS **with its full mip chain** and letting
it upload every level is a known-fragile path. On one NVIDIA driver it killed
the process outright — no API error, no exception, nothing catchable — while
Mesa accepted byte-identical data without complaint.

The data was not at fault: every file verified byte-exact against its own header
via `sum over mips of blocks × blockBytes + 128`.

Decoding BC1/BC2/BC3 yourself is about 150 lines and removes the whole failure
class. BC1 is two RGB565 endpoints plus sixteen 2-bit indices; BC3 adds an
8-byte interpolated alpha block. The one subtlety is BC1's two modes: when
`c0 > c1` you get four opaque colours, otherwise three colours and a
transparent slot.

## 5. PFTXS

`.pftxs` (5727) are packed texture archives — 4,144 in TPP, 1,847 in Survive.
They bundle many textures into one file, and must be descended into like any
other container. Textures inside are subject to the same hash-naming rules.

## 6. Materials

`.fmtt` (code 164, four files per game) holds material parameter definitions,
and `.fsop` (1439) the compiled shaders (`TppShaders_dx11.fsop`,
`GrModelShaders_dx11.fsop`). Neither was decoded for this work. **[open]**

What a model gives you directly is, per material, the set of texture references
by role — which is enough to rebuild a physically-plausible material, and is what
the Blender add-on does: base to colour, DXT5nm-unpacked normal to the normal
input, and `_srm` channels to roughness and specular.
