# Sim Systems Overview + Memory Integration Map

A consulting reference for building the full memory integration. Describes the live CQRS sim
as it actually is (grounded in `SimWorld.cs` + the system sources), and where the validated
memory core (A1–A5, `Assets/Sim/Memory/`) plugs in.

---

## 1. The engine model (CQRS, double-buffered)

`Assets/Sim/Engine/` — namespace `DaggerfallWorkshop.Sim.Engine`.

- **Registries** hold all state in `Dictionary<EntityId, …>` (or singletons). Each is the *sole
  writer* of its own data. `Update(tick)` applies queued **intents** from the `EventBus`.
- **Systems** are read-only over registries; they read this tick's state and `Publish<T>()`
  events/intents for next tick. They never mutate registry state directly.
- **`EventBus`** is double-buffered: systems publish into batch N+1 while reading batch N; the
  reducer applies them in registry `Update`. This is the read-tick-N / write-tick-N+1 discipline.
- **`SimEngine.Step()`**: `events.Tick()` (flip) → `Parallel.For` registries (`Update`, write
  phase) → `Parallel.For` systems (read+emit phase) → `Tick++`. `StepSerial()` is the
  determinism reference — a parallel run must produce byte-identical state to a serial one.
- **Time:** fixed timestep. `TicksPerDay = 864000` (the soak runs 864k ticks/day). Speed = tick
  rate, never a variable dt.

The whole memory core was built to this contract: pure data + integer/fixed-point math, key-order
iteration, no floats on the runtime path → replay-exact, ready to become registries + systems.

---

## 2. The per-tick cognition loop (the order that matters)

System order from `SimWorld.cs:103-138`. The cognition-relevant slice, in execution order:

```
… world/body systems (time, weather, health, aging, effects, skills, economy, items) …
NeedsSystem        drives computed — hunger, fatigue, FEAR (the arousal-ish signal lives here)
OddSystem          THE PLANNER — scores activity "ads", picks one, emits an Intent
ExecutionSystem    enacts the chosen Intent → Behavior (ActivityKind)
MovementSystem     moves toward the behavior's target
CreatureSystem     monsters act
CombatSystem       fights resolve
SenseSystem        PERCEPTION — which entities are within range → SensedRegistry (entity IDs)
SubjectiveSystem   INTERPRET — sensed entities → SubjectiveView (per-entity read: valence/threat/…)
AffectsSystem      fold interaction events → acute directed emotions (decaying)
MeaningsSystem     fold interaction events → category stereotypes (role → valence/confidence)
SocialSystem       relationships update (dossiers), friendships
RequestSystem      job/help requests between agents
Lifecycle / Repopulation   birth, death, migration
```

**The cognitive loop, abstracted:** `Sense → Interpret → (next tick) Needs → Plan(Odd) → Act →
… → Sense`. Learning (`Affects`/`Meanings`/`Social` folds) happens at the *end* of the tick, on
discrete interaction events, feeding the *next* tick's interpretation. This is the staggered
detect-at-2 / write-at-5 pipeline the memory spec assumes — it already exists in skeleton.

**Key facts for integration:**
- A "percept" today is just **an entity ID in range** (`SensedRegistry`). There is no atom data.
- `OddSystem` (the planner) reads meaning as **one scalar**: `valence × confidence`, keyed by the
  other agent's **role** (3 values). It never sees predictions, atoms, or surprise.
- **Arousal** is not a stored signal — it's the **Fear drive** computed inside `NeedsSystem`
  (`CompleteThreat(clarity, floor, arousal)`, `SpiralGain = 0.6`). It feeds threat completion, not
  memory.
- There is **no awake/asleep cognitive state** and **no consolidation phase**. `ActivityKind.Sleep`
  is just a behavior; nothing consolidates during it.

---

## 3. Agent-state registries (what an agent *is*)

Grouped by role (from `SimWorld.cs:70-91`). Each is `Dictionary<EntityId, Data>`.

- **Body / vitals:** `Vitals`, `Life`, `Stats`, `Effects`, `EffectAggregate`, `StatusFlags`,
  `Progression`, `Skill`.
- **Drives / disposition:** `Needs` (hunger/fatigue/fear/…), `Personality` (traits: Warmth, …),
  `Conscience` (`Dictionary<ActivityKind, double>` aversive charge — learned guilt per action).
- **Economy / inventory:** `Coin`, `Stock`, `Larder`, `Treasury`, `Items`, `Employment`,
  `Earnings`, `Ledger`, `WorldMarket`.
- **Place / body-in-world:** `Position`, `Behavior` (current `ActivityKind`), `Intent`, `Path`,
  `Occupancy`, `Residency` (role + home), `Buildings`, `TownGrid`.
- **Perception / social cognition:** `Sensed` (entities in range), `Subjective` (interpreted
  reads), `Affects`, `Meanings`, `Relations`, `Memory`, `PlaceMemory`, `Lineage`.

---

## 4. ⭐ The four memory stores ALREADY EXIST (thin + scalar)

This is the crux for the integration. The memory spec's four stores map one-to-one onto four
registries the sim already runs — each currently a thin, scalar version of what the core builds:

| Spec store | Holds | **Existing registry (live)** | Current shape (thin) | New core (rich) |
|---|---|---|---|---|
| **MEANINGS** | category facts/expectations | `MeaningsRegistry` | `CategoryNode { int Signature(role); double Valence; double Confidence }` keyed by 3 roles | `MeaningsStore`: prototype `AtomBag` + variance-gated `PredictedStats` + `Fixed` valence/confidence, nearest-prototype recognition |
| **THINGS** | entity dossiers | `RelationsRegistry` | `RelationData { double Familiarity; double Regard }` per known entity | `MemoryStore` of `MemoryRecord` keyed by signature, delta vs category |
| **EVENTS** | episodes (who/what/where/when) | `MemoryRegistry` | `MemoryEntry { long Tick; MemoryKind Kind; EntityId Other; int Building }` (flat list) | `MemoryStore` of delta-records (subject/verb/object/place) + affect tag atom |
| **PLACES** | spatial facts | `PlaceMemoryRegistry` | `PlaceFactValue { double Value; long AsOfTick }` keyed by `PlaceFact` enum | `MemoryStore` keyed by position, category-ref + delta |

**So full integration is an UPGRADE of four existing stores, not a greenfield bolt-on.** The
sim already learns stereotypes (Meanings), keeps dossiers (Relations), logs episodes (Memory),
and remembers places (PlaceMemory) — all as scalars. The core replaces each scalar with the
atom/delta/variance-gated version, and unlocks what scalars can't express: surprise, gist-as-
delta, consolidation, confident false memory, recall-as-reconstruct.

---

## 5. The new memory core (A1–A5), recap

`Assets/Sim/Memory/`, namespace `DaggerfallWorkshop.Sim.Memory`. 115 tests, 5 clean reviews, zero
floats on the runtime path, **no live wiring yet**.

- **A1 substrate:** `Fixed` (Q8 fixed-point), `AtomTypeId`/`Atom`, `AtomBag` (sorted; `Merge` =
  recall, `Diff` = divergence), `RunningStat` (integer mean/variance).
- **A2 record stores:** `MemoryRecord` (delta-record: key, categoryRef, deltaBag, strength,
  flags), `MemoryStore` (bounded, key-sorted, Encode/Refresh/Decay/evict), `AgentMemoryStores`.
- **A3 MEANINGS:** `PredictedStats` (variance-gated intersection — the one new primitive),
  `CategoryNode`, `MeaningsStore` (recognition + reinforce + fold), `MemorySeeds`.
- **A4 surprise + encode gate:** `Surprise` (MAX attention / MEAN encode), `MemoryEncoder.Perceive`
  (recognize → fold → surprise → gate → record; verbatim when novel).
- **A5 consolidation + recall:** `MemoryRecall` (reconstruct = confident false memory;
  reconsolidation), `Consolidation` (RE-DIFF → MINT → DECAY).

**Known boundary:** MINT compresses only *exact* value matches (prototype is a truncated mean,
Diff is exact-equality). SETTLE/split is deferred (spec calls it under-pinned).

---

## 6. What full integration requires (the build pieces)

To make agent behavior actually change, five pieces — roughly in dependency order. Each is a
Phase-B sub-project with its own spec → plan → review.

1. **Percept → AtomBag projection (the hard design call).** Decide which agent attributes become
   atom types. Today an interpreted entity carries (from `SubjectiveSystem.Interpret`): role
   (`Residency`), dossier (`Relations` familiarity/regard), observable behavior (`Behavior`),
   personality stigma (`Personality`). These become a signature `AtomBag` (identity atoms) + a
   percept `AtomBag` (full atoms incl. an affect-tag atom). **This is where "is it full of special
   cases" gets answered** — a clean projection vs. a pile of per-attribute hacks.

2. **Expose arousal as a signal.** `MemoryEncoder.Perceive` needs a `Fixed arousal`. Source it
   from the Fear/threat drive in `NeedsSystem` (proxy) or add an `ArousalRegistry`. Decide whether
   arousal is global (agent-level) or per-target.

3. **MemoryWriteSystem (awake, row 5).** New system after `SubjectiveSystem`: for each attended
   sensed entity, build the atom bags, call `MemoryEncoder.Perceive`, route the resulting
   `MemoryRecord` to the right store (THINGS/EVENTS/PLACES) via `Encode`, and `StatFold` the
   recognition into MEANINGS. Replaces the discrete-event folds of the old `MeaningsSystem`.
   *Behavior change:* agents now learn from *perception*, not only explicit interactions.

4. **ConsolidationSystem (asleep, row 7).** New system gated on `ActivityKind.Sleep`: run
   `Consolidation.Pass` over the agent's stores (RE-DIFF → MINT → DECAY). Needs the awake/asleep
   split (key off `Behavior`). Decay is sleep-gated, so awake memory never fades — population-
   staggered sleep amortizes the cost.

5. **OddSystem read adaptation.** The planner currently reads `valence × confidence` as a scalar.
   The new `MeaningsStore` can supply that directly (a `CategoryNode` still has `Valence`/
   `Confidence` as `Fixed`) — so the *minimum* change is a scalar adapter at the read boundary.
   The *richer* payoff (the planner reacting to surprise, recalling places/entities via cue) is a
   larger `OddSystem`/`SubjectiveView` change, best staged after 1–4 prove out.

**Recommended first step:** a throwaway spike of piece 1 + a scalar slice of 3 — feed real sensed
entities through a minimal projection into `MeaningsStore` for a few agents, run a soak, and
measure the projection friction before committing to the full five. The projection in (1) is the
load-bearing risk; everything else is mechanical once atoms exist.

---

## 7. Baseline behavior (pre-integration, for diffing later)

1-day soak, Gothway Garden (Daggerfall), seed 12345, pop 337→340:

```
11:29  Sleep=71 Wander=63 Flee=61 EatHome=28 Buy=28 Visit=23 Socialize=20 Farm=19 Work=18 Fish=6
17:29  Sleep=118 Buy=57 Flee=46 Wander=34 Farm=22 Work=21 EatHome=15 Visit=11 Fish=8 …
23:29  Sleep=337
coin ~6900 → 6584, meanHunger low, 13 monster kills inside walls
```

Healthy: diverse activity, day/night rhythm, stable economy. The integration's job is to make this
*shift* in legible ways (e.g. agents avoiding places/entities they have bad memories of, recognizing
strangers by learned category, surprise-driven attention) — diff future soaks against this.
