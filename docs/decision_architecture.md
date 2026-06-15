# Decision architecture — senses → ads → choose, with execution split out

*Planning artifact for E0b. Companion to [`behavior.md`](behavior.md) (the Atoms-convergence
roadmap) and [`economy.md`](economy.md) (the economy loop the new actions plug into).*

## 0. The two problems with `OddSystem` today

1. **It's special cases all the way down.** `Decide()` hardcodes eight candidate blocks
   (Idle/Wander/Sleep/EatHome/Work/EatTavern/Socialize/Visit), each with bespoke gating
   (weather, hours, traits, distance, occupancy, prepotency). Every new action = another block.
   The world advertises nothing — the agent is omniscient (`EnsureTaverns`/`EnsureLandmarks`
   scan *all* buildings regardless of whether the agent could know they exist).
2. **Execution is smuggled into the decider.** `OddSystem.ProcessEvents` flips Moving→Doing on
   arrival, turns greetings into `Chat`, and turns ask-journeys into `SeekHelp` movement. That's
   *carrying out* intents, not *choosing* them.

The target replaces both with one clean pipeline and a clean split.

## 1. The pipeline

```
raw senses ─► percepts ─┐                         (SenseSystem)
 (nearby buildings,     ├─► ADS ─► cull ─► score ─► argmax ─► Intent
  persons, self)        │   gather  drives  uniform           (DeliberationSystem:
memories ───────────────┘          (CAN×WANT) modulators       pure, no special cases)
 (known places)                                                       │
interrupts ─ salient percepts short-circuit ─────────────────► ExecutionSystem
                                                               carries out the Intent + interrupts;
                                                               sole writer of BehaviorRegistry
```

`Decide()` becomes, in full: **gather ads → cull by active drives → score with universal
modulators → argmax → emit Intent.** Branch-free. No `if isKeeper`, no per-tavern loop.

## 2. Systems after the refactor (and single-writer map)

| System | Was | Owns / writes | Does |
|--------|-----|---------------|------|
| **SenseSystem** | PerceptionSystem | PlaceMemory (adds learned places); emits percept/interrupt events | senses nearby buildings + persons; learns places; fires greet/dislike interrupts |
| **DeliberationSystem** | OddSystem.Decide | Intent (new) | gather ads → cull → score → argmax. *Reads* Behavior (incumbent), Needs, Personality, PlaceMemory, percepts. No execution. |
| **ExecutionSystem** | OddSystem.ProcessEvents | **BehaviorRegistry** (now sole writer) | applies Intent + interrupts; Moving→Doing; runs the Doing countdown; completion → re-decide; picks execution-detail targets (wander offset, landmark choice) |
| **MovementSystem** | unchanged | civilian Position | walks toward the target; emits ArrivedAtTargetEvent |

The arrival phase-flip moves from OddSystem to ExecutionSystem, keeping Behavior single-writer.
Deliberation never writes Behavior — it emits an `Intent`; Execution reifies it.

## 3. Data shapes

- **`Ad`** = `{ verb, where (building/person/-1 self), x/z, Spec }` — one opportunity. `Spec` is the
  existing `ActivityCatalog.Spec` (duration + served-Δ + base utility).
- **`AffordanceCatalog`** — what advertises what:
  - *Public* (any agent who knows/senses it): Tavern→{EatTavern,Socialize}; Temple/Guild/Bank/
    Store/Library/Palace→{Visit}.
  - *Agent-relative*: **Home→{Sleep,EatHome,shelter} to its residents**; **Workplace→{Work} to its
    employee** (← the employment link the economy loop plugs into — keepers now, residents in E1,
    *no new code*); **Person→{Chat,Ask}**.
  - *Innate*: {Idle, Wander} always available (Object-Zero floor).
- **`DriveDef`** per need axis: `{ weight, satisfaction-model (Deplete|Derived), prepotency }` —
  formalizes today's scattered `Weights`/`DriftPerHour`/`PrepotencyGate` constants into authored data.
- **`PlaceMemoryRegistry`** (new) — `entity → known building indices`. The recall half of "senses +
  memories → ads".

**The ~6 universal modulators** (applied to *every* ad in scoring, replacing the per-candidate hacks):
distance · time-window · outdoor×weather · crowd/liveliness · holiday · prepotency-cull.

## 4. Sensing & memory model

How NPC sensing works in stock DFU, for reference: **enemies** have a real model (`EnemySenses` —
sight ~102 m / FOV 180° / hearing 25 m, all `Physics.Raycast`, plus last-known memory, target
*prediction/leading*, and stealth/noise detection) but it's combat-target-centric (senses one
target) and physics-bound; **civilians** (`MobilePersonNPC`/`PopulationManager`) sense *nothing* —
ephemeral wander props spawned around the player. So there's nothing to port for ambient civilian
perception, and the raycast model can't run headless. We build our own, and four decisions shape it:

- **One agent kind.** No enemy-vs-NPC split — there are only **agents**. Perception/decision/action
  are kind-agnostic; "wolf" vs "baker" is *data* (which drives/affordances/stats an agent has),
  never a separate code path. When creatures enter the sim they use these same systems.
- **Perception is pure.** Senses report what's perceived right now — nearby agents, salient state —
  and nothing more. Target *prediction/leading* (useful, from `EnemySenses`) is an **action/activity**
  concern (a future Pursue/Attack action), not a sense; it does not live here.
- **Memory of places is full-town for residents.** A town's residents know its layout — they live
  here. So every town agent is seeded at spawn with the **whole building list** in PlaceMemory
  (`TownLoader.SeedTownKnowledge`); a resident is never blind to the tavern down the street. This
  *deletes* the discovery/blindness risk for residents. Discovery (other towns, the player, wandering
  creatures) is a later concern, for agents *without* this grant. No decay in v1.
- **LOS without raycasts.** Occlusion is computed by **walking the grid tiles** along the ray over
  `TownGridData` (cost grid = walls): march tile to tile, cancel the moment one blocks. Deterministic,
  headless, testable — and the Bridge can substitute real engine raycasts when running in Unity.

So SenseSystem's job is **live perception** (who/what is near me now → social percepts & interrupts,
with grid-tile LOS), *not* learning where places are — that's already in memory. Ads at decision time
come from `PlaceMemory (full town, distance-weighted) ∪ sensed-persons ∪ innate`.

## 5. How every special case dissolves

| Today in `Decide()` | Becomes |
|---|---|
| `if isKeeper && hour 8–18 && !holiday` Work | Workplace advertises Work **to its employee**; hours/holiday = ad gating data |
| per-tavern loop + distance + liveliness + social gate | Tavern ads; distance/crowd = universal modulators; sociability = social-drive weight |
| Sleep night-gate + circadian; EatHome night-damp | Home ads with `night` time-window; circadian = night-active energy baseline |
| Wander hash-offset + restlessness + night | Wander ad; restlessness = its drive weight; **offset is execution** |
| Visit prepotency-cull + daylight + rotation | Landmark growth-ads; prepotency universal; **rotation is execution** |
| Idle "wet → shelter at home" | Weather **culls outdoor ads** → best remaining is a home ad → shelter *emerges* |
| Sticky / hysteresis | uniform incumbent bonus in scoring (reads current Behavior) |
| ProcessEvents arrivals/greeting/askJourney | **all move to ExecutionSystem** |

## 6. Migration order (each step builds + the suite stays green where it should)

1. **Scaffolding** — `Ad`, `AffordanceCatalog`, `DriveDef`, `PlaceMemoryRegistry`, building spatial
   index. Additive, behavior-neutral.
2. **SenseSystem** — sense buildings, learn into PlaceMemory, seed home/workplace/home-block.
   Additive (populated, not yet consulted).
3. **ExecutionSystem** — extract execution from OddSystem; becomes sole Behavior writer; Deliberation
   emits Intent. Behavior-preserving step.
4. **DeliberationSystem** — the ad-pipeline rewrite (gather/cull/score/argmax). The big behavioral
   change. Verified against the behavioral expectations (§7), re-tuned via the soak.
5. **Re-tune + clean up** — delete dead special-case code; confirm the soak's behavioral assertions.

## 7. Testing — behavioral, against the running sim  *(methodology, not just E0b)*

Mechanism unit tests are wrong for an emergent sim: they pin *how* the machine computes (exact scores,
exact coin deltas), which E0b changes wholesale, while the town should stay recognizably alive. Green
mechanism tests give false confidence — they prove the numbers didn't move, not that the town behaves.
We test *what the town does*, in four tiers:

1. **Invariants over a running town** (tuning-independent): coin conserves, no NaN, liveness, no
   deadlock, memory bounded, population stable. The `--soak` verdict already checks these — promote it
   to a first-class test.
2. **Behavioral expectations** (emergent, asserted statistically with generous margins): most asleep at
   night, tavern occupancy peaks in the evening, keepers favor work in work-hours, storms thin the
   streets, friendships form without saturating to all-pairs.
3. **Scenario tests** (a situation set up in the *full* sim, assert the outcome): hungry agent + known
   tavern → eats; two friends crossing → greet; pauper + liked keeper → alms.
4. **A thin layer of real unit tests** — only deterministic algorithms where exact values *are* the
   contract: pathfinding, clock transition-crossing, BSA parsing, Gini/conservation math.

**Evaluative, not binary.** An off-band metric is not automatically a failure — the agent may have
done something we didn't predict that its motivations explain perfectly. So tiers 2–3 are *soft*:
printed and flagged for judgement, with the **motivations of the surprising agents captured**
(`TownCensus.Explain` — drives, current act, coin; the ad-scores join once DeliberationSystem retains
them). Only true invariants (tier 1) and clear-bug floors fail the build. *Proof this matters:* the
first harness run flagged "47% asleep at 1pm" — the captured motivations showed they were all poor
jobless Residents whose only servable need was residual tiredness, so they slept. Sensible, not a bug,
and a pointer straight at E1 (resident jobs) — exactly what a behavioral suite should surface.

**Shared measurement layer:** the soak's `Capture()` already computes everything tiers 1–2 need.
Extract it into a queryable `Town` fixture so tests read `town.RunDays(3); Assert(town.AsleepFractionAt(3) > 0.8)`,
and the soak and the tests measure identically. In-process, deterministic by seed, fast (no network).
The **live server + spectator** (`--serve` + web/TUI) stays the human-in-the-loop "does it feel right?"
check.

**Consequence for E0b:** don't re-baseline the brittle magnitude tests — **delete them and write the
behavioral expectations the refactored town must satisfy.** Those expectations are the acceptance
criterion for the refactor and the spec for the re-tune.

## 8. Open decisions (recommendation in italics)

- **Sense radius for buildings.** *Start ~40–60 m (wider than the 12 m person-sense; you notice a
  building from further than you notice a face), tune via soak.*
- **Seed breadth.** *Home + workplace + home-block buildings; widen to nearest-tavern only if the soak
  shows a social-need deadlock.*
- **Naming.** *OddSystem → DeliberationSystem; new ExecutionSystem; PerceptionSystem → SenseSystem.*
- **Intent transport.** *A small `IntentRegistry` (entity → chosen ad) the ExecutionSystem reads,
  mirroring the single-writer pattern, rather than an event — decisions are state, not fire-and-forget.*
