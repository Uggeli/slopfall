# What Is a World?

> Companion to **What Is a Bunny?**. That doc describes how an *entity* works — what it perceives, wants, remembers, does. This doc describes how the *world* runs it **as code**. They are the same thing seen from two sides.

## The two docs are one grid, transposed

The bunny doc and this doc are not two models — they are two readings of a single table.

- **What Is a Bunny? walks *down a column*** — pick one entity, and describe everything it *is* (its atoms/molecules — nouns) and everything it *does* (its actions/activities — verbs). Entity-centric.
- **What Is a World? walks *across a row*** — pick one kind of data or one piece of logic, and describe every entity it touches. System-centric.

|  | Bunny doc (domain) | World doc (substrate) |
|---|---|---|
| reads the grid | down a column — one entity, all its stuff | across a row — one registry/system, all entities |
| nouns | atoms · molecules | **components in registries** |
| verbs | actions · activities | **systems** |
| "private process" | what the entity experiences | *a behavioral invariant systems uphold — not a wall* |

> The whole correction this doc rests on: **the bunny doc describes the entity, not the engine.** Its layers — process vs molecule, public vs private, subjective vs objective — are facts about *what the entity experiences*. In code they become **data in components + logic in systems**, and they do **not** carve up the engine. There are no private registries. The membrane is upheld, not enforced.

---

## Entities are just IDs

An entity is an **`EntityId`** — an integer index, nothing more. It owns no data and has no methods. There is no `Bunny` object.

What we *call* an entity is the **cross-section across the registries at one id**: the bunny "is" its row — `Position[id]`, `Body[id]`, `Drives[id]`, `Memory[id]`, … assembled by sharing a key.

> **A molecule is an entity's cross-section.** The bunny doc's central noun — a molecule, "a bundle of atoms held by relations" — is *exactly* the set of component values sharing an `EntityId`. Atoms are component values; the molecule is the row. The two docs meet here: nouns are the data model viewed from the entity's side.

Data is **SoA** (structure-of-arrays): one array per component, indexed by id. Cache-friendly, trivially parallel, and the natural shape for streaming public state to the client renderer.

---

## The world — shape and constants

Fixed decisions the rest of the engine builds on:

| Constant | Value | Why |
|---|---|---|
| **Tile size** | 1 tile = **1 m** | pairs with 1 tick = 100 ms — space and time both anchored |
| **Elevation** | **none** — flat 2D | water and movement stay simple; no flow sim, no slopes |
| **X axis** | **wraps** (cylinder) | seamless east–west; with poles at the Y edges it reads as a globe and feels bigger than it is |
| **Y axis** | **bounded** — poles at the edges, equator at mid-Y | drives the temperature gradient |
| **World size** | **config** — 32²/64² for tests, target **16 384² (16k)** | small worlds to iterate, one big world to ship |
| **Seed** | worldgen is deterministic `f(seed)` | reproducible worlds; feeds the replay guarantee |

> **The seam is sealed; everything above it is wrap-oblivious.** X-wrap lives in exactly two places — the **wrapped-coordinate type** (all position arithmetic normalises X mod W; `a.delta(b)` and `a + offset` take the short way round) and the **spatial-access layer** (index neighbour queries, the field stencil's neighbour reads, and the chunk graph's adjacency all auto-wrap). A system calls `index.near(pos, r)` or `a.distance(b)` and gets wrapped-correct answers with no `if (x > W)` in sight. Y is a hard **pole boundary** — clamp, never wrap. Noise samples on a **cylinder** (X → angle → 3D noise) so terrain is seamless across the seam.

> **Even the renderer is sealed — zero wrap-aware consumers anywhere.** The client requests a *viewport* (an area); the server resolves it through the same wrap-aware layer — stitching across the seam if the area spans it — and returns the data in a contiguous **local frame** plus a **viewport spec** (the local↔world mapping, wrap offset included). The renderer blits local coordinates and maps interactions back through the spec; it never computes a wrap. Crossing the seam is the local frame sliding while the spec's origin wraps underneath — invisible. A viewport request resolves to a set of (possibly seam-spanning) chunks, streamed as deltas in that local frame. Cost is a thin seam-strip where a query box splits in two — confined to the index/viewport service.

> **Memory at 16k².** 268 M tiles. A 4-byte `Tile` ≈ 1 GB; a tile-resolution byte field ≈ 268 MB (×2 double-buffered). That's why fields carry **per-field resolution** — climate fields (temperature, wind) live at *chunk* resolution (65 k values, trivial), only moisture/scent need tile resolution — and why chunks generate **lazily** and idle field-chunks cost nothing. The big world works because almost none of it is hot at once.

---

## Registries — the data

A **registry** is one component's array, keyed by `EntityId`. All registries are flat and uniformly accessible to every system; "privacy" is a property of the *model*, not the *store*.

The bunny doc's "Physical & Mental Things" become registries. Grouped by what they are:

### World / observable molecule (what others can perceive)

| Registry | Atoms it holds | On entities |
|---|---|---|
| `Position` | position, orientation/facing | anything spatial |
| `Body` | shape, size, material, mass, color, texture, integrity … (a molecule's atoms) | every physical thing — bunny, carrot, soil |
| `Motion` | velocity, direction-of-travel | moving things |
| `Condition` | temperature, moisture, freshness/age, phase | food, water, soil |
| `Signal` | signature, channel, intensity, origin, **decay** | transient *non-diffusing* pulses — a thump (sound), a groom display (scent is a **field**, not a Signal — see *Senses*) |
| `Relations` | part-of · contains · adjacent · connects | nests, burrows, composites |

### Entity process state (private *by model* — still just components)

| Registry | Holds | Timescale |
|---|---|---|
| `Senses` | which channels this entity reads, range/acuity | static-ish |
| `Percepts` | this tick's gathered `PerceivedAtom`s (**scratch**) | per-tick |
| `SubjectiveView` | interpreted atoms — recognition, valence, attention, trust (**scratch**) | per-tick |
| `Marketplace` | ads that passed this tick's state + perception culling (**scratch**) | per-tick |
| `Plan` | committed ODD buffer + traverse position + current action's progress | persists until the action completes or interrupts |
| `Affects` | this tick's felt read — the residual over the `Drives` poles (**scratch**) | per-tick |
| `Drives` | drive **poles** (levels keyed by `DriveId`): the innate set — hunger, thirst, libido, energy↔fatigue, safety/fear, social — plus poles minted by learning (an addiction, a specific bond) | slow |
| `Memory` | spatial · entity · episodic · self · categorical · factual | learned |
| `Personality` | bold↔timid, curious↔neurotic, social↔solitary | lifelong |

### Static tables (frozen, shared, not per-entity)

| Table | Maps | Read by |
|---|---|---|
| `AtomTypes` | atom type → value domain (how to read its bits) | everything |
| `Ads` | ad → {Data, Preconditions, Enables} — actions *and* activities alike | the Planner |
| `Reactions` | rule → {reactant patterns, relation, conditions → transform} | Reaction systems |
| `DriveDefs` | drive → {score-field, urgency-projection (MAX · SUM), satisfaction-model (deplete · reset-on-percept · none), gate-edges} | Metabolism, Interpretation, the Planner's `V` |

> **Drives are extensible — a pole is data, not an enum.** The innate poles are dense SoA; *acquired* poles (a withdrawal deficit, a specific bond — see *What Is a Drive?*) are minted at runtime by the learning loop and live in a sparse per-entity bag keyed by `DriveId` — the same dedicated-component + stored-bag split the atom backings use below. An install is an absolute write from a third `DriveDelta` source, resolved by A1's priority-then-min-id; the bag is one more thing that accrues over a long sim (open issue ii). What a `DriveId` *means* is read from `DriveDefs`, which is a **frozen library of templates** (deficiency-deplete, aversion-reset, directed-craving…) — *not* a per-drive enum. An innate pole names a template directly; an *acquired* pole stores `(template-ref, target/signature, level)` in the bag, taking all four definition fields from the frozen template and supplying only its runtime target and level. So minting a drive at runtime adds pole **data** referencing a frozen def, never a new def — "frozen table" and "runtime-minted drive" coexist with no contradiction. This is why **addiction needs no new machinery** (proto #5 would only re-run verified parts): it's the `directed-craving` template — sensitized "wanting" decoupled from reward (Berridge), so its urgency overrides real deficits — instanced on a substance signature, ticked as a deficiency (withdrawal) and written by the cue-spike path (the third writer). The only bespoke part is *which* template and signature the learning loop installs; the gate-edges and satisfaction-model come from the template. Two template fields change the engine's defaults:
>
> - **Satisfaction is not uniform.** Only **deficiency** poles run the `action → DriveDelta` depletion loop (Metabolism ticks the level up, a landed Feed writes it down). **Aversion** poles (safety) *reset on percept-absence* — fox gone, the level falls back to its vigilance floor, no delta. **Growth** poles (curiosity) never deplete — engaging may amplify. The `satisfaction-model` field routes each.
> - **Gates form a DAG.** Drives suppress one another — curiosity ⊣ safety ⊣ hunger; a starving bunny can't binky — so `gate-edges` get the same acyclicity + topological-order guarantee as the ODD Build. Kahn again, already in the engine.

### Atoms are a read-interface, not one storage format

An atom describes *what a thing is or is like* — orange, plant-fibre, soft. It is the **query interface** every precondition, reaction, and value function reads reality through. It is **not** a single storage layout. Three backings, chosen per property by how hot and how common it is:

| Backing | For | Example |
|---|---|---|
| **Dedicated component** (SoA, typed) | hot, near-universal | `Position`/`Velocity` as `Vector2[]` — read every tick by steering & perception |
| **Stored atom bag** (per-entity sorted `Atom[]`, binary-searchable by `AtomTypeId`) | cold, variant descriptive tail | a carrot's {colour, taste, material, freshness} |
| **Virtual atom** (derived on demand from packed storage) | massive uniform fields | ground — `material`/`hardness` *projected* from the `Tile` struct, `moisture`/`scent` from field arrays, only when queried |

> **Position is not an atom** — it's *where* the thing is, the substrate, a dedicated `Vector2`. Atoms are *what* it is.

> **Why virtual atoms.** A world is mostly ground. Materialising a real `Atom[]` per tile would bloat memory for data that's identical across millions of cells. So a tile's *static* props are a compact `Tile[]` struct (`material:byte, hardness:byte, fertility:byte`) and its *dynamic* props are field arrays (`moisture`, `scent`…; see *Fields*). When a reaction or affordance asks "is this tile `hardness: soft`?" we **derive** the atom from the struct's bits (or a field's array) on the spot. The querier never knows the difference — that's the point of atom-as-interface: matching is oblivious to backing.

> **Atom value.** `readonly record struct Atom(AtomTypeId Type, AtomValue Value)`; `AtomValue` is a tagged union over the unmanaged kinds (scalar · symbol/categoryId · count · bool · entityRef), explicit-layout, no boxing. The `AtomType` already carries the value domain, so interpreting bits needs the type in hand — which a matcher always has. `with`-expressions make deltas one-liners.

> `Percepts`, `SubjectiveView`, `Marketplace`, `Affects` are **pipeline registers** — each holds one stage's output for exactly one tick, then is overwritten (see *Snapshot semantics*). Never accumulated, never serialized to the client. This is the bunny doc's "feeling is rebuilt, never stored," realized literally: the subjective molecule — *and the feeling itself* — is rebuilt every tick, never carried. A lingering **mood** is not stored affect; it's this register re-deriving from a slow, unresolved `Drives` pole tick after tick. Persistence lives in the pole; the feeling is rebuilt.

---

## Systems — the logic

A **system is (an entity list) + (per-entity logic).** It owns the entities whose components it writes; it iterates them, and for each, *that* entity is "self." It reads self's full state plus whatever **observable** components of *other* entities the logic needs — and emits events. It never mutates a registry directly.

> **Membership = "has the components I care about."** Perception owns everything with `Senses`. A rock has none, so it isn't a *subject* of Perception — but its `Body`/`Signal` atoms still get *read about* when a perceiver senses it. The list is the subjects a system **writes for**; its **reads** range wider, which is safe because the read phase is immutable.

The bunny doc's verbs — the perceive → interpret → plan → act → remember loop — become this pipeline of systems. Each is one stage applied across all its entities.

| # | System | Owns (membership) | Reads | Emits |
|---|--------|-------------------|-------|-------|
| 0 | **Reflex** | has `Senses` + reflexes | self `Percepts`/`Signal`, self `Affects` | a `CRITICAL` interrupt **+** an immediate Action (Freeze, Scream) — preempts the Planner |
| 1a | **Raw-sense** (Vision · Scent · Hearing · Touch) | has the matching `Senses` | the world — tiles via the vision template, the scent field, `Signal` pulses (**the membrane-in crossing**) | → self raw-sensory buffers |
| 1b | **Perception** | has `Senses` | self raw buffers + self `Memory` cues + self `Affects` (the register, from N−1) | → self `Percepts` (**affect-biased, capacity-limited** attention; see *Attention*) |
| 2 | **Interpretation** | has `Percepts` | self `Percepts` + self `Personality`·`Drives`·`Affects`·`Memory` | → self `SubjectiveView` + self `Affects` (the felt read, derived from `Drives` residuals); **pole writes** (the third writer — absolute percept-tracking for aversion poles, additive cue-spikes for acquired/craving poles; the innate deficiency poles stay Metabolism + landed-action only); surprise → Memory |
| 3 | **Planner (ODD)** | has `SubjectiveView` | self `SubjectiveView` + `Ads` (preconditions · V · Enables) + self `Drives`·`Affects` + self `Plan` | replan → self `Plan`; traverse → the committed action |
| 4 | **Action** | has a running `Plan` | self `Plan` + self `Position`·`Body` | world-mutation events (Move, Bite, Lap, ChinRub, Thump, DigScratch, Mount…) |
| 5 | **Memory** | surprise/stakes flagged | self surprise (prediction error) + `Affects` intensity | MemoryWrite events (deltas vs category) |
| 6 | **Metabolism** | has `Drives` | self `Drives` + `DriveDefs` + elapsed ticks | Drive-delta events (deficiency poles only: hunger↑, energy↓) |
| 7 | **Consolidation** | **in `Sleeping` state** | self `Memory` (episodic) | MemoryWrite events (mint/update facts, decay) |
| — | **Growth** | vegetation | self `mass` + elapsed ticks + ambient light/moisture | mass↑; seeding reaction |
| — | **Weather** | ambient field + terrain tiles | the weather field | `moisture`/`temperature`/`light` onto tiles & exposed entities |
| — | **Scent diffusion** | the scent field | deposits + the field | spread + decay (trails, gradients) |
| — | **Reaction** | the **reactive set** (fire, water, growing things) | self + neighbour atoms (incl. virtual tile atoms) | transform events (atom-deltas, spawn, despawn) |

> **Affordance + Selection collapsed into the Planner (ODD).** The old "afford → select" pair is one marketplace-and-plan step now; see *The planner*. The bunny doc's verb-loop reads `perceive → interpret → [plan] → act → remember`.

> The membrane is the one rule in row 1a: **the raw-sense systems read only *observable* world data** — tile contents, the scent field, signal pulses — never another entity's `Memory`/`Drives`/`Plan`. Nothing stops them; they just don't, because in the model that data isn't observable. A system that did would be a *modeling bug*, not a compile error. Everything downstream of raw-sense works on the agent's own private buffers.

> **1a is also affect-blind — attention is 1b's job, not 1a's** (proto p3). Raw-sense is the *physics* of sensing: what signal reaches the agent given geometry, occlusion, range. It cannot be coloured by the agent's state — you can't will less light onto the retina. So drives/affect enter at **1b Perception**, which reads the `Affects` register and *attends*: a capacity-limited selection of the raw buffer, biased by the register (fear raises the threat channel and suppresses competitors; hunger raises food). The retina/cortex split, made literal. The register 1b reads is from **N−1** (the stagger) — last tick's feeling colours this tick's attention, and the loop stays pipelined, never intra-tick.

> **Attention is capacity-limited, and that's where tunnel vision comes from** (proto p3, P4). 1b keeps the top-K of the affect-weighted raw items. "A frightened bunny misses the food it's standing on" is *not* an intrinsic law — it's what happens when the must-attend items exceed K: with a severe bottleneck, catching a faint threat and keeping a salient food cue become mutually exclusive (no bias setting does both); with more capacity a band opens where both survive. Tunnel vision is a property of the bottleneck, not of fear per se. A consequence (P5): under tunnel vision an *available escape route* can be filtered out — the agent can't act to leave, stays exposed, and fear sustains. That's a second, **perceptual** spiral pathway, distinct from the affect-completion spiral (*What Is a Drive? → Emotion*) and a perceptual echo of blocked-escape: here the escape is present but unperceived.

> **Rows 0–7 are agent systems (the private process); Growth/Weather/Reaction are environment systems — the same machine with no process.** A system was never "an agent"; it's entity-list + logic. See *The environment*.

---

## The tick — two phases

A tick is **read then write**. Read computes; only write mutates.

```
TICK
 ├─ READ PHASE   — registries are an immutable snapshot; every system reads, none writes
 │     parallel over systems AND over each system's entity list
 │     each (system, entity) job reads self + observable-others → emits events into buffers
 │
 └─ WRITE PHASE  — drain event buffers, merge deterministically, apply to registries
       Move→Position · Bite→Body(target)+gut(self) · Thump→spawn Signal entity
       MemoryWrite→Memory · DriveDelta→Drives
```

- **Read phase is a flat fan-out.** Because each per-entity job writes only to *self's* outputs (its `Percepts`/`SubjectiveView`/event slot), jobs never collide. So `Parallel.For` over each system's list is race-free *by construction*. Two axes of parallelism: across systems, and across entities within a system.
- **Write phase is the only mutation.**

### Snapshot semantics — read N, write N+1, uniformly

Every system reads the **frozen snapshot N** and writes only into **N+1**. No exceptions: no system ever observes another system's *this-tick* output. N+1 begins as a logical copy of N (untouched components carry forward); the write phase applies events onto it; then the buffers swap. Double-buffered SoA — two arrays per registry, swap pointers at the tick boundary.

Three consequences, and they're the point:

- **Granularity is a free parallelism dial.** With no intra-tick dependencies, splitting one system into ten costs zero coordination — none of them could see each other's output this tick regardless. *More systems → more parallel work units, no ordering to reconcile.*
- **The agent loop is software-pipelined across ticks.** A percept formed at N is interpreted at N+1, afforded at N+2, selected at N+3, acted at N+4 — each stage a pipeline register handed forward one tick. End-to-end latency = pipeline depth, absorbed by tick rate. (A `thump` at N becomes a `Signal` in N+1, perceived at N+2 — emission is just another stage.) **At a 100 ms tick this is ~400 ms sense→act — sub-second, and in the range of real animal reaction latency, so the pipeline depth *is* a plausible reaction time, not an artifact to hide.**
- **Determinism collapses to write-write conflict only.** Read-phase order stops mattering entirely (everyone reads N), so the global event-ordering problem is gone. The sole remaining hazard is two events writing the *same cell* of N+1 — a per-component merge rule, not a global sort. See open issue #1.

> The cost is memory (double the component storage) and latency (pipeline depth). Both are deliberate trades for *zero intra-tick coordination* — the property that makes "everything in parallel" true rather than aspirational.

> **Signals close the loop between agents with zero special-casing.** A `thump` is an Action event; the write phase spawns a `Signal` entity (signature: alarm, channel: sound, decay). Next tick, *other* bunnies' Perception reads it like any world atom. Fear spreading through the warren is just one entity's emitted molecule perceived by the next — the bunny doc's open "social signals" question, answered by the event bus.

---

## The write phase — gather and reduce

The read phase leaves typed **event streams** (one per mutation kind: `Move`, `Bite`, `BurrowClaim`, `DriveDelta`, `MemoryWrite`…), filled in parallel. Each event names its **target cell** — the `(registry, id)` it writes. Targets fall into two classes, and only one is hard:

- **Sole-target** — the cell is the emitter's own row (`Move → Position[self]`, `DriveDelta → Drives[self]`, `MemoryWrite → Memory[self]`). At most one writer per cell *by construction*. The bulk of all events.
- **Shared-target** — a contested world cell (`Bite → Mass[carrot]`). Many entities may aim at one cell. Rare — only scarce discrete resources.

> **Continuous space keeps movement out of the contest.** With soft/continuous space (Reynolds steering — separation · alignment · cohesion · seek · flee), a Move reads neighbours in the read phase, integrates a desired velocity, and writes its *own* Position. No claim, no contention; two bunnies may briefly overlap and the next tick's separation force smooths it. So movement is *sole-target* — the gather model fires only for genuinely discrete scarce things: the last carrot, a burrow slot, a mate's attention.

Two passes:

```
WRITE PHASE   (N0 = frozen snapshot, N1 = next state)

1. SOLE-TARGET — scatter, fully parallel, no coordination
   parallel for e in soleStreams:
       applyDirect(N1, e)            // Position[e.self] = … — one writer, can't collide

2. SHARED-TARGET — gather by target cell, reduce per bucket
   for stream in sharedStreams (fixed order):
       buckets = groupBy(stream, e => e.target)
       parallel for (cell, evs) in buckets:
           if evs.Count == 1: applyDirect(N1, evs[0])
           else:              applyMerge(N1, cell, evs)
```

Pass 1 is nearly everything and just scatters — each agent writes only its own rows, so no buckets, no locks. Pass 2 buckets only the handful of shared-resource streams, and merges only the cells that drew more than one bid.

### The merge is an order-independent reducer

`applyMerge` is a pure function of the *unordered* set of competitors — so thread timing can't change the result and nothing is globally sorted:

```
applyMerge(N1, carrot, claims):                // a claim is emitted when the bite COMMITS
    winner = claims.MinBy(c => c.sourceId)      // scan for min — order-independent
    N1.Gut[winner.src] += N0.FoodValue[carrot]  // the whole carrot, gated on winning
    despawn(carrot)                             // destroyed in one go — no partial drain
    // losers: nothing
```

bunny#3 and bunny#8 both commit to `Bite{carrot#7}` → bucket size 2 → `MinBy(sourceId)` → #3 wins: the carrot is **destroyed in one resolution, #3's gut fills, #8 gets nothing.** The claim resolves at **commit**, not completion — so #8 perceives the gone carrot the very next tick and replans, losing one tick, not the whole bite duration. #3 is then **occupied** in the bite action for its ~10-tick duration (duration is commitment, not incremental effect); it replans when the action finishes. The membrane delivers #8's bad news as a percept — no "bite failed" channel.

Two properties to keep:

- **An event may carry a shared part and a private part, and the private part is gated by winning.** The bite touches `Mass[carrot]` (shared) *and* `Gut[biter]` (private), but the gut fills only if the bite landed. The merge emits the *resolved consequences*; a loser's private effects never fire. Never apply the two halves independently.
- **Determinism cost ≈ zero.** No global sort. Pass 1 scatters; pass 2 reduces each contested cell with a commutative function (`min-id` is a scan). The contested set is tiny, so keeping replay reproducible costs essentially nothing — the "ordering by id costs time" worry only applied to the scatter-with-atomic-claim model we rejected.

> **In one line:** scatter the sole-writes, gather-and-reduce the shared-writes, make every reducer order-independent.

> The Planner (below) decides *which* action an agent commits to; the Action system turns the committed action into the events this phase resolves.

### Structural changes — spawn and despawn

Not every write is an atom-delta. Entities are **created** (a doe births kits, a reaction spawns steam) and **destroyed** (a carrot is eaten, a fire burns out). These can't be cell-merges — they change the set of ids. So the write phase ends with a **structural pass** draining a spawn/despawn queue at the tick boundary:

- **Despawn** frees the id (and anything `Relations` says it `contains`).
- **Spawn** allocates a new id and seeds its components. Ids are assigned by a **deterministic counter ordered by a stable key** (e.g. the spawning event's `sourceId`), so replays mint the same ids — the one place entity creation could otherwise break determinism.

Spawn/despawn is its own pass *after* atom-merges so a being-destroyed entity isn't half-written by a stale event; events targeting a despawned id are dropped.

---

## Space — two indices

Perception, steering, and reactions all ask "what's near me?" One shared spatial layer answers, split by how often a thing moves — which is essentially the agent / non-agent line:

| Index | Holds | Why |
|---|---|---|
| **Uniform grid** | **agents** (move every tick) | O(1) cell update; a rebuild is a trivial parallel bucket-fill each tick; ideal when everything moves and density is ~uniform. Cell sized to the max interaction radius, so a query touches a 3×3 neighbourhood. |
| **Quadtree** | **non-movers / rare-movers** (vegetation, fire, water, burrows, carcasses) | adapts to clumpy density (dense thicket, empty plain); cheap *queries*, costly *rebuilds* — fine, because this set changes only on spawn/despawn/rare-move, which already pass through the structural pass. Maintained incrementally, never rebuilt wholesale. |

- A neighbour query hits **both** and unions the results: flockmates and predators from the grid, carrots and fire from the quadtree. Two cheap lookups.
- Reactions are mostly **quadtree-internal** (fire + water + grass all live there) — the reactive set ≈ the quadtree's residents.
- The index is **part of the N→N+1 state**: built from snapshot-N positions, queried in the read phase, updated at the tick boundary from N+1 (grid: rebuild, or move only the boundary-crossers; quadtree: apply spawn/despawn).

> **One determinism catch.** Steering *sums* neighbour forces, and float addition isn't associative — so neighbour order must be fixed or replays drift in the low bits (and chaos amplifies it). Fix: neighbour queries return ids **sorted**, so the sum is reproducible on a given machine. Cheap (small neighbour sets), and it's the steering analog of the write-merge's `min-id`.

---

## Fields — properties of a tile

A field is nothing fancier than **a property of a tile**. The map is a grid of tiles; a field is one value per tile. Some tile properties are static, some flow:

| | Holds | Storage | Buffering | Stepped by |
|---|---|---|---|---|
| **Static** | `material`, `hardness`, `fertility` | packed in the `Tile` struct (AoS — read together) | single, fixed after worldgen | nobody |
| **Dynamic** | `moisture`, `temperature`, `light`, `scent×channels`, `fire` | one contiguous array per channel (SoA) | **double-buffered** | `FieldSystem` |

> **Resolution is per-field.** Large-scale climate fields (`temperature`, `wind`) live at *chunk* resolution; `moisture`/`scent` need *tile* resolution. Coarse fields stay nearly free even on a 16k world — see *The world — shape and constants*.

> Both are "tile properties" — the split is storage, by access pattern (same rule as atoms). A diffusion stencil streams one field's array, so dynamic fields want their own contiguous SoA; static props always read together pack into the `Tile` struct. And you only double-buffer what changes, which by itself forces dynamic fields out of the static struct.

### Four ops, one machine

`sample` · `deposit` · `diffuse` · `decay`. Each field is config (`Scent = {diffuse: 0.2, decay: 0.05}`, `Heat = {diffuse: 0.1, decay: 0.02}`); one `FieldSystem` advances them all by one stencil — the discrete heat equation:

```
N1[t] = N0[t] + diffuse·(Σ N0[neighbours] − k·N0[t]) − decay·(N0[t] − baseline) + deposits[t]
```

A diffusion stencil **must** read old neighbours to compute a new value — it cannot work any other way. So **fields are read-N/write-N+1 in its purest form**; the engine's whole tick is just the field update generalised to the world. Parallel over tiles, deterministic, and deposits *sum* (commutative — no tie-break).

### Bytes and bools

- **Byte** (0–255) for graded fields (moisture, scent, light, heat) — a quarter of `float`'s memory, cache-friendlier. Accumulate the stencil in `int16`, clamp back to byte.
- **Bool / bitset** (1 bit/tile) for binary fields (`passable`, `water`, `on-fire`). Binary fields don't obey the heat equation — they spread by **cellular rules** (fire = a CA), not diffusion.
- **Integer math is associative → quantized fields are bit-exact across runs with no ordering care at all.** The field half of the sim is deterministic for free; only the float force-sums in steering need the sorted-neighbour trick.

### Chunks

Group tiles into **64×64 chunks** (power-of-two → shift/mask indexing), the unit of:

- **Dirty-tracking** — a chunk with no deposits and all-baseline tiles is skipped entirely. Most of the moisture/scent field is idle, so idle chunks cost nothing; a deposit reaching a chunk edge wakes its neighbour (active wavefront).
- **Streaming** — send only changed chunks to the client.
- **Parallel granularity** — one chunk, one work unit.

> Chunk interiors diffuse independently; the 1-tile **halo** at an edge reads neighbour-chunk tiles — safe, because every read is from frozen N. No locking, no ghost-copy.

### Sample → virtual atom

A field sample **is** a virtual atom (`Atom(moisture, value)`) — the atom-as-interface reads fields transparently. Agents at continuous positions **bilinear-interpolate** the 4 surrounding tiles; the **gradient** (∇, a neighbour finite-difference) is what scent-following ascends to walk a trail to its source.

> **Why sound isn't a field:** fields are for quantities that *persist and superpose* across space (scent accumulates and lingers). A thump is a one-shot expanding pulse — it stays a `Signal` entity. Persist+superpose → field; fire-once → Signal.

Bonus: **a field is a texture.** Moisture is a wetness map, scent a heatmap, light a lightmap — streaming fields to the client is streaming textures, and the scent field renders directly as a debug overlay to *see* what the bunnies smell.

---

## Senses — raw transduction before perception

The perceive step splits in two, matching biology (retina vs visual cortex):

- **Raw-sense systems** — one per modality — do the *physics of sensing*: what signal actually reaches the agent given geometry, occlusion, range, diffusion. They read the world and write a per-agent **raw sensory buffer**.
- **Perception** then reads only the agent's *own* raw buffers and does the *filtering / attention / assembly* into `Percepts`.

> This sharpens the membrane: **the raw-sense systems are the membrane-in crossing** — the only place the process reads the world. Everything after (Perception, Interpretation, Planner) works on private buffers. Sensing reads the world; perception interprets the agent's own sensations.

### Vision — a ray-march template

Vision is a **precomputed template**: a list of `Vector2` tile offsets ordered for ray marching (outward along rays, by distance). At runtime, for an agent at `P`:

```
for offset in visionTemplate:          // one contiguous list — cache-friendly
    tile = P + offset
    if rayBlocked(tile.ray): continue  // an occluder earlier on this ray shadows it
    collect index.lookup(tile) → raw vision data
```

- **Occlusion is free** — tiles arrive in ray-march order, so an opaque tile flags its ray and shadows everything behind it. No per-pair line-of-sight tests.
- The template encodes **FOV and acuity** (dense near, sparse far; only tiles within facing). Precompute one **per facing** (8–16 rotations) and pick by orientation — a lookup, not a rotation.
- **Cache-friendly and deterministic** — iterate one fixed-order list, sequential tile lookups; same template → same raw vision every run.

### Scent — a field you sniff

Scent is a **field over the tile grid** (like `moisture`), not per-scent entities:

- Entities **deposit** into it — body odour (weak, fast-decay), a chin-rub or spray (strong, slow-decay, signature-tagged), fear-scent.
- A **diffusion system** spreads and decays it each tick — giving **trails** (a mover leaves a fading wake) and **gradients** (sniff toward the source).
- The Scent raw-sense system **samples** the local field (own tile + neighbours) → raw scent data: which signatures, how strong, gradient direction.

> This supersedes "scent mark = `Signal` entity." **Scent diffuses, so it's a field; a thump is a brief expanding pulse, so sound stays a `Signal`/event.** Scent lingers and flows; sound fires once — different physics, different backing.

---

## The planner (ODD)

ODD is the **Affordance + Selection** stage as one mechanism: a per-agent, per-replan utility planner over **ads** (advertised actions, à la Sims smart-objects). It builds a small scored tree of what's possible *for this agent given its perception and state*, walks it to one committed action, and tears it down on interrupt. It handles **actions and activities uniformly** — both are ads; "activity vs action" is just non-terminal vs terminal in the tree.

### Ads, and Object Zero

> **Ad** := `(Data, Preconditions, Enables)`. `Data` is opaque except to the value function `V`. `Preconditions` gate entry to the market. `Enables` are the tree edges — what doing this makes available next (the bunny doc's activity-*chaining*, built dynamically instead of authored).

Ads come from two sources:
- **Smart objects** — perceived entities advertise ads whose preconditions match (a carrot advertises `Bite`, water advertises `Lap`).
- **Object Zero** — the agent advertising *to itself*: static fallbacks (`Idle, Wander, Panic, Rest`) that guarantee the market is never empty (**liveness**), role/shift actions, and **goal beacons** — if no perceived ad serves an active goal, inject a beacon ad pulling toward it. Beacons are how *distant* goals get pursued without deep lookahead (see *Why subtree-sum*).

### The loop

```
Collect → Score → Build (Kahn) → Propagate → Traverse (revalidate) → Interrupt → repeat
```

1. **Collect** — gather the market `C₀` = Object Zero ∪ { perceived ads whose preconditions hold }. Preconditions test the **subjective** molecule (and self-state) — so this step is also the cull (next subsection).
2. **Score** — `DirectScore(ad) = V(ad, agent) + GoalBonus(ad)`. `V` is the plug point where drives, affect, personality, memory all enter; `GoalBonus` sums active goals' match.
3. **Build** — expand the `Enables` DAG into a flat, BFS-ordered buffer by Kahn's algorithm (a node's children are contiguous; `placed` set excludes cycles/dupes). O(N). The buffer includes Enables-descendants that are **not yet afforded** (a chain's later links, whose preconditions don't hold until earlier links complete): they're there for *propagation only* — Traverse stops at the first afforded terminal and never executes them. That's how a deferred payoff deep in a chain pulls the agent toward the chain's currently-afforded entry (proto p2, G1).
4. **Propagate** — one backward pass: `parent.PropagatedScore += (child.Direct + child.Propagated) × decay`. Each node ends holding the **geometrically discounted value of its whole subtree** — an ad is worth what it directly gives *plus* discounted what it leads to. `decay` is a per-agent knob (how far it looks ahead).
5. **Traverse** — from the current position, take `argmax(Direct + Propagated)` child down to a terminal; execute it. **Before executing, revalidate its preconditions against the agent's *perceived* world** — if stale, discard and rebuild.

### Two layers: cull at the gate, score in the market

Affect and state act at **two distinct points**, and conflating them was a bug we avoided:

- **Cull (hard, at Collect).** Preconditions match the *subjective* molecule, which is coloured by state. A frightened bunny reads `safety: high` as low, so the `Binky` ad **fails its precondition and never enters the market.** Self-state clauses (`self.fear: low`) gate whole ad classes. Fear *removes* options.
- **Score (soft, in V).** Among survivors, `V` ranks by need — hunger lifts food ads, satiety lowers them. Satiety *down-weights* food; it doesn't delete it.

Different jobs: the gate decides *what's even thinkable*; `V` decides *what's chosen*.

### Commit-to-leaf, duration, structural inertia

Every action is **durational** (bite ≈ 10 ticks). On reaching a terminal, the agent **commits** to it and is *occupied* for its duration; the `Plan` buffer persists, and **Build does not re-run until the action completes or an interrupt fires.** So:

- **Replan frequency = action-completion frequency**, not per-tick. The O(N) Build amortises over ~10+ ticks. Per-tick cost is just Traverse, O(k).
- **Structural inertia** (anti-jitter): a committed plan's alternative root branches are *not* re-scored each tick. Switching requires an interrupt, not a score flicker. Binary commit/interrupt, never continuous re-evaluation. **This — not duration — is what kills jitter** (proto p2, G6): a persistent score oscillation flips the winner at *every* re-Build, so commit-to-leaf can only change the *frequency* of switching (it re-Builds at each completion), never stop it. Only inertia — not re-scoring at all between interrupts — actually halts the churn (p2: duration alone left the agent still flipping every completion; inertia dropped it to just the one real interrupt). Duration amortises Build cost; inertia is the stability mechanism.
- Per-agent buffers are independent → Build parallelises across agents in the read phase; **pool the buffers**.

### Interrupts: reflexes and perceptual staleness

A committed plan tears down for exactly two reasons, both honest:

- **Reflex** — a `CRITICAL` interrupt that *also injects an immediate action* (Freeze). It preempts the planner this tick; replan follows. This is where the bunny doc's reflexes live — off the deliberative path entirely.
- **Perceptual staleness** — revalidation fails because the agent **now perceives** the precondition broke (the carrot it was walking to is gone). Crucially this is *perceptual*, not a pushed notification: if the carrot vanishes out of sensory range, the bunny keeps walking toward the remembered one and only discovers the truth on arrival or when it comes into view. The wasted journey is the membrane being honest — the bunny doc's "memory-percepts can be stale."

### Why subtree-sum (and where beacons come in)

Propagation **sums** the discounted subtree, so a branch's pull grows with *how much* it leads to — a **breadth / optionality bias**: agents gravitate to where there's a lot to do.

> Resolved (proto p2, G7): the path-counting question dissolves because the Enables graph is a **forest, not a DAG**. Nodes are target-bound *instances* (the drive-doc pole×target rule), so a "shared sub-activity" is really separate per-target chains — `Approach(carrot)→Bite(carrot)` and `Approach(water)→Lap(water)` are distinct, not one shared `Approach`. No node has two parents, so there is nothing to double-count; count-once and separate-paths coincide. The engine enforces the forest invariant (a literal shared node is a modelling error — duplicate per target instead). A genuine "two routes to the identical instance" case would be the exception to revisit, not the rule. With `decay` this reads as "value nearby options, discount the far." It will *not* find an optimal path to a single distant prize — and it doesn't have to, because **goal beacons** handle distant goals by injecting them as *root* ads scored directly. Clean division of labour: **propagation = where's the local action; beacons = pursue that specific far thing.**

> Aggregator (resolved, proto p2 → C/G4): **sum** vs **max** subtree propagation *is* a forager-vs-optimizer temperament knob (sum: 50 nibbles outpull one prize; max: the prize wins), and it's distinct from the `{MAX,SUM}` *projection* knob over a drive's target field — same operator, two sites, keep them apart. Normalisation = **mean** (sum ÷ child-count), not a steeper divisor: it makes breadth count-immune *without* inverting the bias — when the single child is genuinely worth more, mean still picks it, but when the prize is worth less than one nibble, mean correctly picks the patch. So mean is the safe default for breadth-blindness; raw-sum is the deliberate forager setting.

---

## Pathfinding — coarse HPA, fine A*, flow fields for crowds

Three layers, matching *decide where → how to get there → move*:

- **Planner** commits a durational `Go(target)` action.
- **Pathfinding** (a service) turns target into a route.
- **Steering** (Reynolds, in the write phase) follows it as a **seek force** blended with separation/alignment/cohesion.

> The payoff of that last line: a path waypoint and a flow vector are both just *a desired direction*, the same kind of thing flocking already produces — so pathfinding output and steering compose with no glue. Pathfinding decides the goal direction; steering does the locomotion.

### Coarse: chunk connectivity, basically free

At worldgen each chunk flood-fills its own interior and records **which of its border openings are mutually reachable** — the *edge-pair flags*, a compact per-chunk byte (for the 4 borders, which pairs connect inside). Adjacent chunks connect where their shared border has aligned walkable tiles. That's the entire **coarse graph**: chunks as nodes, edge-pair flags as intra-chunk traversability, shared-border openings as inter-chunk edges.

Coarse pathing is then **bit lookups** — "can I cross this chunk from entry border to exit border?" is one flag test. A*/BFS over chunks yields a chunk route with *no chunk-level tile search at all*. Connectivity is free because it's precomputed and queried as bits.

### Fine: bounded per-chunk A*

Tile A* runs **one chunk at a time**, inside the *current* chunk only. Its goal is the **next chunk's center, used purely as a directional beacon — never reached.** The search **terminates the instant it crosses the chunk boundary**, so we never enter the next chunk this leg: its center can be unwalkable, an island, anything — irrelevant, because we don't look there. The coarse graph already guaranteed a walkable crossing exists; the center just biases *which* border tile the A* crosses at. Every tile search is bounded to ≤64×64 — cheap and predictable. The agent crosses; the next leg runs in that chunk, now beaconing on the one after. Legs are **lazy** — only the current chunk's path is materialised; reaching its end triggers the next leg, or a **repath** if the goal moved. Chunk boundaries *are* the path-length bound. A* tie-breaks by tile index → deterministic.

### Flow fields — for crowds, not individuals

When *many* agents share a goal or threat (a food patch; fleeing a fire), build **one flow field** over the region — a Dijkstra integration from the goal, each tile storing the direction down-cost. Agents sample it O(1); the build amortises across all of them. **Unique goals → HPA; shared goals → flow field.** Both emit a desired direction into steering's seek slot.

### As a service, tick + 1

Path requests are events; a `PathSystem` owns the requesters, runs coarse-bits + one bounded tile leg per request (parallel across requests), and writes a `Path` component readable next tick. An agent is briefly `WaitingForPath` — one tick, absorbed by the pipeline. This dovetails with commit-to-leaf: `Go(target)` is durational (its duration *is* the travel), the path drives it over many ticks, and perceptual revalidation (target gone) interrupts it like any action.

### The one maintenance cost

Connectivity is precomputed, but digging and fire change terrain — so a chunk's edge-pair flags go stale when its tiles change. Fix: on a terrain-modifying event inside a chunk, **recompute that one chunk's flood-fill** and refresh its flags (and rebuild any flow field crossing it). Bounded to a chunk, triggered only on change — never a global recompute.

---

## Membership is cadence and gating

"Which entities are in which system's list this tick" expresses the bunny doc's **timescale ordering** directly — no scheduler special-casing:

- **State-gated lists.** `Consolidation` owns only entities in the `Sleeping` state. That *is* "consolidation gated to rest." Falling asleep drops an entity from the active behavior lists and adds it to the consolidation list; waking reverses it.
- **Frequency-gated runs.** `Metabolism`/drive-update owns everyone but runs every N ticks; `PersonalityDrift` owns everyone but runs rarely. Perceive/interpret/act run every tick.
- **Spreading work across ticks is load-smoothing, not just timescale.** Slow systems run on staggered ticks so no single tick carries everything — cost amortizes instead of spiking. Parallelism spreads work across *space* (entities/systems); cadence spreads it across *time*. Both buy throughput.
- The architecture fixes only the **ordering** (perception ≪ drives ≪ learning ≪ personality); the **rates** are config — set the tick rate and everything scales against it. **Mood is not a rung**: emotion is a per-tick register with no clock of its own, and a lingering mood is that register re-reading a slow unresolved drive pole — it runs at the drive's clock.

> **Default anchor: 1 tick = 100 ms sim time.** Sub-second behaviors (a sniff, a hop, the ~400 ms reaction loop) span a handful of ticks; drives drift over seconds-to-minutes (tens-to-hundreds of ticks); consolidation runs over a sleep of thousands. This is the bunny doc's "pin the order, leave the clock to config" — here's the clock.

---

## The environment — reactions are the world's reflexes

Terrain, vegetation, weather, fire, water: all of it is **the same engine with the process removed.** A system was never an agent — it's entity-list + logic. So the environment needs only one genuinely new concept, and the bunny doc already named it.

### Reactions = the world's reflexes

A bunny doc *reflex* is stimulus → action, automatic, no goal, no selection. A **reaction** is that at the molecule level:

> **Reaction** := `(reactant atom-patterns + spatial relation + conditions) → transform`, where `transform` is atom-deltas, spawn, or despawn. It fires automatically when matched — no market, no `V`, no choosing. The environment's reflex.

It reuses everything already built: precondition matching (atom patterns, incl. virtual tile atoms), the spatial index (neighbour queries), the two-phase tick (match in read, transform in write), and gather-merge (competing transforms on one molecule resolve by priority). The examples drop straight in:

- **fire + fuel → heat + ash** — a fire consumes local fuel and pumps **heat** into the temperature field.
- **water + fire** — water raises `moisture` and pulls heat, dropping the fire below sustain; the heat boils the water to a brief `steam` puff (it disperses — there's no vertical in 2D).
- **fire spreads** — *emergent, not a rule*: heat diffuses, and a dry flammable neighbour ignites when its temperature crosses the threshold; wet ground is a firebreak.

> These are illustrative — the real mechanism is **field-mediated**; see *Reactions — physics by local rule*.

### Atoms are the coupling substrate

`moisture` is written by **Weather** (rain), read by the **fire reaction** (wet won't burn), and read by the **digging affordance** (soft damp earth digs well). One atom, three unrelated subsystems, agents and environment alike. Nobody wires weather→fire→digging; they *meet at the atom*. That coupling is the whole reason the atom layer exists — the bunny doc's "one activity's written atom is another's read atom," now spanning the entire world.

### Fields vs entities

The one representation call: continuous, everywhere quantities → **fields**; discrete, locatable things → **entities**.

| | Backing | Examples |
|---|---|---|
| **Field** | `Tile[]` struct grid + per-channel field arrays; virtual atoms on query | terrain (`material`/`hardness`/`fertility`), climate (`moisture`/`temperature`/`light`/`wind`) |
| **Entity** | a molecule with an id | a plant, a fire, a puddle, a steam cloud |

- **Terrain** — a tile field. Weather writes onto it; reactions & affordances read virtual atoms from it.
- **Vegetation** — entities. `Growth` raises `mass` over ticks; a seeding *reaction* spawns seedlings on fertile neighbours; agents eat it (Bite → despawn).
- **Weather** — one field/ambient system writing `moisture`/`temperature`/`light` outward. Its **day/night** term drives the crepuscular cycle and gates `Sleeping` (→ which gates Consolidation).

### What it costs

Almost nothing new: spawn/despawn is already a write-phase pass (above). Two small rules: **reactive-set membership** (only fire/water/growing things enter reaction systems — a rock is inert and free), and **pairwise dedup** (a two-molecule reaction is attributed to `min-id` so it fires once, not once per direction).

---

## Reactions — physics by local rule

Reactions are the world's reflexes (*The environment*), and together they **are** the physics engine. There is no special-purpose fire code or water code: **physics is data** — a table of local rules over the reactive set and the fields. A new phenomenon (rot, rust, freezing) is a new *rule*, not new code.

### The rule

```
Reaction {
    reactants : AtomPattern[]               // who — patterns the participants must match
    relation  : SameTile | Adjacent | WithinRange(r)
    when      : FieldCondition[]            // thresholds on local fields (temperature, moisture…)
    products  : Transform[]                 // atom-deltas · spawn · despawn
    priority  : int                         // conflict order
}
```

Rules live in the frozen `Reactions` table. A `ReactionSystem` matches them in the read phase and emits transforms as events; the write phase applies them. Two trigger modes:

- **Entity-driven** — iterate the reactive set; for each, query neighbours (quadtree) + sample local fields, test rules (a fire looks for fuel and flammable neighbours).
- **Field-threshold** — a tile crossing a threshold fires (`flammable` + `temperature > ignite` + `moisture < dry` → spawn fire). Gated to **active field chunks** — heat only exists near fires — so it's tested on a thin region, never all 268 M tiles.

### Field-mediation — the elegant default

Most physics shouldn't be a direct per-pair rule with a spread-probability; it should be **mediated through fields**, so behaviour *emerges*:

> **Fire, end to end, with no spread rule.** A fire deposits **heat** into the temperature field and consumes local **fuel** each tick. Heat **diffuses** (the field stencil). A flammable neighbour ignites when its temperature crosses the threshold — but only after the heat first boils off its **moisture** (wet resists). The fire dies when fuel runs out *or* temperature drops below sustain. **Water** needs no "quench" rule: it raises moisture and pulls heat, dropping the fire below sustain, and the heat boils the water to a brief steam puff. Spread is gradual because heat takes ticks to build and diffuse; wet ground forms natural **firebreaks**; **wind** (which advects heat and moisture) bends the fire downwind. *All of that falls out of three fields and two thresholds — no fire-spreading code.*

This is "atoms are the coupling substrate" as physics: fire ↔ `temperature` ↔ `moisture` ↔ `fuel` meet only at the fields. Direct-contact reactions (`ice + heat → water`; `carcass → rot → fertility`) handle the discrete chemistry fields can't express.

### Conflict & priority

Transforms hitting one molecule gather like any shared write (*The write phase*) and resolve by **kind**:

- **Additive** (deposit heat, reduce fuel by N) — **sum**, commutative, order-independent. Three fires heating one tile just add.
- **Exclusive** (despawn, phase-change) — **highest `priority` wins**, ties by `min-id`; if it consumes the target, lower transforms drop. Quench (despawn, high priority) beats a spread contribution.

Field-mediation keeps this surface tiny: most interactions resolve through field thresholds, never rule-vs-rule, so priority only arbitrates genuine direct-contact collisions.

### Determinism & scale

- **Deterministic.** Thresholds are exact; the rare *stochastic* rule (a decay chance) draws from `hash(tick, participantIds, ruleId)` — order-independent and replay-exact, never a global RNG.
- **Scales by activity.** Gated by the **reactive set** (small) and **active field chunks** (heat/moisture exist only where something's happening). The inert majority of the world costs nothing — physics runs only where there's physics.

---

## Worldgen — geology and water only

Worldgen is a deterministic `f(seed)` that lays down **geology and water — and only that.** It does *not* paint biomes; biomes emerge later from climate + growth (see *Climate*). Because a tile is a pure function of `(seed, x, y)`, worldgen is **chunk-lazy** (generate a chunk on demand) and embarrassingly parallel — the same grain as the rest of the engine.

1. **Geology** — cylinder-sampled, domain-warped noise → tile `material` (rock / soil / sand) + `fertility`. No elevation.
2. **Water** — lakes as low-frequency noise blobs; rivers as thin carved channels (warped ridged-noise, or meandering walks between lakes and the Y edges). Water tiles take the `water` material and become **moisture sources** — *sources*, not simulated currents.
3. **Seed life** — scatter a *sparse* initial vegetation set (and starting agents) by current moisture suitability. Most vegetation arrives later by spreading, not here.
4. **Chunk connectivity** — flood-fill each chunk → the pathfinding **edge-pair flags**.
5. **Field init** — seed `moisture` (high near water), `temperature` (latitude baseline), `wind` (noise). The running Field/Weather systems take over from here.

Steps 1–3 are pure noise/scatter; 4–5 are the bridges to pathfinding and the climate.

---

## Climate — temperature, wind, moisture, emergent biomes

The world isn't painted with biomes — it **grows** them. Worldgen lays geology + water; the climate systems run, and biomes fall out as an *emergent read* of the result.

- **Temperature** — sun-driven and simple: warm at the equator (mid-Y), cold at the poles (Y edges), the whole gradient **scaled by a seasonal oscillation**. Weather writes it as the temperature field's coarse (chunk-resolution) baseline; fire and shade perturb it locally; diffusion smooths. *(Optional richness: let the season shift the warm band north/south for hemispheric seasons.)*
- **Wind** — a slowly-evolving noise field gives **direction + strength** per chunk. Large-scale, so chunk resolution is plenty, and evolving it slowly makes weather *change*.
- **Moisture** — sourced at water, **advected by wind** (each chunk pushes moisture downwind — wet plumes trail off lakes, dry rain-shadows form away from water), spread by diffusion, evaporated faster where hot. This is the chunk-level moisture transfer the wind drives.
- **Biomes = an emergent read.** `biome(place) = f(local temperature, moisture, vegetation)`, derived on demand, never stored: hot+wet+green → jungle; cold+dry → tundra; temperate+wet → meadow/forest; hot+dry → desert. Vegetation grows where temp+moisture suit it (Growth + seeding reactions test the local fields), so over sim-time green spreads into the suitable regions and the biomes *appear*.

> The project's whole spirit, applied to terrain: **seed the substrate, let the rules grow the structure.** No stored danger, no stored emotion, no stored biome — all read off the running state.

---

## Open issues (to nail before code)

**Resolved this pass:**

- ~~Write-write conflict~~ → gather-and-reduce, `min-id` + clamp (*The write phase*).
- ~~Reflex ordering~~ → a `CRITICAL` interrupt that also injects an immediate action; off the deliberative path (*The planner → Interrupts*).
- ~~Atom value union / `IBehavior` recursion~~ → atoms are a read-interface over three backings (*Atoms are a read-interface*); verbs are ads in the ODD tree, no separate behavior type (*The planner*).
- ~~Spatial index~~ → grid for agents, quadtree for the static set, queries union both, neighbours returned id-sorted for replay (*Space — two indices*).
- ~~Reaction priority~~ → additive transforms sum, exclusive resolve by `priority` then `min-id`; field-mediation keeps the conflict surface tiny (*Reactions → Conflict & priority*).

**Architectural — surfaced reconciling this doc against the bunny docs (these block code, unlike the content items below):**

A1. **The private-write invariant — "sole-target" is not single-writer.** Pass 1 of *The write phase* scatters sole-target writes "fully parallel, no coordination… at most one writer per cell by construction." That holds per *emitter row* but **not across systems**: `Metabolism` and a landed `Feed` both write `Drives[self]`; a `Reflex` (`velocity = 0`) and `Steering` both write `Motion[self]`. Two systems, one N+1 cell, lock-free scatter → race. The model is only sound if **every private write is a commutative delta, gathered and reduced exactly like a shared cell** — which is *why* they're named `DriveDelta`/`MemoryWrite`, not `Set`. But it **breaks for absolute writes**: Reflex's `velocity = 0` and steering's integrated velocity don't sum. Resolution to pin: private cells flow through the same gather-reduce as shared cells; **additive** atoms sum, **absolute** atoms (a stop, a phase-change) resolve by per-component `priority` then `min-id`. Pass 1 cannot be the coordination-free scatter the doc describes — it's the *common case* of the same reducer, not a separate path.
>
> **Gap surfaced by proto p3 (P3): additive *and* absolute writes to the same cell in one tick is still undefined.** A1 covers additive-sum and absolute-priority *separately*, but the safety pole now takes both — an absolute percept-tracking pull (Interpretation, *Senses → Attention*) and, potentially, an additive spike (a reflex, a fear-scent contribution) in the same tick. Proposed resolution to pin before code: **apply the absolute write first (assign toward its target), then the additive increments on top** — absolute = assignment, additive = increment, in that order. Owed, untested.

A2. **Entity handles must be generational, not bare ids.** Spawn allocates an id, despawn frees it (*Structural changes*) — and id reuse then silently re-points every `entityRef` atom and every `Memory` dossier keyed to the dead entity at whatever now occupies the slot. An `EntityId` must be **`{index, generation}`**; a ref dereferences only when generations match, else reads as *gone* — which the planner already absorbs as perceptual staleness. At 10⁴ agents the array cost of *never* reusing is negligible, so this is a **correctness** decision, not a perf one — but it must be made before any `entityRef` is stored in an atom or a memory.

A3. **The run loop — a 100 ms real-time budget needs an overrun policy.** 100 ms is a hard wall-clock ceiling now, not just the sim-time unit, and the engine owes the loop that enforces it: a fixed-timestep driver, plus a **defined behaviour when a tick exceeds budget** — even if rare. Determinism forbids silently dropping or skipping a tick (replay depends on every tick running), so the honest options are *let sim-time lag real-time* (best-effort fallback) or *hard-cap and flag the overrun*. Plus pause and a speed multiplier the observer renderer will want. None of this is in the doc.

A4. **Persistence & replay — invoked as a guarantee, never designed.** "The replay guarantee" / "reproducible worlds" appear repeatedly with no mechanism. Decide: **full-state snapshot** (every registry + `Memory` + field arrays, double-buffer-aware) vs. **seed + input-log re-simulation**. A pure observer sim has no player input, so it replays from **seed alone** — *iff* every stochastic draw is deterministic (A5) and float-sum order is fixed. **Caveat to state outright:** the neighbour-sorted float sums (*Space*) are reproducible *on one machine*; replay is **not portable across machines** without fixed-point. Fine for single-host; a hard constraint the moment a replay is shared or a client must agree with the server.

A5. **Deterministic randomness for agent actions.** Reactions already draw from `hash(tick, participantIds, ruleId)` — order-independent, replay-exact. Agent actions with randomness — `Wander`'s biased walk, `Binky-Escape`'s random direction — have **no specified source** and would leak nondeterminism straight into replay. Same fix, stated once as the rule: any agent draw is `hash(tick, selfId, actionId, …)`, never a shared RNG stream.

> **Also unaddressed, but not blocking first code:** **(i) Event-buffer architecture** — the typed event streams are "filled in parallel" with no spec for allocation, per-thread arenas, sizing, or the merge pass that drains them; a pooling design, not new architecture, but owed. **(ii) Bounded storage over a long sim** — a breeding population grows the entity set *and* every agent's `Memory` (spatial maps, dossiers, episodic) *and* its bag of acquired drive poles monotonically, and with everything always live nothing goes dormant to amortize it. `Decay`/`Consolidation` must eventually be a real bounded-storage mechanism, not only the conceptual one the bunny doc sketches — later, but real.

**Still open:**

1. **Per-resource merge rules (content, not architecture).** The mechanism is fixed; each scarce resource still needs its reducer written: burrow = capacity-N claim, mate = mutual-selection (not first-come), etc.
2. ~~**The aggregator knob.**~~ Resolved (proto p2): sum (forager) vs max (optimizer) subtree propagation is a personality parameter; normalisation = mean (count-immune, doesn't invert). See *The planner → Why subtree-sum*. *(Remaining content: the actual per-personality default values — authoring, not architecture.)*
3. **Scent field granularity.** Scent is a field (*Senses → Scent*). Open: a few fixed channels (predator / self / other / food / fear) — cheap, loses individual identity — vs per-signature richness that bloats the field. Likely channels for ambient + a dominant-signature tag per cell for recognition.
4. **The memory write-side.** How `MemoryWrite` events encode deltas-vs-category, and the consolidation mechanics, are domain content the bunny doc sketches but the engine hasn't pinned.
5. **Reaction table content.** The mechanism is fixed; the actual rules (combustion, freezing, rot, evaporation thresholds) are content to author.
6. **Pathfinding leftovers.** Flow-field lifecycle (when a shared-goal field is built, cached, and evicted), and border-opening granularity (treat a chunk border as one opening, or subdivide when a wall splits it into two gaps).

**Deferred by choice:**

- **`V`, the value function.** The heart of selection — how drives, affect, and personality score an ad. The grounding it was held back for now exists: **What Is a Drive?** (Damasio's somatic markers, Barrett's constructed emotion, and the vocabulary it adopts) fixes the shape — `V` scores **(pole-urgency × target-affordance)** pairs joined on the ad's target, and the felt residual does its fast pruning at the existing *cull* step, not as a third layer. `V` is a plug point in ODD, so the engine is complete without it; the formula itself — the actual score-fields — is still a separate pass.

> The spine and the substrate are committed, and **scale is not the worry** — at 10³–10⁴ agents in a 268M-tile world, full every-tick simulation with no LOD fits the 100 ms budget with room to spare; agents are sparse dots, so lazy chunks and dirty fields stay correct. What's left splits two ways: the **content** items above (reducers, reaction rules, scent channels, memory encoding) and the **deferred `V`** are genuinely just authoring — but the **architectural** items (A1–A5) are not. The earlier claim that "no architecture is left to decide" was wrong: the write-cell invariant, generational handles, the run loop, persistence/replay, and deterministic action-randomness are all engine decisions the doc had silently assumed rather than made. Pin A1–A5 before code; everything else can grow in.
