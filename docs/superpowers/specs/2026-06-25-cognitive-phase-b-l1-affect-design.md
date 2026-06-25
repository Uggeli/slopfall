# Cognitive Phase B / L1 — The Affect Layer (Design)

**Date:** 2026-06-25
**Status:** draft, ready for review.
**Depends on:** **Phase A** (the atoms refactor — `AtomName` + `AtomCatalog`, landed on
`cognitive-phase1-subjective-interpret` @ `207e50976`); the cognitive framework roadmap
(`docs/cognitive_framework_roadmap.md` — **this is its Phase B, rung L1**); the reframed M1 spec
(`2026-06-24-cognitive-phase1-subjective-interpret-design.md` — D1–D3 superseded, D4–D6 are the
surviving core); the 2026-06-23 substrate audit (§2.3). Blast-radius mapped against current
`Assets/Sim` + `Headless`; cites `file:line`.
**Part of:** the cognitive framework. Phase B is **the reading** — making an agent's verdict (threat,
valence) a *perceived* read (tone × disposition × behaviour), not an omniscient type-check. **L1** is
the first rung: capacity-tone × proximity (already in the fear controller) × a fixed-prey disposition,
with the prey-veto, **off the `_creatures` oracle**. L2 (real kinematics) and the species priors
(Phase C) follow.

## The idea

After Phase A, atoms are honest (form, not verdicts) and the catalog carries a **reserved `Tone`
face**. Today an agent still decides *threat* with an omniscient oracle: `SubjectiveSystem.Interpret`
returns `Threat = 1.0` **iff** `creatures.Contains(other)` (`SubjectiveSystem.cs:101`) — a
ground-truth membership check, not a perceived read. L1 replaces that with affect: a creature
**broadcasts honest form atoms** (`Fanged`, `Fast`) carrying aversive **tone** in the catalog, and the
perceiver **reads** threat from those tones — graded, earned, perception-gated. The `_creatures`
oracle leaves the agent's subjective path entirely.

This is low-risk because the whole **downstream** of threat already reads the perceived verdict
(`EntityRead.Threat`); only the *producer inside Interpret* is an oracle (see *Why dropping the oracle
is safe*). And it is the honest home for the reserved Tone face: **Phase A declared the cell; Phase B
fills it.**

> A civilian near a fox reads its `{Fanged, Fast}` → the prey-veto fires on `Fanged` → a graded
> aversive `Threat`+`Valence` → the existing fear/flee/guard machinery responds — with **zero
> `creatures.Contains` in the agent's subjective path.** A civilian near another civilian reads only
> neutral-tone identity atoms → `Threat 0`, exactly as today.

## What L1 is (and is not)

- **The division of labour holds.** `Interpret` answers *"is there a cue, how strong, intrinsically"*
  (capacity from tone); the **fear controller** (`NeedsSystem.FearLevel`/`CompleteThreat`) answers
  *"how loud"* (proximity via `clarity = 1 − d/12`, vigilance floor, arousal spiral); the **planner**
  answers *"fight or flight"*. L1 changes only the first.
- **Proximity stays in the fear controller.** The reframe's literal wording ("Threat = … × proximity")
  would double-count distance — `FearLevel` already multiplies by `clarity`. So L1's `Interpret`
  produces an *intrinsic, proximity-free* capacity; the "engagement" term is the existing `clarity`;
  real kinematic engagement (closing/orientation) is **L2** (and needs a velocity substrate that does
  not exist today — only `Position{X,Y,Z,Yaw}`).
- **Disposition is a constant ("prey") in L1.** Structurally present, no effect yet. Real
  vulnerability (my form vs its harm-capacity), species priors, and the predator/prey asymmetry are
  **Phase C**. Personality already enters downstream via the fear `VigilanceFloor` (HarmAvoidance).

## Decisions (locked)

### B1 — Fill the catalog `Tone` face

1. `AtomCatalog` stops leaving `Tone` neutral-for-all: it populates `AtomEntry.Tone =
   AtomTone(Valence, Arousal)` (both `Fixed`, already declared in Phase A) per atom. Every existing
   atom keeps the neutral default `(0,0)`; only the new form atoms get non-neutral tone.
2. Tones (frozen placeholders — tuned later in one survivable-economy pass, per the roadmap; **not**
   now): `Fanged ≈ (−0.8, 0.9)` (aversive, high-arousal), `Fast ≈ (−0.4, 0.6)` (mildly aversive),
   `Size → (0, 0)` **neutral** (size *modulates* weapon cues in B3 — it is not itself aversive).
3. The catalog stays **float-free** (`AtomTone` is `Fixed`); consumers in `double`-land (Interpret)
   read tone via `.ToDouble()`.

### B2 — Form atoms + per-kind mapping + creature-spawn stamp

1. Three new `AtomName` members: **`Fanged`, `Fast`** (weapon form) and **`Size`** (a graded body
   attribute) — descriptive *form* properties (not verdicts), beside the identity atoms. (Extensible
   later: `Clawed`, `Blood`, …) All are non-identity (`AtomCategory` = a new `Form` category, so
   `IsIdentity` stays false and they don't pollute the recognition `Signature`). **`Size` is graded:**
   its instance **value** carries the magnitude (≈0 tiny … 1 huge); its catalog tone is **neutral** —
   size is a *modulator* the read applies to weapon cues (B3), not an aversive atom itself.
2. A frozen **`CreatureForms`** table keyed by creature kind → form-atom bag. Today there is **one**
   creature kind (generic `Beast`/`EnemyMonster`), so **one row**: `Beast → {Fanged, Fast, Size≈0.5}`.
   This is the per-kind *architecture*; it gains rows — with their own sizes (a future `Squirrel≈0.1`,
   `Bear≈0.9`) — when monster-type variety is introduced (a separate, later feature — see Deferred).
   Lives beside the other frozen catalogs.
3. **Stamp at creature spawn.** `CreatureSystem.TrySpawn` (which already publishes
   `IdentitySetIntent`/`PositionSetIntent`/`VitalsSetIntent`/`CreatureSetIntent`,
   `CreatureSystem.cs:~217-228`) also publishes a `StampAtomIntent` per form atom from `CreatureForms`.
   Creatures finally have a perceivable bag (empty today). Civilians/StaticNPCs are untouched.

### B3 — The affect read: a prey-veto over aversive *cues* (the `Interpret` threat path, off the oracle)

1. **Remove `creatures.Contains(other)` as the threat source** in the canonical
   `SubjectiveSystem.Interpret` (`SubjectiveSystem.cs:101`). (`OddSystem.Interpret`'s identical oracle
   at `:881` is removed *wholesale* by the B5 de-dup, not edited here — and it never fired for
   creatures anyway, since `RegardFieldAt` reads un-sensed *building occupants*, not creatures.)
2. **Threat = the prey-veto = the `max` over independent aversive *cues*.** *One sufficiently aversive
   cue dominates the whole molecule* — whether that cue is "it has huge fangs" or (later) "it's
   charging me." Each cue is computed from perception; `Threat` = the largest. The aggregation is built
   as `max` over a **cue list**, so later rungs drop in *without re-architecting* — a new cue is a new
   entry in the max, **not** a new formula and **not** a multiplier on capacity. The cues, by rung:
   - **Capacity cues (L1):** for each weapon form atom, `cue = aversiveTone × perceivedSize`, where
     `aversiveTone = max(0, −tone.Valence) × tone.Arousal` and `perceivedSize` = the value of the
     creature's `Size` atom (B2). **Size modulates the weapon:** small attenuates toward zero (a
     squirrel's fangs ≈ harmless), large amplifies (a bear's fangs ≈ terrifying); a large creature also
     carries a little raw mass-menace of its own (`+ k × perceivedSize`).
   - **Behaviour cue (L2):** observed aggression — *its own entry in the `max`, independent of
     capacity*, so **behaviour can make a low-capacity thing scary** (a tiny thing charging you is
     frightening though its fangs are not). Deferred to L2 (real kinematics — no velocity substrate
     today); L1's engagement proxy stays the existing **proximity** in the fear controller.
   - **Reputation cue (Phase C/D):** learned "this *kind* hurt me." Deferred.
3. **The veto also forces `Valence` aversive** (the dominant cue's tone valence) — a thing that scares
   you is not also something you feel warmly toward — overriding any learned/dossier blend for that
   read. `Threat` is **graded in [0,1]**, so `FearLevel`'s `CompleteThreat(clarity) × Threat` scales by
   a real perceived capacity instead of a binary flag.
4. **Disposition = constant prey (L1):** a `1.0` multiplier — structurally present, no effect.
   **`Attention` must retain the threat-grabs-attention boost** the old creature-read had
   (`1.0 + |ThreatValence|`): the read sets `Attention = base + Threat`, so a graded threat stays
   salient through the top-K attention cull (otherwise a freshly-perceived threat could be culled from
   the subjective view *before* fear reads it — a regression). `Recognition`/`Trust` follow the
   existing stranger path (familiarity) — harmless, since the threat path consumes only `Threat`, not
   `Trust`. A behavioural test must assert a perceived creature survives the attention cull.
5. Exact weights/clamps (the size factor, mass-menace `k`, normalization) are **frozen placeholders**
   the plan/tuning pins; the **shape** — a `max`-veto over weapon×size capacity cues, off the oracle,
   extensible to behaviour/reputation cues — is locked here.

### B4 — Retire the role-scalar "stereotype stack"

1. **Delete** the role-scalar meanings stack: `MeaningsSystem` (the interaction-event writer),
   `MeaningsRegistry` + `MeaningsData` + `CategoryNode` (`Engine/Units/MeaningsRegistry.cs`,
   `Registries/MeaningsRegistry.cs`), `MeaningsSetIntent`, the static `MeaningsSystem.CategoryValence`,
   `OddSystem.CategoryValence`, and `SubjectiveSystem.BlendValence`. Remove the system from the
   schedule. **This is distinct from the memory-core `MeaningsStore`** (`agentMem … mem.Meanings`,
   `RecognizedValence`) — which **stays** as the sole stranger-valence source.
2. After removal, `Interpret`'s valence is: dossier `rel.Regard` (known individuals) → else the
   learned-store `RecognizedValence(perceivable.Signature(other))` (strangers) → `+
   affects.ValenceToward(self, other)` → `+` beggar-stigma.
3. **Accepted consequence (decided with the user):** the new memory-core store is **empty/seedless**
   today (Phase A dropped `MemorySeeds`; seeds are Phase C, episodic writes Phase D), so generic
   social valence reads **neutral** until Phase C. Dossier-regard, affects, and beggar-stigma still
   color reads of known/acting individuals.

### B5 — De-duplicate `Interpret`

1. **Delete `OddSystem.Interpret`** (`OddSystem.cs:879-913`) and `OddSystem.CategoryValence`
   (`:922-927`); route `RegardFieldAt`'s un-sensed-occupant read through the single static
   `SubjectiveSystem.Interpret`.
2. The "circular dependency" the code comment (`OddSystem.cs:873-874`) warns of is about the async
   *system*; the static `Interpret` *method* is pure over registries. **Plan-time check (B5a):**
   confirm `OddSystem` holds every registry the static `Interpret` needs (notably `agentMem`,
   `perceivable`) and wire any it lacks. If a genuine cycle exists, fall back to a single shared
   static helper both call — there must be exactly one interpretation implementation either way.

### B6 — The two percept bugs (they corrupt exactly what `Interpret` consumes)

1. **Somatic clear-on-zero.** `SomaticPerceptSystem.Stamp` (`SomaticPerceptSystem.cs:32-40`)
   early-returns on `value ≤ 0`, leaving a stale somatic atom (e.g. old `SomaticHunger`) in the bag
   forever. Fix: publish a `ClearAtomIntent` when `value ≤ 0` (`PerceivableRegistry` already consumes
   it, `PerceivableRegistry.cs:35-38`).
2. **Indoor stale-sensed list.** `SenseSystem.Update` (`SenseSystem.cs:42-89`) builds a view only for
   `IsOutAndAbout` agents, so an agent that goes indoors keeps its last street `SensedSetIntent` and
   "sees" people who aren't there. Fix: publish an **empty** `SensedSetIntent` for indoor agents so
   `SensedRegistry` clears.

### B7 — Beggar-stigma kept as-is (flagged)

The `Doing Beg → valence += (Warmth − 0.5) × StigmaScale` special-case (`SubjectiveSystem.cs:132-137`)
**stays** for L1 — it is a working *social* disposition read on an activity, orthogonal to the threat
affect read. Dissolving it into a social-tone × social-disposition mechanism (the roadmap's other
"dissolve the special-cases" goal) needs the social-tone machinery and belongs with **Phase C**.

## Why dropping the oracle is safe (the de-risking finding)

The whole **downstream** of threat already reads the perceived `EntityRead.Threat`; only the *source
inside Interpret* is an oracle. All five consumers are read-only filters / scalar multiplies and need
**no change**:

- `NeedsSystem.FearLevel` — `if (reads[i].Threat ≤ 0) continue;` (`:254`) and `felt =
  CompleteThreat(clarity, floor, prevFear) × reads[i].Threat;` (`:263`). The Fear drive already reads
  the subjective view, owns `CompleteThreat`, and is proximity-gated by `clarity`.
- `OddSystem.NearestThreat` (Flee targeting) — `if (reads[i].Threat ≤ 0) continue;` (`:849`).
- `OddSystem` guard-attack ad injection — `if (e.Threat ≤ 0) continue;` (`:537`).
- `SubjectiveSystem` greet-gate — `if (r.Threat > 0) return;` (`:206`).

Creatures already appear in agents' subjective views (they are added to the sense spatial hash,
`SenseSystem.cs:67-76`, and pass the same distance/LOS filters) — which is *why* monsters cause
fleeing today. L1 changes the **producer** (oracle → tone-read) and stamps form atoms on creatures
(B2). Fear, flee, and guard targeting keep reading `EntityRead.Threat`. **Out of scope (stays
ground-truth, legitimately):** `PlaceDangerSystem` and the metrics' creature-kill attribution
(bookkeeping over ground truth, not the agent's subjective read); the guard's own `NearestCreature`
attack-targeting scan (the attack path, not the fear path — reconciling it is later).

## Architecture / blast radius (file:line)

```
B1 TONE        AtomCatalog: populate AtomEntry.Tone per atom (Fixed valence+arousal)
               new AtomTone rows for Fanged/Fast; expose AtomCatalog.For(a).Tone
B2 FORM        AtomName: + Fanged, + Fast, + Size (new AtomCategory.Form, IsIdentity=false)
               new CreatureForms table: Beast -> {Fanged, Fast, Size≈0.5}
               CreatureSystem.TrySpawn (~:217-228): StampAtomIntent per form atom (Size carries value)
B3 INTERPRET   SubjectiveSystem.Interpret  SubjectiveSystem.cs:93-146  (oracle :101 removed)
               threat = prey-veto (max) over capacity cues = weaponTone × Size; cue-list extensible
B4 RETIRE      DELETE MeaningsSystem / MeaningsRegistry+Data / CategoryNode / MeaningsSetIntent
               DELETE MeaningsSystem.CategoryValence, OddSystem.CategoryValence, BlendValence
               valence = rel.Regard | mem.Meanings.RecognizedValence | + affects | + stigma
B5 DEDUP       DELETE OddSystem.Interpret (:879-913) + CategoryValence (:922-927)
               RegardFieldAt -> static SubjectiveSystem.Interpret  (B5a: registry wiring)
B6 BUGS        SomaticPerceptSystem.Stamp (:32-40): value<=0 -> ClearAtomIntent
               SenseSystem.Update (:42-89): indoor agent -> empty SensedSetIntent
KEPT           beggar-stigma SubjectiveSystem.cs:132-137 (B7); fear stack NeedsSystem.cs:229-274;
               EntityRead struct SubjectiveViewRegistry.cs:12-20; the 5 Threat consumers
UNCHANGED      AtomBag/AtomName storage; proximity/clarity/CompleteThreat; CreatureData
```

## Data flow (a civilian meets a Beast, post-L1)

```
SenseSystem -> civilian senses Beast (in the spatial hash, passes LOS) -> SensedSetIntent
SubjectiveSystem.Interpret(civilian, beast):
   bag(beast) = {Fanged(−0.8/0.9), Fast(−0.4/0.6), Size=0.5}
   capacity cues: Fanged 0.72×0.5=0.36 ; Fast 0.24×0.5=0.12 ; (+ raw size-menace k×0.5)
   prey-veto = max ≈ 0.36  ->  EntityRead { Threat≈0.36, Valence aversive }   (NO creatures.Contains)
   (future Squirrel{Fanged,Size=0.1} -> 0.72×0.1≈0.07 harmless; Bear{Clawed,Fanged,Size=0.9} -> high;
    a tiny thing CHARGING -> high via the L2 behaviour cue, independent of its low capacity)
SubjectiveSetIntent -> SubjectiveViewRegistry
NeedsSystem.FearLevel(civilian): clarity = 1 − d/12 ; felt = CompleteThreat(clarity,floor,arousal)×0.72
   -> Fear rises (proximity- and arousal-gated, exactly as today)
OddSystem: Fear loud -> Flee (NearestThreat reads EntityRead.Threat) -> civilian flees
```

## Validation

- **Prey-veto / capacity read (unit).** Bag `{Fanged, Fast, Size}` → `Threat` graded `> 0` and
  `Valence` aversive; the veto = `max` capacity cue (`Fanged` dominates). **Size modulates:** the same
  weapons with a *small* `Size` value → a much *smaller* `Threat` (the squirrel case → ~harmless), a
  *large* `Size` → a larger `Threat`. A neutral identity-only bag → `Threat == 0`, tone-untouched.
  Assert the aggregation is **`max`-over-cues, not `capacity × behaviour`** — a synthetic extra cue
  raises `Threat` independent of capacity (so a future low-capacity creature can be made scary by
  behaviour).
- **Catalog tone (unit).** `AtomCatalog.For(Fanged).Tone` aversive; existing atoms neutral; `Fanged`
  is `Form`-category and not `IsIdentity` (so it never enters a recognition `Signature`). Extends the
  Phase-A catalog tests.
- **B6 percept bugs (unit).** A somatic atom is cleared when the need hits 0 (a `ClearAtomIntent` is
  published); an indoor agent's sensed-list is empty (an empty `SensedSetIntent` is published).
- **B4 retire (unit + build).** A no-dossier stranger with an empty learned store → **neutral**
  valence (no role-scalar fallback); the build proves `MeaningsSystem`/`MeaningsRegistry` have **zero**
  remaining readers.
- **B5 de-dup (build).** `grep` shows one `Interpret`; `RegardFieldAt` compiles against the canonical
  one.
- **Soak — behaviour-sense, NOT byte-determinism.** Per `[[validate-sim-by-behavior-sense]]`, the
  parallel soak is nondeterministic run-to-run; **do not** gate on reproduction. A short
  Gothway/Gallotale soak must stay **in-regime and believable**: population stable; civilians still
  fear and flee monsters — now via *perception* (Flee / creature-kill counts a plausible ballpark, not
  zero, not exploded); guards still post and attack. Wire a soak metric if useful (e.g. mean `Threat`
  over perceived creatures) to confirm the graded read fires.

## Scope / deferred

- **In:** B1–B7 — fill the catalog tone face + `Fanged`/`Fast`/**`Size`** + `CreatureForms` (1 row) +
  creature-spawn stamp + the **prey-veto capacity read** (weapon×size, off the oracle, built as
  `max`-over-cues) + retire the role-scalar stack + de-dup `Interpret` + the two percept bugs.
- **Deferred (Phase C — innate priors):** real disposition/**vulnerability** — the *observer-relative*
  size/harm comparison (my form vs its harm-capacity), distinct from L1's *absolute* creature-size
  capacity; species priors and the predator/prey asymmetry; the **reputation cue** (learned 'this
  *kind* hurt me'); the **social-valence replacement** (kind-keyed stereotype seeds) for the gap B4
  opens; beggar-stigma dissolution into social-tone.
- **Deferred (L2 — within Phase B):** the **observed-behaviour cue** — real kinematic engagement
  (closing/orientation; needs a velocity substrate that does not exist today) plus observed aggression,
  added as its *own* entry in the prey-veto `max` so **behaviour can make a low-capacity creature
  scary**.
- **Deferred (separate feature):** **monster-type variety** (selecting `MobileTypes` at spawn + per-type
  forms + the balance question); the guard `NearestCreature` attack-scan reconciliation; sense-path
  range/occlusion of creature perception.
- **Deferred (cross-cutting):** engine replay-determinism (the failing same-seed-twice test); tuning
  the tones / fear-completion band beyond "behaves believably in soak."

## Sequencing (for the plan)

One safety constraint drives the order: **stamp creatures with aversive-tone form atoms *before*
flipping `Interpret` off the oracle** — otherwise threat collapses to zero between steps and civilians
stop fearing monsters (a believability break the soak would catch, but better never introduced).

```
B6 (independent percept bugs, smallest)
  -> B1 (catalog tone face) + B2 (Fanged/Fast + CreatureForms + creature-spawn stamp)   // creatures perceivable FIRST
  -> B3 (Interpret threat path: oracle -> tone-read + prey-veto)                          // now safe to drop the oracle
  -> B4 (retire role-scalar stack)
  -> B5 (de-dup Interpret)
  -> soak gate (behaviour-sense)
```

Each step lands as its own committable, tested change; B1+B2 must precede B3; the soak runs after B5
as the integration check.
