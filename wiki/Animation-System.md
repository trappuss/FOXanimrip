# The Animation System

## 1. MTAR — animation archives

`.mtar` (extension code 3296) is a container of animation clips. TPP holds
**1,236** of them, Survive 542, GZ 19. They live at
`/Assets/tpp/motion/mtar/<group>/<set>` — 495 known paths, grouped by character
(`player2`, `soldier2`, `avm`, `horse2`, `quiet`, `buddydog2`, `heli`, …).

Sets are large and thematic. The Phantom Pain player alone:

```
player2_resident      1,245 clips   the main movement/action set
player2_online          159         MGO-specific
player2_horse           128
player2_cqc                         close quarters combat
player2_jump   player2_ladder   player2_pipe   player2_cbox
player2_heli   player2_vehicle  player2_carry  player2_elude
player2_*_facial                    facial sets, several per character
player2_camera / player2_camera_anim
```

Note `player2_resident_ps3` and `player2_cqc_ps3` — last-gen variants shipped
alongside. They are near-duplicates; a bulk export that does not deduplicate
will produce two of many clips.

`mgoplayer_resident` (1,592 clips as measured) is MGO's parallel set and shares
the same rig, so it drives the same models.

## 2. GANI — a clip

`.gani` (8074) is one animation clip. Exactly **one** ships loose in TPP
(`sna0eye_blink_nomal.gani`); every other clip is inside a `.mtar`.

A GANI holds per-track keyframes addressed by **bone name hash**, not by index —
which is what allows one clip to drive several different skeletons. Resolution
is: hash the model's bone names, match against the clip's track hashes, and
count how many landed. That count is the binding quality (see below).

**Frame rate is 59.94 fps.** Every measured clip in all three games is authored
at 59.94, not 60 — the NTSC rate. Play a clip at 60 and it drifts by one frame
every 16.7 seconds, which is enough to break a loop's phase over time. In
Blender that means `fps = 60, fps_base = 1.001`.

Clip lengths in the player set run from ~15 frames (a snap transition) to 540
(a long context idle).

## 3. Binding a clip to a model

The pipeline, in order:

1. **Resolve tracks to bones.** Match the clip's track hashes against the
   model's bone-name hashes. The result is a *matched bone count*.
2. **Reject weak matches.** A minimum threshold is necessary — but see the
   warning below.
3. **Solve bone drives** from the `.frig`.
4. **Solve IK jobs** — the legs above all. Without IK the feet do not plant and
   every measurement derived from foot contact is wrong.
5. **Solve help-bone operators** from the `.frdv`.
6. Only now is the pose complete.

### Matched-bone count is the quality signal

For a player model against a player set, expect **44 of 53** rig bones, or
similar. Numbers to interpret:

| matched | meaning |
|---------|---------|
| 44–53 | properly bound |
| ~20 | partial — probably a different character class |
| 6–8 | bound to a stub skeleton; legs will not move |

**A threshold of 8 is far too low.** Generic core bones (waist, spine, chest)
appear in nearly every humanoid skeleton including props, so a 15-bone prop
skeleton clears 8 against a full player set. Rank candidates by matched count;
never take the first over a threshold. This exact bug put 209 TPP animation
sets — the player's included — onto a 15-bone stand-in whose exports drive six
bones and animate no legs.

## 4. Root motion is optional, and usually absent

Fox's own export path bakes clips **in place** — the root's translation is
discarded. That is the right default for building an animation library (a walk
cycle that wanders off is hard to retarget), but it destroys the one number that
says how fast the clip was authored to move.

Keeping the root's travel is a single flag in the bake. Even then, most
locomotion clips travel almost nothing, because they are authored in place by
design — see [Locomotion Deep Dive](Locomotion-Deep-Dive), which is entirely
about recovering the speed anyway.

## 5. Clip naming grammar

Movement clips are named systematically, and the grammar is the most valuable
undocumented thing in the animation data:

```
snapnon_s_wk_st_l45_l
└──┬──┘ │  │  │   │  └── lead foot (l / r)
   │    │  │  │   └───── angle in degrees, signed
   │    │  │  └───────── phase:  st start · lp loop · tn turn · ed stop
   │    │  └──────────── verb:   wk walk · rn run · jg jog · dh dash
   │    └─────────────── stance: s stand · q crouch · p prone · c crawl
   └──────────────────── family: weapon/context state
```

Family prefixes seen on the player:

| prefix | meaning |
|--------|---------|
| `snapnon` | no weapon |
| `snaprdy` | weapon ready |
| `snapasr` | assault rifle |
| `snapcqc` | close quarters |
| `snapstr` | stairs (graded) |
| `snapslp` | slope (graded) |
| `snapbeh` | behind cover |
| `snapdam` | damaged |
| `eneXXX` | enemy/NPC equivalents |

Trailing modifiers include `_rev` (reverse variant), `_vr1` (variant), `_dw`
(down), `_u`/`_d` (up/down grade), and blend-angle tokens like `blc225`.

Two corrections we had to make while reading it:

- **`dh` (dash) is a verb** alongside wk/rn/jg — `snapnon_s_dh_st_l0_l` is real.
- Loops are `_lp_`, and a filter matching the words `run`/`dash` rather than the
  tokens `_rn_`/`_dh_` **silently misses the most important clips in the game**.

## 6. Other animation-adjacent formats

| ext | code | what |
|-----|------|------|
| `.fsm` | 3131 | cutscene/scene state data (239 in TPP, 164 Survive, 82 GZ) — **not** locomotion |
| `.lani` | 783 | UI/logic animation **[inferred]** |
| `.sani` | 6407 | scene animation **[inferred]** |
| `.gskl` | 71 | skeleton **[inferred from name]** |
| `.mog` | 4752 | motion graph — see [Open Questions](Open-Questions) |
| `.mas` | 8069 | — none found in any game |
| `.fsml` | 7415 | — none found in any game |
