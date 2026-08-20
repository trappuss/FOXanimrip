# Locomotion Deep Dive

How movement is built in MGSV, why the obvious approaches to reading it fail,
and how to recover the authored numbers anyway.

This is the longest page in the handbook because it is the part that took the
most work and is least documented anywhere else. It ends with the complete
measured speed table.

---

## Part I — The shape of the system

### 1.1 What Fox Engine is doing

A Fox Engine character's movement is assembled at runtime from three things:

1. **A library of authored clips** — hundreds of them per character, covering
   every gait, direction, stance, start, stop and turn.
2. **A motion graph** — the state machine choosing which clip plays, deciding
   when to blend, and applying travel.
3. **The rig** — IK and help-bone solves that plant feet and fix joints.

Items 1 and 3 ship as data you can read. Item 2 is the problem.

### 1.2 The motion graph: referenced everywhere, located nowhere

The motion graph asset tree is unmistakably real. The path dictionary holds
**989 entries** under `/Assets/tpp/motion/motion_graph/`, organised exactly like
the animation sets:

```
/Assets/tpp/motion/motion_graph/snak/      123 paths
/Assets/tpp/motion/motion_graph/soldier2/   80
/Assets/tpp/motion/motion_graph/soldier/    71
/Assets/tpp/motion/motion_graph/animal/     70
/Assets/tpp/motion/motion_graph/player2/    62
/Assets/tpp/motion/motion_graph/quiet/      58
```

and the names mirror the `.mtar` sets one-for-one. `TppGzPlayer_layers` exists
as both:

```
/Assets/tpp/motion/motion_graph/snak/TppGzPlayer_layers
/Assets/tpp/motion/mtar/player/TppGzPlayer_layers
```

The `_layers` suffix is itself informative: the graph is layered — a base
locomotion layer with additive layers over it (aim, damage, facial) — which is
consistent with how the clip families are organised. **[inferred]**

**But no file carrying the `.mog` extension code (4752) exists in any of the
three games.** An extraction pass by extension code finds zero in TPP, GZ and
Survive alike. That test is sound rather than vacuous: the identical
extension-code mechanism simultaneously locates 19,138 `.fmdl` and 1,236
`.mtar` in TPP, so it demonstrably works through the FPK containers.

**What that proves, and what it does not.** It proves nothing ships under
extension code 4752. It does **not** prove the graph is absent. The data may
ship under a different extension code, inside a container that is not descended,
or be compiled into the executable.

> **A correction worth recording.** We first claimed the stronger result — "zero
> of ~963,000 files, the graph does not ship" — on the strength of a top-level
> extension histogram. That argument was wrong. At the top level that same
> histogram reports zero `.fmdl` and zero `.mtar`, which plainly exist; they sit
> inside FPK containers and do not carry usable extension bits in that walk. The
> conclusion happened to survive a better test, but the reasoning that produced
> it was invalid, and a reader deserves to know which is which.

The concrete next experiment is in [Open Questions](Open-Questions).

### 1.3 Why this matters less than it sounds

The graph's *logic* is unavailable, but its *edges* are not. The set of
transition clips that exists tells you which state changes the game actually
performs — because a transition that has no clip cannot happen. And each clip's
measured duration, distance and net rotation pin the timing of the edge it
represents. See Part IV.

---

## Part II — The measurement problem

### 2.1 Root travel is approximately zero

The natural way to ask "how fast is this walk?" is to bake the clip with its
root translation kept and measure how far the root moves.

For contact moves — throws, falls, grabs — that works and gives real numbers.
For **walking, running and dashing it gives essentially nothing**:

| clip | frames | root travel |
|------|--------|-------------|
| dash start | 43 | 0.13 m |
| walk loop | 77 | ~0 |
| run loop | 38 | ~0 |

This is not a measurement bug. **The cycles are authored on the spot** and the
engine applies travel parametrically from the motion graph — the same graph that
cannot be located. Root travel therefore can never yield cruise speeds, and any
pipeline built on it silently produces a character that moonwalks.

### 2.2 The insight: the planted foot knows

The speed is still in the curves, in a place that survives in-place authoring.

**While a foot is planted it is stationary in the world.** The body passes over
it. So in the character's own reference frame, the stance foot sweeps *backward*
under the root at exactly the speed the animator intended the character to
travel.

That gives a measurement that works on in-place and travelling clips alike:

```
for each frame pair:
    if both feet-heights are in the lower quartile of this foot's range:   # stance
        v = |Δ(foot − root) projected onto the ground plane| × fps
gait_mps = median(v)
```

Details that matter:

- **Find the feet by name**, `*LFOOT` / `*RFOOT`, not by index — indices differ
  per model.
- **Determine "up" from the data**: the axis along which the root sits furthest
  from the feet. Do not assume Y; a differently-oriented rig then measures
  height sideways and returns nonsense.
- **Use the pelvis as the root reference**, not a synthetic scene root at the
  origin — the latter makes the axis detection depend on a few centimetres of
  toe offset.
- **Reject feet that never lift** (< 2 cm range): that is an idle, and it should
  report 0 rather than noise.
- **Take the median**, not the mean. Contact transitions at the ends of stance
  are noisy.

### 2.3 Validating a measurement you cannot check against ground truth

There is no published number to compare against, so validation has to be
structural. Four independent lines, in increasing strength:

**1 — Synthetic.** Fabricate an in-place walk whose stance feet slide at exactly
1.2 m/s and measure it: **1.2 m/s**. Build the same cycle with a travelling root
and it measures the same. Build an idle: 0, not noise.

**2 — Cross-source agreement.** TPP male and female measure **byte-identical**.
Ground Zeroes and Survive — separately authored games — land within **1–3 %** of
TPP on every shared gait:

| gait | TPP | GZ | Survive |
|------|-----|----|---------|
| walk (stand) | 1.117 | 1.125 | — |
| run (stand) | 3.955 | 3.955 | — |
| dash (sprint) | 6.394 | 6.394 | — |
| walk (weapon ready) | 1.142 | 1.157 | 1.142 |
| run (weapon ready) | 3.570 | 3.630 | 3.570 |

Three separately-shipped games agreeing to three decimal places on a number
neither states anywhere is strong evidence the method reads a real authored
quantity.

**3 — Internal consistency.** The `_rai` dash variant measures *faster* than the
default dash (6.835 vs 6.394), matching the known faster-sprint outfit.

**4 — Replay in an engine.** Import the same clips onto the same rig in Blender,
drive the character's translation at the measured speed, and measure the planted
foot's world-space slide. In-scene speed matched the measured number to
**0.35 % worst case** across four gaits, with travel distance exactly
speed × time, and residual foot slide of 8–14 % of travel — which is the
cycle's own authored variance around its cruise speed, not error.

---

## Part III — The measured data

### 3.1 Cruise speeds

Median stance-foot speed of each flat forward loop, in metres per second.

| gait | stance / state | family | m/s |
|------|----------------|--------|-----|
| **walk** | standing, unarmed | `snapnon_s` | **1.117** |
| walk | weapon ready | `snaprdy_s_fre0` | 1.142 |
| walk | crouched | `snapnon_q` | 0.705 |
| walk | crouched, weapon ready | `snaprdy_q_fre0` | 0.561 |
| walk | prone | `snapasr_p` | 0.578 |
| walk | crawl | `snapasr_c` | 0.354 |
| **jog** | standing | `snapnon_s` | **2.308** |
| **run** | standing, unarmed | `snapnon_s` | **3.955** |
| run | weapon ready | `snaprdy_s_fre0` | 3.570 |
| run | crouched | `snapnon_q` | 2.847 |
| run | crouched, weapon ready | `snaprdy_q_fre0` | 3.007 |
| **dash** | sprint | `snapnon_s` | **6.394** |
| dash | sprint, `_rai` variant | `snapnon_s_rai` | 6.835 |

The ratios are worth noting: walk → jog → run → sprint is roughly
1 : 2.1 : 3.5 : 5.7. Crouching costs about 37 % of walk speed and 28 % of run
speed. Readying a weapon barely changes walking (+2 %) but costs 10 % off a run.

### 3.2 Speed is constant around the circle

Directional loops measure nearly the same speed regardless of heading:

| direction | −135° | −90° | −45° | 0° | 45° | 90° | 135° | 180° |
|-----------|-------|------|------|-----|-----|-----|------|------|
| stand walk | 1.123 | 1.107 | 1.082 | 1.142 | 1.164 | 1.139 | 1.117 | 1.012 |
| stand run | 3.708 | 3.424 | 3.658 | 3.570 | 2.982 | 3.559 | 3.592 | 2.705 |

Walking is flat to within ±4 % except straight backwards (−11 %). Running is
noisier and drops 24 % backwards. **Implication for reconstruction:** one cruise
speed per gait with per-direction clips is faithful — speed does not need to be
a function of heading, except for a backwards penalty.

### 3.3 Turn rates

Turn loops carry their own measured angular velocity. Standing, unarmed:

| gait | turn @90° | @135° | @180° |
|------|-----------|-------|-------|
| walk | ~0 °/s | ~0 | ~0 |
| run | −33.8 °/s | −8.3 | −5.1 |
| dash | +34.4 °/s | +33.8 | +21.1 |

Walk turns measure ~0 because walking turns are handled by *selecting a
directional loop*, not by rotating during one. Run and dash turns genuinely
rotate the body — the faster the gait, the more the turn is an authored arc
rather than a re-facing.

### 3.4 Slopes and stairs are separately authored

There are dedicated graded sets — `snapstr` (stairs) and `snapslp` (slopes), at
15° and 25°, up and down — with their own speeds:

| set | gait | m/s |
|-----|------|-----|
| `snapstr_s_25_u` (25° up) | walk | 0.356 |
| `snapstr_s_15_u` (15° up) | walk | 0.869 |
| `snapstr_s_15_d` (15° down) | walk | 0.600 |
| `snapslp_s_up` | run | 1.646 |
| `snapslp_s_dwn` | run | 1.881 |

**This is a trap for automated selection.** A graded set shares the verb and
phase tokens of the flat one, so a naive "find the walk loop" picks
`snapstr_s_25_wk_u_lp_l` at 0.356 m/s instead of flat walking at 1.117 — a
three-fold error that still looks like a plausible walk on screen. Score
candidates by family, preferring the plain `snapnon`/`snaprdy` sets.

### 3.5 Scale of the data

The full grouped table (`cruise-table.tsv`) contains **1,276 groups** spanning
9,676 measured clips: every family × stance × verb × phase × angle combination
in the player sets across all three games.

---

## Part IV — The clip inventory as a state machine

### 4.1 The grammar

```
snapnon_s_wk_st_l45_l
└──┬──┘ │  │  │   │  └── lead foot
   │    │  │  │   └───── angle, signed degrees
   │    │  │  └───────── phase: st start · lp loop · tn turn · ed stop
   │    │  └──────────── verb:  wk · rn · jg · dh
   │    └─────────────── stance: s stand · q crouch · p prone · c crawl
   └──────────────────── family: weapon and context state
```

### 4.2 Reading it as edges

Each phase is a different kind of graph edge:

- **`lp` loop** — a *state*. One per direction per gait per stance.
- **`st` start** — an edge from idle into a gait, parameterised by the angle you
  are turning through and which foot leads.
- **`ed` stop** — an edge from a gait back to idle, per angle.
- **`tn` turn** — an edge between two directional loops of the same gait.

A complete grid for one family and verb is 8 directions × {start, loop, stop} +
16 turns. Our grid detector treats a family+verb as complete at
`st ≥ 8, lp ≥ 8, tn ≥ 16, ed ≥ 8`. The Phantom Pain player has **four complete
grids**, 290 clips with no gaps.

Because a transition the engine can perform *must* have a clip, enumerating the
clips enumerates the transitions. The graph's structure is recoverable even
though its code is not. What is **not** recoverable this way: the blend
durations, the conditions under which the engine chooses one edge over another,
and any parametric blending between clips. **[open]**

### 4.3 The lead-foot problem

Starts and turns specify which foot leads (`_l` / `_r`). A faithful
implementation must therefore track the phase of the current loop to pick the
correct start or turn variant — choosing wrongly produces a visible stutter as
the character switches support foot mid-step. The information is all in the clip
names; the logic that consumed it is in the missing graph.

---

## Part V — Reconstruction notes

If you are rebuilding this movement system, in order of what matters:

1. **Drive translation from the measured stance-foot speed**, not from root
   travel. Zero foot-slide then holds by construction at any scale, because the
   speed came from the feet in the first place.
2. **Play at 59.94 fps.** Not 60.
3. **Solve IK.** Without it feet do not plant and the whole premise collapses.
4. **Pick loops by family score**, so a stair set never stands in for flat
   ground (§3.4).
5. **Blend on local transforms** — lerp positions, slerp rotations, per bone.
   Blending world matrices bends limbs through the body.
6. **Measure the model's forward axis** from the walk cycle's foot sweep rather
   than assuming one; it is the same estimator as §2.2, negated.
7. **Stand the character on the ground** by offsetting the foot's bind height —
   the skeleton's origin is the pelvis (§ Models and Skeletons).

A validation harness built this way in Blender reproduced the authored speeds to
0.35 %. A standalone bench was then built and later scrapped; the measurements
outlived it, which is why they are written down here.

---

## Part VI — Reproducing every number on this page

```
test-measure-locomotion.bat     measures every player clip in all three games,
                                writes locomotion-params.tsv per source, then
                                cruise-table.tsv overall
test-rip-loco-loops.bat         rips the gait loops against the real player
                                models with a correctly solved rig
```

Per-clip columns: `frames, fps, distance_m, speed_mps, netYaw_deg,
turnRate_dps, matchedBones, gait_mps, gaitSamples`.

`gait_mps` is the number this page is about. `gaitSamples` is how many stance
frame-pairs it rests on — under ~10 and the value is not trustworthy; the
forward loops typically give 30–140.
