# The Cognitive Layer — Map & Audit

> **Date:** 2026-06-23. **Status:** authoritative reference for what the cognitive substrate
> *intends* (the `what_is_*` docs) vs. what the code *does* (this audit). When a `what_is_*` /
> `cognitive_substrate_*` / `odd_*` doc claims a behaviour is built or proven and this file
> disagrees, **this file is the ground truth on implementation status** — the design docs remain
> the ground truth on intent.
>
> Produced by a five-way parallel audit of `Assets/Sim` + `Headless` against the design docs.
> Vendored Daggerfall-Unity reference (`Assets/Scripts/Game`, `Assets/Game`) is out of scope.

---

## Part 1 — The cognitive layer (the model)

The canonical loop (from `what_is_a_bunny.md` + `what_is_world.md`), a pipeline of per-tick
registers:

```
1a Raw-sense ─┐
   Memory ────┼─► 1b PERCEIVE ─► 2 INTERPRET ─► 3 PLANNER/ODD ─► 4 ACT ─► 5 REMEMBER
   Signals ───┘   (attention,    (subjective    (Collect→Cull→             (6 Metabolism ticks
                   capacity-K,     molecule:      Score→Build→               poles, 7 Consolidate
                   drive-biased)   recog/valence) Propagate→Traverse)        in sleep)
```

### The three sources → one pool → cull → score

| Source | Enters at | Mechanism (intended) |
|---|---|---|
| **Senses** | Collect | perceived smart-objects advertise ads |
| **Memory** | Collect, **via the cull** | an active drive raises the matching channel → the *remembered* satisfier wins an attention slot → reaches the market as a percept (the planner never queries memory directly) |
| **Self** | Object Zero | the agent advertises *to itself*: static fallbacks + role/shift + **goal beacons** (general — a need, a duty, a quest, a bond; somatic is one instance) |

**Two culls, both = "only what matters now":**
- **Attention cull (1b)** — capacity-limited top-K, **biased by the active drives**. Hunger raises
  food, fear raises threat and suppresses the rest. *This is the relevance filter — the cull's whole job.*
- **Gate cull (3, Collect)** — preconditions tested against the **subjective** molecule + self-state.
  Fear reads `safety:high` as low → `Binky` never enters the market. The gate decides what's
  *thinkable*; `V` (score) only ranks the survivors.

`V` scores `(pole-urgency × target-affordance)` pairs joined on the ad's target; the `Enables`-DAG
(`OddTree`) propagates discounted subtree value backward so "work is worth the meal it buys."

This is the shape the `OddSystem` converges toward (a faithful **depth-1 subset** today).

---

## Part 2 — Audit by subsystem

Severity: **HIGH** (blocks correctness or the planned build), **MED**, **LOW**. State: *active*
(misbehaves now) or *latent* (wrong but masked today).

### 2.1 Drives + emotion — *shape faithful, wiring broken, untested*

**Works (high confidence):** the 4-field `DriveDef` + `LevelSource` exactly matches the doc
(`DriveCatalog.cs:50-59`); `Weights`/`DriftPerHour` genuinely project from the one table;
per-drive Max/Sum projections authored correctly (fear=Max, social=Sum); derived levels
(coin/larder/threat) read fresh each tick, never stored-and-drifted (`NeedsSystem.cs:119-145`);
`DriveGraph` is a real Kahn topo-sort with cycle-throw; the fear controller's math matches `fear.md`
(`NeedsSystem.cs:229-234,243-274`); `Affects` is a genuine per-tick rebuild, never replayed
(`AffectsSystem.cs:45-113`).

**Divergences:**
- **[HIGH, active] The prepotency cull is decorative.** The cull keys off a hand-tagged
  `Prepotent` bool (`OddSystem.cs:649`) set on exactly two specs — `Socialize`, `Visit`
  (`ActivityCatalog.cs:248,285`). The `DriveGraph` gate-edges (`hunger ⊣ {social,goods,coin}`) are
  recorded but **never mapped to which ads get culled**. So `Buy`/`Steal`/`Beg`/`Gossip` are never
  suppressed — **a starving agent shops and begs freely**. The doc's keystone property ("every
  deficiency gates every growth directly") is violated. Fear's hard-cull edges are inert for the
  same reason (MED, latent — fear is rare today).
- **[MED, latent] `FearLevel` smoothing is non-compositional** (`NeedsSystem.cs:268-271`): linear
  `prev + rate·(target−prev)` clamped, not the geometric `Decay.TowardBaseline` used everywhere
  else. Tick-rate-dependent; fine only under the fixed-timestep invariant.
- **[LOW] `AffectKind.Fear`/`Loneliness` authored but never minted** — the scalar `NeedAxis.Fear`
  controller and the directed `AffectKind.Fear` residual are two separate "fears" that never meet.
- **[LOW] S2.3 magic-number retirement unmet** (literal ±0.x impulses still in `AffectsSystem`);
  `RepopulationSystem.cs:130` seeds derived axes into the stored vector (harmless, latent).

**Tests:** **none.** The fear/affect unit tests were **deleted 2026-06-22 (commit `2cdc353a3`)** and
never migrated. `fear.md:71-73` still claims they prove the dynamics. The whole subsystem is
unverified.

### 2.2 Memory — *best-built core, three load-bearing pieces not live*

**Works (high confidence):** the atom core (`Assets/Sim/Memory/*`) is the cleanest in the codebase —
three bounded key-sorted stores with spec caps (`AgentMemoryStores.cs:11-18`); delta-records with
**per-atom** strength+flags (`MemoryRecord.cs`, `AtomMeta.cs`); novelty stored verbatim; write gate
= surprise OR arousal with per-atom etch; MAX-for-attention / MEAN-for-encode split present
(`Surprise.cs:21-41`); MEANINGS = variance-gated running statistics (`PredictedStats`,
`RunningStat`); recall = predicted ⊕ delta (delta wins); consolidation RE-DIFF→MINT→DECAY with
strength-scaled, sleep-gated decay; caps + lowest-strength eviction; fixed-point determinism. Unit
tests here are high-quality and honest (they document their own compression limits).

**Divergences:**
- **[HIGH, active] The innate MEANINGS seed is never installed in the live path.**
  `MemorySeeds.Install` is called only in tests; `AgentMemory` constructs an *empty* `MeaningsStore`
  (`AgentMemoryRegistry.cs:33,70`). So every agent spawns recognizing **nothing** → every percept is
  novel → every THINGS write is verbatim/SURPRISE/strength-255. The doc's "the dictionary can't
  bootstrap from empty, so it doesn't start empty" is violated.
- **[HIGH, gap] EVENTS store allocated but never written** — episodic + self-memory, a whole pillar,
  is absent; nothing calls `Stores.Events.Encode`; plan-surprise → EVENTS is unwired.
- **[MED, active] THINGS stores signature-only** (`MemoryWriteSystem.cs:42-46`) with `Arousal`
  hard-zeroed — dossiers carry no behavioural content and flashbulb-by-arousal can never fire.
- **[MED, latent] A legacy float `MeaningsSystem` still drives the live interpretation**
  (`SubjectiveSystem.cs:121-127`, `MeaningsSystem.cs:99-100` uses float `Decay`). Two parallel "what
  I think of your kind" stacks blended live; the live one is the determinism exception.
- **[MED, latent] MEANINGS has no decay/eviction of learned nodes** (`MeaningsStore.cs:36-37`
  throws when full) — unbounded for a long-lived agent.
- **[LOW] encode-gate uses MEAN where the doc proposed SUM/count** (defensible divergence — doc
  should be updated to match); SETTLE/split deferred; MINT clustering order-dependent (deterministic
  but not the hash-seeded spec); PLACES never participates in MEANINGS.

**Tests:** strong unit coverage of the core; **thin integration coverage gives false confidence** —
`AgentMemoryStoresTests` asserts the EVENTS capacity (implying it exists, though it's never written);
`AgentMemoryLearningTests` would pass with the empty-seed pathology fully active.

### 2.3 Perception + interpretation — *plumbing sound, meaning hollow*

**Works:** the 1a/1b membrane is respected (raw-sense is affect-blind; attention sits one stage
later); CQRS single-writer holds; `AtomBag` Merge/Diff and the `BlendValence` math are correct and
tested.

**Divergences (all of my six earlier findings CONFIRMED, plus deeper ones):**
- **[HIGH] `Interpret` is a scalar lens, not a subjective molecule.** It never reads the other
  entity's perceivable `AtomBag` at all (`SubjectiveSystem.cs:110-145`) — so the doc's load-bearing
  "one sufficiently-aversive atom **vetoes** the whole reading (a meadow of clover still reads flee
  under one fox-atom)" is unimplemented. It aggregates dossier-regard + affect + role + stigma into a
  number.
- **[HIGH] Fear-completion can't fire on ambiguity.** `CompleteThreat` (`NeedsSystem.cs:229-234`)
  lives in the fear *drive*, not `Interpret`, and only triggers when `Threat>0`, which is set **only
  for actual creatures** (`SubjectiveSystem.cs:101-108`). A timid agent can never conjure threat from
  a faint/ambiguous non-creature cue — the doc's signature case. Recognition is binary-objective.
- **[MED] Attention is need-blind *and* inert.** `Attention = 0.1 + |valence| + familiarity`
  (`SubjectiveSystem.cs:144`) takes no `NeedsData`; no drive channel. And the computed magnitude is
  read by **nothing** downstream except `TrimToTopK`'s own sort (`:279`).
- **[MED] Somatic atoms are write-only and never cleared** (`SomaticPerceptSystem.cs:23-33`); the
  `Signature` filter (`PerceivableRegistry.cs:83`) excludes the 7000+ band, so no consumer ever
  reads them, and a need falling to 0 freezes the last positive value.
- **[MED] `MemoryWriteSystem` takes first-K of the raw sensed list** (`MemoryWriteSystem.cs:30-35`),
  not attention-K; it never consumes `SubjectiveView`.
- **[MED] Indoor/`Doing` agents retain a stale sensed list indefinitely** (`SenseSystem.cs:52`
  only emits for out-and-about; `SensedRegistry` clears only on despawn) → phantom
  greetings/dislikes/memories.
- **[MED, maintenance] `Interpret` is duplicated in full** (`OddSystem.cs:819-867` vs
  `SubjectiveSystem.cs:93`) and the copies have **diverged** — the inline one lacks the learned
  AgentMemory blend, so building-occupant valence ignores learned meanings. The "verbatim" comment is
  false.
- **[LOW] `Recognition`/`Trust` ignore clarity** (`SubjectiveSystem.cs:143-144`); the percept
  metadata (intensity/clarity/origin) is discarded by `SenseSystem` (ids only). The cadence constant
  `5` is duplicated 4× (`SenseSystem`, `SubjectiveSystem`, `SomaticPerceptSystem`, `OddSystem`).

**Tests:** `SubjectiveSystem.Interpret`/`TrimToTopK`/`Update` have **zero** tests;
`LearnedValenceBlendTests` tests a pure function the live `OddSystem` path never calls.

### 2.4 ODD planner + cull + chains — *machinery sound, the gaps block the build*

**Works (high confidence):** `OddTree.Build/Propagate/Traverse` faithfully implements the spec's
Kahn + backward-pass + argmax (`OddTree.cs:34-100`); `V` is a single uniform expression (no verb
switch, `OddSystem.cs:637-662`); preconditions are separated from scoring; `ConscienceFactor` is
correctly wired into `V` as a somatic-marker bias (`OddSystem.cs:654,706-710`); the snapshot mirrors
Traverse.

**Divergences:**
- **[HIGH, active — the 0/337 root] Chain collapse.** `GatherAds` *removes* stock-gated ads
  (`Buy`/`EatTavern`) from the pool before Build (`OddSystem.cs:427-428` via `SaleStockAvailable`),
  and `EnabledIndices` (`:556-563`) only wires `Enables` edges to verbs **present** in the pool. So
  when a shop is empty, the `Work→Buy` edge never forms, `Work` becomes a childless root, and the
  meal's value never propagates back. The break is at the **mid-chain** link (`Buy`), not the
  terminal (`EatHome` is larder-gated → removed only as a *root*, kept in the pool). The spec
  (`what_is_world.md:375`) explicitly says unafforded chain links must stay in the buffer *for
  propagation only* — the code culls them pre-Build, the exact opposite.
- **[MED-HIGH, latent] No self-state cull layer.** The gate tests only static world facts
  (hours/holiday/keeper/stock; `ActivityCatalog.cs:74-77`). Drives/fear/affect bias *score* (inside
  `V`'s `gate` product), never *gate-removal*. **There is no `self.X` precondition layer for a
  drive-biased cull to live in** — the exact hook the planned cull needs has no socket.
- **[HIGH, latent→active] No terminal revalidation.** The spec mandates re-checking preconditions
  before executing a terminal (`odd_spec.md:101-109`); `Decide` publishes the intent
  (`OddSystem.cs:271,353-366`) with no re-check. Becomes a correctness bug the moment a multi-tick
  chain commits to a step whose stock/larder drains mid-walk.
- **[MED, by design] Structural inertia weaker than spec.** The queue path is polling-with-hysteresis
  (`PreemptEveryTicks=5`, `ShouldSwitchCommitment` 15% margin), not the spec's binary
  commit/interrupt. "No oscillation" is only probabilistically held.
- **[expected] Goals/beacons UNBUILT** — no `AgentContext.Goals`/`GoalBonus`/`BeaconAction`;
  `DirectScore` = `V` alone. `Panic`/`Rest` missing from Object Zero (`Idle/Wander/Flee/Attack` vs
  the spec's `Idle/Wander/Panic/Rest`). `V` dereferences `ad.Spec` with no null-guard (`:639`) — a
  beacon ad with `Spec=null` will NRE.

**Tests:** the decision core (`Decide`/`GatherAds`/`V`/`EnabledIndices`) is tested **nowhere**. The
one tree test (`OddSnapshotTests`) uses `precond: _ => true` — **nothing is ever culled, so it stays
green while the live town produces 0 chains.** Queue/preemption *helpers* are unit-tested in
isolation; their wiring into `Decide` is not.

### 2.5 Tick architecture + determinism — *better than feared, not the doc's machine*

**Works:** it's a **working CQRS event-bus** (read intents this tick, apply at the boundary) — **not**
the suspected serial pipeline. Single-writer-per-agent holds by convention.

**Divergences:**
- **[determinism] Ordering hazards, untested.** Sense→memory selection depends on `Dictionary`
  iteration order; buffers resolve last-write-wins; memory apply order matters. There are **zero
  replay/determinism assertions** (no same-seed-twice-equal test); the seed is fixed at 12345 but
  never used to compare two worlds.
- **[perf-ceiling] Not the doc's engine.** Single-axis parallelism, dict/AoS storage — not the
  double-buffered SoA + dual-axis `Parallel.For` + gather-reduce machine `what_is_world.md`
  describes.
- **[latent correctness] A1/A2 unaddressed** — the private-write "single-writer" invariant is held by
  hand-coded convention, not by a commutative gather-reduce; `EntityId` is a bare int (id-reuse
  hazard), not generational.
- The double-buffer / N+1 latency is *relied on* by 4+ tests (`UtteranceWireTests.cs:27`,
  `ExecutionQueueRoutingTests.cs:37-46`, …) and **asserted by none** — a refactor that collapsed the
  bus delay would pass the whole suite.

---

## Part 3 — Cross-cutting themes

1. **The wiring gap.** Across all five subsystems: the structures are built and unit-tested in
   isolation; the *integration into the live tick* is missing or wrong. The cognitive **substrate**
   exists; the cognitive **agent** doesn't quite run yet.
2. **The test suite gives false confidence on the live path.** The decision / interpretation /
   learning loop is tested *nowhere*; isolated helpers are tested well. Several tests *mask* the
   bugs: the tree test (`_ => true`) hides chain collapse; an EVENTS-capacity test implies a store
   that's never written; a `BlendValence` test passes for a path the live planner never calls;
   deleted fear tests leave `fear.md` claiming proof that's gone.
3. **Determinism is argued, not asserted.** Zero replay tests; several real ordering hazards.
4. **Two parallel meanings stacks** (new fixed-point core vs legacy float system) blended in the
   live interpreter.

---

## Part 4 — Remediation sequence

The operative constraint (audit of §2.4): **do not layer goal-beacons + a drive-biased cull until
the foundation is fixed — the cull has nowhere to live, and the chains it would steer don't
propagate.**

### Phase 0 — Foundation (the blockers). Small, contained, each independently testable.

| # | Fix | Unblocks | Evidence |
|---|---|---|---|
| 0.1 | Keep stock-gated mid-chain links in the pool (gate at root-eligibility + terminal-revalidation, not pre-Build) | chains propagate (0/337 → real) | `OddSystem.cs:427-428`, `:556-563` |
| 0.2 | Add a self-state precondition/cull layer (an `Ad`-level predicate list evaluated as removal in Collect) | the drive-biased cull gets a socket | `OddSystem.cs:427-428` |
| 0.3 | Terminal revalidation before executing the chosen action | chains commit safely | `odd_spec.md:101-109` |
| 0.4 | Make the prepotency gate cull by the **axis its edges name**, not the `Prepotent` bool | the cull expresses drive prepotency | `OddSystem.cs:649`, `ActivityCatalog.cs:248,285` |
| 0.5 | Install the innate `MEANINGS` seed at spawn | interpretation has something to recognize with | `AgentMemoryRegistry.cs:33,70` |

### Phase 1 — The subjective layer

Make `Interpret` read the perceivable atom-bag (prey veto); give attention a drive channel that is
actually *consumed* downstream; move fear-completion into interpretation so it can fire on ambiguity;
de-duplicate `Interpret` (one shared implementation); reconcile the two meanings stacks onto the
fixed-point core.

### Phase 2 — The planned build, on a sound base

O3 goal-beacons (general `AgentContext.Goals = (MatchFn, BeaconAction)`); the drive-biased cull (now
it has a hook); the memory-recall door (active drives raise the channel so remembered satisfiers
reach the market). This is "self as a first-class source."

### Throughout

Backfill **behavioural** tests on the live path (the audit proved the unit tests don't guard it),
add a replay-determinism test, and decide EVENTS' fate (implement, or stop reserving its cap).

---

## Appendix — the design docs this audits against

`what_is_a_bunny.md` (the loop, subjective truth, memory kinds) · `what_is_drive.md` (pole×target,
4-field drive, prepotency, emotion) · `what_is_world.md` (registries, the tick, the planner, cull vs
score) · `what_is_a_memory.md` (four stores, delta-records, consolidation) · `odd_spec.md` /
`odd_convergence.md` (the ODD algorithm + the depth-1→true-ODD staging, incl. O3 Goals+Beacons) ·
`drive_engine.md`, `fear.md`, `cognitive_substrate_S1–S4`.
