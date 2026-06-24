# Cognitive Phase 1 — M1: The Subjective Layer (Design)

**Date:** 2026-06-24
**Status:** approved (2026-06-24), ready for plan.
**Depends on:** Phase 0 (landed on master 2026-06-24, commits `8ada0cbdd`..`d3e104dfa`),
the 2026-06-23 substrate audit (`docs/cognitive_layer_audit.md`), [[cognitive-substrate-audit]],
[[what-is-a-bunny-architecture]], [[atom-model-redesign]].
**Part of:** completing the cognitive model. Phase 1 ("the subjective layer") is decomposed into three
mini-milestones; this spec is **M1**. M2 (drive-biased attention) and M3 (episodic EVENTS + THINGS
full-percept) are out of scope here, each getting its own spec → plan → build cycle.

## The idea

After Phase 0, the ODD planner steers chains and culls by drive — but it steers over **objective**
entity reads. An agent values being near another agent via a town-wide *role stereotype* and decides
something is dangerous via an **omniscient oracle** (`_creatures.Contains`), not via what it perceives.
M1 makes entity interpretation genuinely **subjective and perceptual**: an agent reads another entity
through its own seeded + learned category memory over that entity's perceived **atom-bag** — for both
valence (how do I feel about this person?) and threat (is this a predator?).

### What the audit got right, and what it got wrong (verified 2026-06-24)

The audit said "`Interpret` is a scalar lens that never reads the perceivable atom-bag." That is **only
true of one of the two `Interpret`s.** The real situation:

- `SubjectiveSystem.Interpret` (`SubjectiveSystem.cs:93-146`) is the **canonical production path**. It
  *already* reads `perceivable.Signature(other)` and the agent's private learned `MeaningsStore`, and
  blends the old role-scalar value with the learned one by confidence (`BlendValence`).
- `OddSystem.Interpret` (`OddSystem.cs:879-913`) is a **legacy fallback** (role-scalar only) used for
  un-sensed building occupants in `RegardFieldAt`. *That* is the scalar lens.

So the machinery exists; it is **inert** for two concrete reasons:

1. **The `MeaningsStore` is empty.** F4 (the seed install) was never wired into the live spawn path —
   `MemorySeeds.Install` is called only in tests. So `RecognizedValence` returns nothing and the blend
   always falls back to the role-scalar value (the soak confirmed: `cat/agent=0.00` at spawn).
2. **Even if seeded, the vocabulary would not match.** `MemorySeeds` defines four abstract atoms
   (`Predator=1, Food=2, Water=3, Conspecific=4`), but agents perceive `Kind`/`Role`/`Race` atoms
   (`KindBase=1000+`, `RoleBase=2000+`, `RaceBase=3000+`). The seed prototypes live in a different atom
   space than the signatures perception produces, so `Recognize` (L1 distance) never fires.

M1 fixes both, retires the redundant role-scalar stack, de-duplicates the two `Interpret`s, and moves
**threat** off the omniscient oracle onto perception — which is low-risk because the entire downstream
already consumes the perceived read (see "Why dropping the oracle is safe").

## Decisions (locked)

### D1 — Seed install at spawn, in the real perceivable-atom space (this is F4)

1. `AgentMemoryRegistry.Seed(id)` calls `MemorySeeds.Install(mem.Meanings)` so every agent spawns with
   a non-empty `MeaningsStore`.
2. **Rewrite `MemorySeeds` to seed entity categories in the atom space perception actually produces.**
   The innate **entity** vocabulary is **role-differentiated** (not per-kind, not merely coarse):

   | category   | prototype atom                         | valence | confidence |
   |------------|----------------------------------------|---------|------------|
   | predator   | `PerceivableAtoms.Predator` (see D2)    | −1.0    | 0.9        |
   | conspecific| `RoleBase + ResidentRole.Resident`      | +0.1    | 0.9        |
   | guard      | `RoleBase + ResidentRole.Guard`         |  0.0    | 0.7        |
   | keeper     | `RoleBase + ResidentRole.Keeper`        |  0.0    | 0.7        |

   Everything else → novel/neutral, learned over time.

   **Plan-time verification (D1a):** the `guard`/`keeper` rows assume agents *broadcast* a distinct
   identity atom for those roles. Confirm against `PerceivableRegistry`/the bag-stamping path what
   `Role`/`Kind` atoms civilians actually emit (guards are identified by *employment*, not necessarily a
   `ResidentRole.Guard`). If no distinct atom is emitted for a role, either add a perceivable atom for it
   or fold it into `conspecific` for M1 — do **not** author a seed node that nothing can match. The
   recognizer keys on the agent's `Signature` (identity atoms below `ActivityBase`), so every seed
   prototype atom must live in that range.
3. The old abstract `Food`/`Water` **entity** seeds are **dropped**. Provisioning is a PLACES concern,
   seeded separately ([[agent-memory-seeding-is-general]]); they were scaffolding in the entity
   `MeaningsStore` and matched nothing perception emits.
4. Seed content stays a frozen table beside `Ads`/`DriveDefs` (the existing `MemorySeeds` static).

### D2 — Monsters broadcast a predator atom

1. Introduce **one** `PerceivableAtoms.Predator` class atom (a trait/class atom, not a per-kind id),
   placed in the **identity/signature range** (below `ActivityBase=4000`) so it participates in both
   `Recognize` (over `Signature`) and the prey-veto bag check.
2. Stamp it on every creature's perceivable bag at creature spawn. One class atom + one seed node
   matches **all** monsters by L1 distance — consistent with the role-differentiated (not per-kind)
   choice in D1, and cheap to author/tune.
3. Rationale: the recognizer keys threat off a perceivable property the monster *broadcasts*, so the
   read is earned by perception, not granted by an oracle ([[never-mangle-to-fit-pipe]]).

### D3 — Threat from recognition, not the oracle

1. **Remove `_creatures.Contains(other)` as the threat source** from *both* `Interpret`s. Threat now
   comes from recognizing the predator atom in the perceived bag.
2. **Prey-veto (hard override).** When the bag contains the `Predator` atom, force aversive valence and
   set `EntityRead.Threat`, overriding the learned/dossier blend. The veto *is* the override: a present
   predator atom dominates whatever else the agent might feel.
3. **Fear-completion (interpretation side).** When recognition is *below* the match threshold but the
   nearest category is `predator` (a partial match in a tunable band), still set `EntityRead.Threat`
   (better-safe-than-sorry). This pairs with the drive-side `NeedsSystem.CompleteThreat` that already
   exists — interpretation decides *there is a threat cue*, the drive controller decides *how loud it
   feels* from clarity + arousal.
4. **Scope of the veto/completion:** entity reads only. Place danger (`PlaceDangerSystem`) and the
   metrics' creature-kill attribution legitimately still use `_creatures` — those are bookkeeping over
   ground truth, not the agent's subjective read, and stay as-is.

### D4 — Retire the role-scalar meanings stack entirely

1. **Delete** `MeaningsSystem` (the town-wide fold/decay writer), `MeaningsRegistry`, and
   `MeaningsSetIntent`; remove the system from the schedule.
2. After removal, `Interpret`'s valence is: individual dossier `rel.Regard` (for known individuals) →
   else the learned/seeded category valence over the bag (for strangers) → plus `affects` (acute
   emotion). The old role-scalar term and `BlendValence`'s dependence on it go away.
3. A genuinely novel stranger (no dossier, no recognized category) reads **neutral (0)** and is learned
   over time — which is why D1's seed is a *hard prerequisite*: with the stereotype gone, civilians and
   monsters must be recognizable from tick one or social/threat reads collapse to neutral.

### D5 — De-duplicate Interpret

1. **Delete** `OddSystem.Interpret` and `OddSystem.CategoryValence`.
2. Route `RegardFieldAt`'s un-sensed-occupant reads through the single canonical
   `SubjectiveSystem.Interpret` (it already takes the registries needed: perceivable, agentMem,
   relations, affects, behavior, personality, residency). After this there is exactly one
   interpretation implementation.

### D6 — The two percept bugs (they corrupt exactly what Interpret consumes)

1. **Somatic clear-on-zero.** `SomaticPerceptSystem.Stamp` currently early-returns when `value <= 0`,
   leaving a stale somatic atom (e.g. old `Hunger`) lingering in the bag forever. Fix: publish a
   `ClearAtomIntent` (or equivalent) when `value <= 0` so the atom is actively cleared.
2. **Indoor stale-sensed list.** `SenseSystem` only runs `Sense()` for `IsOutAndAbout` agents, so an
   agent that goes indoors keeps its last street `SensedSetIntent` and "sees" people who aren't there.
   Fix: publish an empty `SensedSetIntent` for indoor agents so the registry clears.

## Why dropping the oracle is safe (the key de-risking finding)

The whole **downstream** of threat is already perception-driven; only the *source inside Interpret* is
an oracle:

- `NeedsSystem.FearLevel` (`NeedsSystem.cs:243-264`, the Fear drive, `LevelSource.DerivedThreat`) reads
  `reads[i].Threat` from the `_subjective` view — **not** `_creatures`. It already owns fear-completion
  (`CompleteThreat`, `:229`) and the arousal spiral.
- `OddSystem.NearestThreat` (`:841-849`, Flee targeting) reads `EntityRead.Threat` from the subjective
  view.
- Creatures already appear in agents' subjective views today (that is *why* monsters cause fleeing in
  the current soak: `Flee`, creature kills).

So M1 changes the **producer** (`Interpret`: oracle → predator-atom recognition) and stamps the
predator atom on creatures (D2). Fear, flee, and guard targeting need **no rewiring** — they keep
reading `EntityRead.Threat`. The guard's own `NearestCreature` attack-targeting scan in
`InjectGuardAds` stays as-is for M1 (it is the attack path, not the fear path; reconciling it with
perceived threat is M2 territory).

## Architecture

```
D1  AgentMemoryRegistry.Seed(id): MemorySeeds.Install(mem.Meanings)
    MemorySeeds: abstract atoms -> real Kind/Role prototypes + Predator class atom
D2  CreatureSpawn: stamp PerceivableAtoms.Predator into the creature's bag
D3  SubjectiveSystem.Interpret(self, other):
       bag = perceivable.Bag(other)
       if bag has Predator:           valence = aversive; Threat = set        // prey-veto
       cat = mem.Meanings.Recognize(perceivable.Signature(other))
       if cat == predator(partial):   Threat = set                            // fear-completion
       baseValence = rel.Regard (if dossier) else cat.Valence (learned/seed)  // NO role-scalar
       valence = baseValence + affects.ValenceToward(self, other)
D4  DELETE MeaningsSystem + MeaningsRegistry + MeaningsSetIntent (off the schedule)
D5  DELETE OddSystem.Interpret + CategoryValence; RegardFieldAt -> SubjectiveSystem.Interpret
D6  SomaticPerceptSystem: value<=0 -> publish ClearAtomIntent
    SenseSystem: indoor agent -> publish empty SensedSetIntent
```

## Data flow (a civilian meets a fox, post-M1)

```
SenseSystem -> civilian senses fox (creature, IsOutAndAbout) -> SensedSetIntent
SubjectiveSystem.Interpret(civilian, fox):
   bag(fox) has PerceivableAtoms.Predator -> prey-veto: valence aversive, Threat set
   -> EntityRead { Threat>0, Valence<0 }
SubjectiveSetIntent -> SubjectiveViewRegistry
NeedsSystem.FearLevel(civilian): reads EntityRead.Threat -> CompleteThreat(clarity,arousal) -> Fear up
OddSystem: Fear loud -> Flee root (NearestThreat reads EntityRead.Threat) -> civilian flees
   (no _creatures.Contains anywhere in the agent's subjective path)
```

## Validation

Each change ships a **behavioural** test where one exists to assert, plus helper units (the Phase-0
discipline: the unit suite must not be the only proof — the audit showed the live path is untested).

- **D1/D2 seed + recognition (unit).** A freshly-seeded `MeaningsStore` recognizes: a bag with the
  `Predator` atom → predator category (aversive); `RoleBase+Resident` → conspecific; `RoleBase+Guard` →
  guard; `RoleBase+Keeper` → keeper. An unseeded novel signature is still novel/None.
- **D3 prey-veto + fear-completion (unit, on the pure parts of Interpret).** Bag with `Predator` →
  `EntityRead.Threat>0` and `Valence<0` regardless of any dossier. A partial predator match (distance
  in the completion band, below the match threshold) → `Threat>0`. A clean conspecific → `Threat==0`.
- **D4 retire role-scalar (unit + build).** A stranger with no dossier and no recognized category →
  neutral valence (no role-scalar fallback). Build proves `MeaningsSystem`/`MeaningsRegistry` have no
  remaining readers.
- **D5 de-dup (build).** `grep` shows one `Interpret`; `RegardFieldAt` compiles against the canonical
  one.
- **D6 bugs (unit).** Somatic atom cleared when the need hits 0; an indoor agent's sensed list is empty.
- **Soak (the risky integration check, ARENA2).** A short Gothway/Gallotale soak: population stable vs
  the M0 baseline; civilians still fear and flee monsters (now via perception — `Flee`/creature-kill
  counts in the same ballpark, not zero, not exploded); guards still post and attack; **`learned%`
  rises and now changes behaviour** (subjective social reads differ from the retired stereotype). Wire
  any new metric needed (e.g. mean |valence| over recognized strangers) into the soak capture.

## Scope / deferred

- **In:** D1–D6 + their tests + the soak check.
- **Deferred (M2 — drive-biased attention):** attention that is need-weighted *and actually consumed*;
  reconciling the guard's `NearestCreature` attack scan with perceived threat; any further sense-path
  work (e.g. range/occlusion of creature perception).
- **Deferred (M3 — episodic memory):** the EVENTS store (allocated, never written); THINGS
  full-percept + arousal.
- **Deferred (cross-cutting):** replay-determinism test; the `FearLevel` compositional-smoothing
  decision (noted FROZEN in `NeedsSystem`); re-establishing the deleted drives/fear unit tests; tuning
  the seed valences/confidences and the fear-completion band beyond "behaves correctly in soak."

## Sequencing (for the plan)

D6 (independent bug fixes, smallest) → D1 (seed install + real-atom vocab) → D2 (predator atom on
creatures) → D3 (threat from recognition + prey-veto + fear-completion) → D4 (retire role-scalar) → D5
(de-dup Interpret). D1+D2 must land before D3/D4 (recognition must be real before the stereotype and
oracle are removed, or reads collapse to neutral). Each lands as its own committable, tested change;
the soak runs after D5 as the integration gate.
