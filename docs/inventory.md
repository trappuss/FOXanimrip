# Every model, texture and customisation option

<!-- SPDX-License-Identifier: MIT -->

```bat
foxanimrip-cli --game tpp --inventory C:\rips\_inventory
```

Writes four files: `models.tsv`, `textures.tsv`, `variations.tsv` and
`rip-all-models.bat`.

## First, a thing that trips people up

**`.mtar` files have nothing to do with models.** They are animation archives —
containers of `.gani` clips, nothing else. `player2_resident.mtar` and
`mgoplayer_resident.mtar` hold motion and no geometry, no textures and no
customisation data. If you are hunting model assets, they are the wrong tree.

## Why an "option" is not a file

Fox Engine does not store a character as a finished thing. A model is a mesh with
*named mesh groups*, and a **form variation** — a `.fv2` file — then does some
combination of:

| | |
| --- | --- |
| hide / show mesh groups | how one `.fmdl` serves many appearances |
| swap a texture | a named material slot points at a different texture file |
| set a material parameter | a shader value (four floats), no texture involved |
| attach a model | a sub-model bolted to a bone or a connection point — hair, headgear, gear |
| attach a `.sim` | physics for something that should swing |

So one model file plus a folder of `.fv2` files is how a handful of assets become
hundreds of looks. A customisation option is an **instruction**, not an asset you
can rip on its own — which is why the inventory reads the instructions out
instead of pretending there is a file per option.

## Is a skin tone a texture or not?

**Either, and `variations.tsv` tells you which.** That is the whole reason the
two are separate rows:

- a **`textureSwap`** row means the option genuinely points at a different
  texture file — rip it, it exists.
- a **`materialParameter`** row means it only changes a shader value — there is
  no texture to rip, and reproducing it means applying the same numbers.

Both mechanisms are used in MGSV, and which one a given option uses is a property
of that option, not of the game. Grep `variations.tsv` for the model you care
about and read the `kind` column.

## The tables

**`models.tsv`** — `model`, `bones`, `meshes`, `materials`, `meshGroups`,
`archive`, `layer`, `path`.

`meshGroups` matters more than it looks: a high count usually means the model is
built to be varied, since mesh groups are what variations switch on and off.
`layer` is the patch layer — where a model exists in more than one archive, the
newest copy is the one listed, because that is the one the game loads.

**`textures.tsv`** — `model`, `material`, `role`, `texture`, `path`. One row per
texture reference, with `role` being base / normal / spec and so on.

**`variations.tsv`** — `variation`, `kind`, `detail`, `value`, `archive`, `path`.
One row per instruction. `kind` is one of `hideMeshGroup`, `showMeshGroup`,
`textureSwap`, `materialParameter`, `attachModel` or `file`.

The `file` rows are the variation's external file list; texture swaps and
attachments refer to entries in it by index, so that is how a swap gets traced to
an actual file.

**`rip-all-models.bat`** — exports every model in `models.tsv`, in batches of 25.
Batched because starting a process costs more than ripping one character. Set
`OUT` at the top before running it, and check your free space first: this is
every character asset in the game.

## Narrowing it

```bat
:: just the player models
foxanimrip-cli --game tpp --inventory C:\rips\_inv-player --model-filter skl0

:: everything, not only characters
foxanimrip-cli --game tpp --inventory C:\rips\_inv-all --all-models
```

Without a filter this reads every character model in the game, which on The
Phantom Pain is thousands of files. It is not fast. It is also the only way to
get a complete answer, so run it once, keep the TSVs, and filter them afterwards
in a spreadsheet rather than re-running.

## Two honest limits

**Unresolved names.** The format identifies everything by hash, and the game's
own dictionaries do not cover every hash. Anything unresolved comes out as hex
rather than a name. Those rows are real — the entry exists and does something —
they are just unnamed. The summary line says how many.

**Indexing changed, so both games re-index once.** `.fv2` files were not
collected before, and the archive fingerprint cannot notice that: the game has
not changed, only what is being looked for in it. The index therefore carries a
schema number, and an index written before `.fv2` was collected is rescanned
rather than reused — otherwise it would load, report itself complete, and show no
variations at all.
