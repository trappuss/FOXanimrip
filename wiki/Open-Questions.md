# Open Questions

What is still unknown, and the specific next experiment for each. Ordered by how
much would be gained.

## 1. Where is the motion graph? **[open]**

**What we know.** The asset tree is real: 989 dictionary paths under
`/Assets/tpp/motion/motion_graph/`, organised per character, with names
mirroring the `.mtar` sets exactly (`TppGzPlayer_layers` appears in both trees).
No file carrying extension code 4752 (`.mog`) exists in TPP, GZ or Survive —
and that test is sound, since the same mechanism finds 19,138 `.fmdl` and 1,236
`.mtar` in TPP.

**The experiment.** Take the known `motion_graph` paths and compute `PathCode`
for each against **every one of the ~47 known extension codes**, then probe the
archives for a hit. A single match names the real extension and opens the whole
system. If nothing matches, extend the sweep to all 8,192 possible 13-bit codes
— it is only 989 × 8,192 hash lookups.

**If it is found,** the payoff is large: blend durations, transition conditions,
and the parametric travel rule — the three things the clip inventory cannot
tell you.

## 2. What are the smaller texture map types? **[open]**

`_hnm` (872), `_mtm` (806), `_dtm` (773), `_trm` (679), `_sdm` (10), `_spm` (3)
are named consistently but their shader consumption was never confirmed. The
answer is in `.fsop` (compiled shaders) or `.fmtt` (material parameter
definitions), neither of which was decoded here.

## 3. Shader and material formats **[open]**

`.fsop` (`TppShaders_dx11.fsop`, `GrModelShaders_dx11.fsop`) and `.fmtt`
(`material_params.fmtt`, four per game) are untouched. Decoding them would give
exact material reconstruction rather than the physically-plausible approximation
current tools produce.

## 4. Simulation parameters **[open]**

130 `_SIM` bones drive cloth, straps and hair, but the parameters governing them
were not located. Candidates by name: `.clo` (5387), `.fclo` (5785),
`.sim` (1682), `.phsd` (479), `.ph` (5527).

## 5. Unidentified extension codes **[open]**

Several codes appear in the histograms with no name: `1531` (19 files in
Survive, sample `DefenseGameBaseDigging`), `7263` (8, `.wmv` video), `422`
(`.wem` audio), `5520` (`foxpatch`), `1740` (`.ffnt` fonts). Mostly minor, but
`1531` is Survive-specific game data.

## 6. Ground Zeroes' extension enumeration **[inferred]**

GZ's extension codes are small and near-sequential (`104` = `.ftex`, `192`–`200`
= `.1`–`.5.ftexs`) rather than sparse 13-bit hashes. This looks like a table
index rather than a hashed extension string, but the table itself was not
located. Anyone working seriously with GZ should establish it properly.

## 7. Dictionary coverage **[open]**

Roughly a third of textures resolve to no name. Those files extract perfectly
but are anonymous. We tested whether hash-named avatar textures could be
recovered by brute-forcing plausible names against the hash — 42,529 candidates
generated from the observed naming conventions, **zero matches**. The names do
not follow the conventions we could infer, so recovering them needs either a
larger real-path corpus or strings from the executable.

## 8. The `--all-sets` anchor defect **[known bug, unfixed]**

Ranks candidate skeletons by first-over-threshold instead of best-match; 209 TPP
sets bind to a 15-bone stand-in. Full description in
[Toolchain and Methods](Toolchain-and-Methods) §5. This is a defect in our tool,
not in the game data, and the fix is well understood — rank by matched-bone
count and re-rip.

## 9. Facial and additive layers **[open]**

Every character has facial sets (`player2_*_facial`, `TppMaleFacial`) and the
`_layers` naming implies an additive layering scheme over base locomotion.
Neither the layer blend weights nor the facial rig's parameterisation was
examined.
