# Archives and Hashing

How Fox Engine finds a file, and how you find one without it.

## 1. The archives

Game data lives in a handful of large archives in the install's `master/`
directory and alongside it:

- **`.dat`** — the main QAR archives (`00.dat`, `01.dat`, `chunk0.dat`,
  `data1.dat`, …). Survive's five-archive layout is typical.
- **`.g0s`** — Ground Zeroes' equivalent.
- **`.qar`** — the same container by its own name.
- **texture archives** — separate, and *easy to miss*: the streamed
  high-resolution mip data lives here rather than beside the model. A tool that
  opens only the "useful" archives will silently produce 512-pixel textures for
  assets that ship at 2048.

Inside those sit further containers:

- **`.fpk` / `.fpkd`** — packed asset bundles (18,336 and 18,268 of them in TPP).
  Most character and animation data is *inside* these, not at the archive's top
  level. This matters more than it sounds; see §4.
- **`.pftxs`** — packed texture archives (4,144 in TPP).

## 2. Names are hashes

Fox Engine does not store paths. It stores a 64-bit **PathFileNameCode**:

```
        63          51                                              0
        +-----------+-----------------------------------------------+
        | ext code  |        low 51 bits of CityHash64(path)         |
        +-----------+-----------------------------------------------+
             13 bits                     51 bits
```

- The path is the full asset path, **lowercased**, with its leading slash and
  **without** the extension — e.g. `/assets/mgo/chara/avm/pictures/body/...`
- The low 51 bits are `CityHash64` of that string.
- The top 13 bits are an **extension code** identifying the file type.

In C#:

```csharp
ulong code  = GameHash.PathCode("/Assets/mgo/chara/avm/Pictures/body/avf0_body0_def_c00_bsm.ftex");
uint  ext   = (uint)((code >> 51) & 0x1FFF);   // 685 = ftex
ulong basis = code & 0x7FFFFFFFFFFFF;          // 1125899906842623
```

Verified example — this exact path hashes to `0x1568918ace305b72`.

The consequence: **you cannot list the archives by name.** You can only ask
"does hash X exist". Recovering names needs a dictionary (§3), and the parts of
the game with no dictionary coverage are effectively anonymous.

## 3. Extension codes

Observed in TPP and Survive. These are stable between those two games:

| code | ext | code | ext | code | ext |
|-----|------|-----|------|-----|------|
| 71 | gskl | 2609 | fox2 | 5533 | xml |
| 239 | qar | 2629 | fpk | 5719 | txt |
| 479 | phsd | 3035 | des | 5727 | pftxs |
| 562 | evf | 3089 | fv2 | 5785 | fclo |
| 685 | **ftex** | 3131 | fsm | 5980 | sbp |
| 783 | lani | 3296 | **mtar** | 6407 | sani |
| 796 | lua | 3527 | spch | 6588 | frdv |
| 1172 | geom | 3609 | json | 6589 | lng |
| 1591 | fox | 3832 | subp | 6686 | aig |
| 1682 | sim | 4235 | **fova** | 7164 | htre |
| 1752 | bnk | 4244 | **fmdl** | 7189 | parts |
| 2276 | **frig** | 4752 | **mog** | 7314 | tgt |
| 2311 | aib | 5180 | nta | 7347 | ftexs |
| 2481 | vfxdata | 5387 | clo | 7359 | gpfp |
| | | 5527 | ph | 7415 | fsml |
| | | | | 7594 | fpkd |
| | | | | 7684 | nav2 |
| | | | | 7741 | lba |
| | | | | 8069 | mas |
| | | | | 8074 | **gani** |

Streamed texture mips get their own codes because the *whole* extension is
hashed, mip number included: `5720` = `.1.ftexs`, `5806` = `.2.ftexs`,
`2787` = `.3.ftexs`.

**Ground Zeroes numbers its extensions differently.** In GZ the observed codes
are small and near-sequential — `104` = `.ftex`, then `192`, `194`, `196`,
`198`, `200` for `.1`–`.5.ftexs`. That pattern is an enumerated table, not a
hash of the extension string. **[inferred]** Treat GZ extension codes as
game-specific and do not carry the TPP table across.

## 4. The trap: containers hide extensions

A file's PathFileNameCode carries usable extension bits **only when it is
addressed as a QAR path**. Entries reached *inside* an FPK container are named
directly, and a naive walk sees extension code `0` for them.

This is not a small effect. A top-level pass over TPP reports:

| code | ext | count |
|------|-----|-------|
| 0 | (container-internal) | 305,065 |
| 685 | ftex | 114,192 |
| 5720 | .1.ftexs | 114,192 |
| 2629 | fpk | 18,336 |
| 7594 | fpkd | 18,268 |
| 5727 | pftxs | 4,144 |
| 3131 | fsm | 239 |
| 8074 | gani | **1** |

Note what is *missing*: zero `.fmdl`, zero `.mtar`, zero `.frig`. Those files
obviously exist — a catalogue pass that descends properly finds **19,138 models
and 1,236 animation sets in TPP**. They were all inside the 305,065-file
container bucket.

The single top-level `.gani` in TPP (`sna0eye_blink_nomal.gani`) is a curiosity:
every other animation clip lives inside a `.mtar`.

**Consequence for reasoning:** "the extension histogram shows zero of X" is
*not* evidence that X does not ship. We made exactly that mistake once, about
the motion graph — see [Open Questions](Open-Questions).

## 5. Dictionaries

Name recovery uses community-built dictionaries, shipped with FoxBrowser:

| file | lines | what it names |
|------|-------|---------------|
| `qar_dictionary.txt` | 388,376 | asset paths (29 MB) |
| `fmdl_dictionary.txt` | 20,659 | model-internal strings, including bone names |
| `gzs_dictionary.txt` | 4,703 | Ground Zeroes paths |
| `fpk_dictionary.txt` | 2,062 | FPK-internal names |
| `bone_dictionary.txt` | 561 | skeleton bone names |

These are *lists of candidate paths*, hashed and matched against what the
archives contain. Two consequences:

1. **Coverage is partial.** Roughly a third of textures resolve to no name and
   come out hash-named. Those files are perfectly extractable — only their
   names are lost.
2. **A path in the dictionary is not proof the file ships.** The dictionary
   records paths that were *known* to exist at some point, from strings found in
   the game and from guesswork.

Paths carry no extension, which is why the extension code is a separate field.

## 6. Asset tree layout

TPP, by volume:

| path | entries |
|------|---------|
| `/Assets/tpp/level` | 106,440 |
| `/Assets/tpp/environ` | 59,406 |
| `/Assets/tpp/ui` | 36,550 |
| `/Assets/tpp/sound` | 30,417 |
| `/Assets/tpp/motion` | 29,443 |
| `/Assets/tpp/pack` | 16,924 |
| `/Assets/tpp/common_source` | 15,995 |
| `/Assets/tpp/chara` | 13,255 |
| `/Assets/tpp/fova` | 3,025 |

Top-level trees: `tpp` (338,791), `tpptest` (31,237), `ssd` (10,091),
`foxtest` (3,824), `mgo` (2,444), `sh` (1,059), `fox` (652). The `*test` trees
are development leftovers and contain genuinely useful reference assets —
`/Assets/fox/rig/frig/human_finger` is the canonical human rig.

Within a character folder the convention is consistent:

```
/Assets/tpp/chara/<code>/Fox_files/     model + rig assets (.fmdl, .frig, …)
/Assets/tpp/chara/<code>/Pictures/      textures
/Assets/tpp/chara/<code>/Scenes/        assembled scene/part definitions
```

`Fox_files/.fpklp/` holds the packed-list variants of the same assets.

Motion splits the same way:

```
/Assets/tpp/motion/mtar/<group>/<set>          animation archives   (495 paths)
/Assets/tpp/motion/motion_graph/<group>/<set>  motion graph assets  (989 paths)
```

The two trees use the *same set names*. See
[Locomotion Deep Dive](Locomotion-Deep-Dive) for what that implies.
