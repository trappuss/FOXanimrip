# Finding a base model for a whole animation set

<!-- SPDX-License-Identifier: MIT -->

The usual way round is: pick a character, see what animates it. That is the
wrong shape for a common job — *"I want every locomotion animation for the
player character, so which model do I load?"* — and this is how to do that one.

The thing that makes it answerable: **Fox Engine binds animation to skeletons,
not to models.** An `.mtar` names the bones it drives, and any model with those
bones can play it. So "which base model has all these animations" is a real
question with a checkable answer, not a matter of taste.

## Why FoxBrowser shows nothing for a Phantom Pain model

Its *Play animation* dialog says **"No master animation fits this model's
skeleton"** for `sna2_main0_def` and most other TPP characters. That is not a
lie, and it is not your setup. An `.mtar` only carries a bone-hash table when
its header sets `HAS_SKEL_LIST`, and a great many Phantom Pain archives do not
set it. With no table there is nothing to compare against, so the check finds no
match and reports none.

`foxanimrip` falls back to decoding a handful of clips and reading the bone
hashes off their tracks, which is why it finds sets FoxBrowser cannot.
`--list-sets` prints a `skel-list` column so you can see which archives are in
that state.

## Where the player animations actually live

| | |
| --- | --- |
| TPP player | `Assets/tpp/motion/mtar/player2/player2_resident.mtar`, inside `player2_resident_motion.fpk` in `chunk0.dat` |
| TPP player, online | `Assets/tpp/level_asset/chara/player/game_object/player2_online_motion.fox2` names further sets |
| MGO avatars | `mgoplayer_resident.mtar`, inside `Assets/mgo/pack/player/motion/mgo_player_resident_motion.fpk` |
| Ground Zeroes player | `TppGzPlayer_layers` |

`player2_resident.mtar` is the one holding hundreds of the player's default
animations — running, CQC, carrying, stairs — so for TPP locomotion it is the
archive to start from.

> **MGO needs a re-index.** Until 1.5.0 the Phantom Pain profile only swept
> `master\` and the game root, so the whole `mgo\` tree was never looked at.
> Version 1.5.0 sweeps it, which changes the archive fingerprint and forces one
> fresh index. It is resumable, so an interrupted scan picks up.

## The base models

For the player character these are the ones to use:

| | |
| --- | --- |
| Male player skeleton | `skl0_main0_def` |
| Female player skeleton | `skl0_main0_def_f` |

`skl0_*` is the base mesh and skeleton the player animations are authored
against. A character body like `sna2_main0_def` or `dlf0_main0_def_f` is a
different skeleton and will not necessarily take the player archives cleanly —
which is exactly what FoxBrowser's *no master animation fits* message is
reporting when you select one.

## "It is not in the list"

If an archive you know exists does not appear under the animation sets, ask the
tool which of the four possible reasons it is:

```bat
foxanimrip-cli --game tpp --character dlf0_main0_def_f --why-mtar player2_resident
```

It prints whether the archive is indexed, whether it can be read, whether it
carries a skeleton list, and how many of the character's bones it matches
against the `--min-match` threshold. Those four causes need four different
responses, and from the outside they look identical.

In the window, tick **Show every set** in step 3. Sets that do not fit are then
listed with the reason rather than hidden, and you can tick one anyway — ticking
a set by hand overrides the fit check, since it is a decision rather than a
guess.

## The recipe

Everything below is `foxanimrip-cli.exe`; the windowed `foxanimrip.exe` takes
the same arguments.

### 1. Find the archive

```bat
foxanimrip-cli --game tpp --list-sets player
foxanimrip-cli --game tpp --list-sets mgo
```

Columns are `clips`, `bones`, `skel-list`, `name`, `archive`, `path`. The
interesting ones are large and have a high bone count.

### 2. See what is in it

```bat
foxanimrip-cli --game tpp --list-clips player2_resident --locomotion
foxanimrip-cli --game tpp --list-clips player2_resident > all-clips.txt
```

Run it both ways. `--locomotion` is a list of name fragments — `wal`, `run`,
`dsh`, `crc`, `trn`, `idl` and friends — not a flag stored in the game files,
because there is no such flag. Compare the filtered list against the full one
and extend it with `--filter-any` when it misses something:

```bat
foxanimrip-cli --game tpp --list-clips player2_resident --filter-any wal,run,dsh,cwl,trn
```

### 3. Find the base model

```bat
foxanimrip-cli --game tpp --for-mtar player2_resident --model-filter sna
```

Ranked by how much of the *animation's* skeleton each model has — the right way
round, because a model missing a third of the bones an archive drives will play a
third of every clip wrong, however many spare bones of its own it has. Ties go to
the leaner skeleton, which is the one built for these animations rather than a
superset that happens to contain them.

Drop `--model-filter` to search every character model. That is thousands of
models on Phantom Pain and takes a while, so narrow it when you can.

**Read the `coverage` column, not just the order.** 100% means that model has
every bone the archive drives — that is your base model. If the best on offer is
80%, no model in the game fully fits the archive and you should expect parts of
each clip to do nothing.

### 3b. Let the tool find the movement sets

```bat
foxanimrip-cli --game tpp --list-grids player2_resident
```

This is the exact version of `--locomotion`. It groups clips by verb, phase,
angle and lead foot, and prints the families that close into a complete
8-direction graph:

```
clips  st  lp  tn  ed  complete  angles                        family
65      8   9  32  16  yes       -135 -90 -45 0 45 90 135 180  snaprdy_q_fre0_wk
65      8   9  32  16  yes       ...                           snaprdy_q_fre0_rn
64      8   8  32  16  yes       ...                           snaprdy_s_fre0_wk
64      8   8  32  16  yes       ...                           snaprdy_s_fre0_rn
32     20   0  12   0  no        ...                           snapnon_q_wk
```

Four complete grids — walk and run, standing and crouched — and a few partial
families that use a different loop naming. A grid that does not close says so
rather than being quietly exported half-finished.

### 4. Export

```bat
foxanimrip-cli --game tpp --character <the model you chose> ^
               --mtar player2_resident --grid ^
               --export-model --pack 50 --dedupe ^
               --out C:\rips\tpp-player
```

`--grid` exports exactly the detected locomotion grids. Use `--locomotion`
instead if you want the broader name-fragment sweep, or neither for everything.

Add `--root-motion` if the character should travel rather than move on the spot.
Off is right for a Blender Action library — a walk cycle that wanders is a
nuisance to retarget — and on is right if you want real displacement, or want to
know the speed each clip was authored for.

`--export-model` rips the character alongside the clips, in the layout the
Blender add-on expects. `--pack 50` puts fifty clips in each FBX as separate
takes, which cuts Blender's import time by roughly that factor. `--dedupe` drops
clips whose baked motion matches one already written — Fox Engine ships a lot of
near-identical variants.

Do the same with `--game gz` for Ground Zeroes, into a different folder. The two
games' skeletons are close but not identical, and mixing the exports makes it
impossible to tell later which is which.

### 5. Check before you commit

Open the window, pick the character, tick the set, press **Preview…** and hold
**↓**. A minute of that will tell you more than an hour in Blender, and it poses
from the game's own solve, so anything wrong there is wrong in the source rather
than in the export.

Also glance at the rig line in the log: it says how many of the skeleton's bones
the chosen rig covers. Below about 90% you are likely on a foreign rig, and
`--list-rigs` will show what else was available.

## Male and female

MGO lets you play as either, and they are separate models with separate
skeletons — which means separate base models, and possibly separate archives.
Run step 1 with `--list-sets mgo` and step 3 twice, once per avatar model. If
the two avatars turn out to share a skeleton, the same archive covers both and
`--for-mtar` will rank them adjacently with the same coverage; that is worth
knowing either way before you export twice.

## What this does not solve

Nothing in the game files marks a clip as "locomotion", so `--locomotion` is a
heuristic over names and will both miss clips and include ones you did not want.
Being slightly too broad is the better failure — an extra clip costs a file, a
missing one costs a re-run — but if you need an exact set, dump the full clip
list and pick from it.
