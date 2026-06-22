# The Threshold — Shared Activities, Queues & Percept-Driven Preemption

**Status:** Design (approved for spec)
**Date:** 2026-06-22
**Milestone:** The Threshold (follows The Renderer / R0–R5)

---

## One-line

Both a door and a shop counter are *thresholds you cross only when it's your turn*. This
milestone makes agents respect contended, shared affordances — turning solo parallel lives into
genuine multi-agent coordination — built on two interlocking pillars: a generic **shared-activity
instance** model and a **percept-driven preemption** loop.

## Why

Today every agent lives a solo life in parallel:

- N agents conduct `Buy` at one shop *simultaneously*; all pay, all get relief. No counter, no line,
  no back-pressure.
- Buildings are points. There is no door to operate, no chokepoint to take turns at. `ActivityKind.Open`
  and `Close` exist in the enum but have **no executors**.
- There is no queue, wait, or mutex anywhere. `OccupancyRegistry` tracks occupants but only feeds
  scoring; it never blocks.
- Agents cannot abandon a commitment when something more urgent shows up, except via the narrow,
  bespoke `Flee`/`Chat` interrupt paths.

The unifying theme: **agents interacting with constrained shared affordances** — an affordance you
must wait for, or take a turn at. Doors and shopkeepers are two instances of the same mechanic.

This also produces an emergent signal the current sim cannot: when shop demand exceeds a keeper's
throughput, lines form and agents balk — stress-testing the food-market loop from the ODD work.

## Scope decisions (from brainstorming)

| Decision | Choice |
| --- | --- |
| Milestone theme | Constrained shared affordances — doors and shopkeepers as **one mechanic** |
| Contention model | **Real ordered queue** (visible line, advance one at a time) |
| Door meaning | **Both** wall gates *and* building-entry doors (same primitive) |
| Building interiors | **Not now** — agent conducts activity at the entrance; interior position is a future hook |
| Balking | Agents **can leave** a queue; via the general preemption loop (below), not bespoke balk logic |
| Primitive | **Shared-activity instances**, not a queue-only registry (a queue is the degenerate case) |
| Build vs design-for | **Build the generic runtime, implement one protocol** (`ServiceQueue`) |
| Interrupts | **Percepts are the only input**; an interrupt is "whatever is more important than what I'm doing now" |
| Packaging | **One milestone**, percept-driven preemption as **Phase 0** |
| Build order | Plan B — vertical slice on the shopkeeper first, then generalize to doors |

## Non-goals (designed-for, not built)

- **Bartering** — `Buy` later becomes a `Barter` protocol (skill rolls + comms-substrate dialog,
  price converges over rounds). The seam is the single service call-site; not implemented now.
- **Combat** — later a `Conflict` protocol (participants with initiative, shared HP/morale; `Flee` =
  `Leave`). The participant/role model accommodates it; not implemented now.
- **Interiors** — no walkable interior geometry, no interior agent positions. Building-entry doors gate
  and queue; the agent still conducts its activity at the entrance.
- **Locks** — `Lockpick`/`Bash` stay reserved for a future thieving milestone.
- **Hard hunger consequences** — agents don't die of hunger yet, so somatic urgency has no calibration
  target. Thresholds are present but cosmetic until a future "consequences" milestone gives them teeth;
  promoting somatic urgency to forced preemption is a natural follow-up then.
- **Multi-counter tuning** — shop service capacity ships at 1; per-building tuning is future.

---

## The two pillars

### Pillar A — Shared activities as instances

The real primitive is **not** a queue. A queue is the degenerate protocol where the "interaction" is
*waiting your turn*. Building a queue-only registry would down-sample a rich thing (multi-agent
interaction) to fit the one consumer in front of us — the "never mangle to fit a pipe" anti-pattern.

Two layers, cleanly split:

- **Static (exists, unchanged):** `ActivityCatalog` / `BehaviorRegistry` — the *spec* of an activity
  (duration, promised deltas, preconditions).
- **New runtime:** a **live instance** that exists only while ≥1 agent participates.

A **`SharedActivityInstance`** holds:

- `kind` / **protocol** — `ServiceQueue` now; `Barter`, `Combat`, `Conversation` are future protocols.
- `anchor` — the affordance it is pinned to: a `(building, verb)` for a shop, a door/gate cell for a
  door (or an agent-pair for future combat).
- `participants` — a list of `{ agentId, role, joinTick, localPhase }`. Roles: `Waiter` / `Served`
  now; later `Buyer`/`Keeper`, `Attacker`/`Defender`.
- `phase` — `Forming → Active → Resolving → Closed`.
- `protocolState` — **opaque per kind**. `ServiceQueue`: ordered participant list + count of busy
  service slots. Future: barter rounds + skill-roll accumulators; combat initiative + HP deltas.

**`SharedActivityRegistry`** holds live instances, indexed by anchor, so an arriving agent does
`FindOrCreate(anchor, kind)` → `Join(role)`. Unlike the existing per-building scalar registries
(`Occupancy`, `Stock`, `Larder`), this holds **instances with internal state**, and reaps them when
empty.

**`SharedActivitySystem`** ticks each live instance's protocol: promoting/serving participants,
applying per-turn effects, and advancing phase. The **instance owns the shared truth** (whose turn,
converging price, combat HP); the agent owns its private state.

#### The `Queued` phase

Today `BehaviorData` runs `Moving → Doing`. We insert one phase:

```
Moving  (walk to the affordance)
  → Queued  (arrived; hold for your turn)
    → Doing  (being served; serviceTime counts down)
      → exit  (Close the door / leave the counter; instance Releases the slot)
```

The agent's `Activity` (its intent, chosen by ODD) is **untouched** — it still "wants `Buy`". We add:

- The `Queued` phase value.
- A queue-slot **position** the agent walks to (anchor position + ordinal × spacing along an approach
  vector), so the line is *physical and visible*, not overlapping bodies.

Generic protocol operations (called by the systems):

- `Join` — append participant with a role; return position. Called on arrival (replaces the old
  unconditional flip to `Doing`).
- `HeadReady` — is this participant at the front *and* a service slot free? → promote `Queued → Doing`.
- `Release` — participant finishes service; free the slot; advance the line (re-target waiters' slots).
- `Leave` — participant balks/preempts; remove from any position; close the gap. Wired to Pillar B.

**Flip point moves:** today `ArrivedAtTargetEvent` flips `Moving → Doing` unconditionally. Now arrival
calls `Join`; `SharedActivitySystem` promotes ready heads to `Doing` and shuffles the line forward.

### Pillar B — Percept-driven preemption

**Interrupts are not a mechanism — they are an outcome of the perception loop.** Percepts are the only
input. The memory work already widened *external* percepts (perceivable-atoms: entities natively are a
bag of perceivable atoms). This pillar adds the inward half and the loop that consumes it.

- **Somatic percepts** — the agent perceives *its own body* (hunger, fatigue, pain) as atoms in the
  same percept bag. This is perceivable-atoms applied inward: the body is just more perceivable atoms.
- **Interrupt = revaluation.** Each perception tick, the agent values the best available action (given
  current percepts) against its **current commitment**; if something outranks it (by a margin, to
  avoid thrash), it switches. There is no `Flee`-interrupt, no `Chat`-interrupt, no balk-logic — all
  three become the same thing: a percept (threat / social / somatic) outscoring the current activity.
- **ODD generalized** from "decide at decision points (idle / arrival / completion)" to
  "continuously re-valuable from percepts." A `Queued`/`Doing` agent is therefore **preemptible by
  construction** — no queue-specific balk code. Preemption from a shared activity calls the protocol's
  `Leave`.
- **Sleep is the one carve-out.** A sleeping agent *suppresses* percept processing — it does not run
  the normal valuation loop. Only **high-salience physical** percepts (being hit, a loud noise) bypass
  the gate and force wakeup. Sleep is a percept-gated state, not a normally-preemptible activity.

> **Balking from a queue is just hunger-the-percept beating wait-in-line-the-commitment.** No special
> case — it falls straight out of Pillar B.

---

## How the two affordance cases hook in

A subtlety worth stating: shops and doors share the `ServiceQueue` protocol but **trigger at different
moments**.

### Shop — a *destination* affordance

ODD chooses `Buy`; the agent walks there; **arrival** triggers join-queue.

- The shop building (per its `Keeper`) is a `ServiceQueue` anchor.
- `capacity` = customers served at once — **starts at 1** (one counter; visible and honest; tunable
  later).
- `serviceTime` = the `Buy` activity's existing 30-game-minute duration.
- Arrival flips to `Queued`, not `Doing`. `SharedActivitySystem` promotes the head to `Doing` when the
  counter frees.
- **Transaction logic is unchanged.** `EconomySystem.PaySale()` still runs during `Doing` — but now
  only `capacity` agents are in `Doing` at once, so coin/stock transfer is naturally serialized. The
  queue gates *who* is in `Doing`; the money path runs unchanged inside it. **This single service
  call-site is the seam `Barter` later replaces.**

### Door — a *transit* affordance

Nobody decides "I want to use a door"; it is an obstacle *en route*. So the trigger lives in
`MovementSystem`, not ODD.

Two kinds, same protocol:

- **Wall gate** — existing block-granular gate cells (subrecord 446 on `WALL` blocks); already passable
  in pathing, guards already route through them.
- **Building-entry door** — the entrance cell of a shop/home.

Mechanic (both): a door cell is a `ServiceQueue` anchor, `capacity 1`, ~instant `serviceTime`. When
`MovementSystem` advances an agent onto a door cell:

1. Pause the walk; `FindOrCreate` + `Join` the door's queue (local phase → `Queued`).
2. When head-of-line, play `Open` (the reserved enum slot finally gets an executor); step through.
3. Door re-closes behind the line per policy.

**Pathing:** doors stay *passable* in the pathfinder (routes still go through them); gating happens at
the **movement layer** (you take your turn to cross). No pathfinder rewrite.

**Close policy (minimal):** `Open` to traverse; `Close` is used by (a) leaving a building and (b) the
existing **night curfew** closing gates — ties directly into guards-as-gatekeepers. Locks are out of
scope.

**Congestion** at a 1-wide chokepoint now produces a real line — same `SharedActivitySystem`, same
visible slots as the shop.

---

## Viewer (town3d)

The sim is authoritative; the renderer draws what the sim reports. The viewer already renders agents by
position and ignores activity detail, so this is additive — and barter/combat later need **zero**
renderer rework beyond possibly new phase hints.

- **Queue lines render for free.** Participants in `Queued` walk to / stand at real sim slot positions,
  so the line emerges from existing position streaming — bodies stand in a row and shuffle forward.
- **One new wire field:** a compact `phase`/`activity` hint (Moving / Queued / Doing / Sleeping) so the
  viewer can tint/label a queued agent vs. an active one.
- **Door state:** a door cell exposes open/closed so the viewer can swap the gate mesh (already renders
  gate model 446) — open vs. closed, optionally an agent mid-`Open`.
- **Inspector:** add a line or two — agent: "Queued at \<shop\>, position 3/5"; affordance: occupants +
  queue length. Makes contention debuggable for tuning capacity/serviceTime.
- **Parity:** the baseline (`parity/baseline/local-r2-base.png`) shifts once queues change placement —
  **re-baseline** as part of the milestone, don't fight it.

---

## Build phasing

Plan B order — prove the vertical slice before generalizing.

1. **Phase 0 — Percept-driven preemption (foundation).** Somatic percepts into the percept bag;
   interrupt-as-revaluation in the decision loop (best-available vs. current commitment, with an
   anti-thrash margin); ODD generalized to continuously re-valuable; sleep percept-gating (suppress
   except hit / loud noise → force wakeup).
2. **Phase 1 — Generic shared-activity runtime.** `SharedActivityInstance`, `SharedActivityRegistry`,
   `SharedActivitySystem`; the `Queued` phase on `BehaviorData`; `Join`/`HeadReady`/`Release`/`Leave`,
   with `Leave` wired to Phase 0 preemption.
3. **Phase 2 — `ServiceQueue` on the shop (vertical slice).** Shop anchor (capacity 1, serviceTime =
   Buy's 30 min); arrival → `Join`; serialize `Doing` so `PaySale` runs unchanged for one customer at a
   time; viewer phase hint + queue line + inspector lines; re-baseline parity. Prove end-to-end.
4. **Phase 3 — `ServiceQueue` on doors.** `Open`/`Close` executors; movement-time door trigger; wall
   gates + building-entry doors; curfew `Close`; door mesh open/closed state.

## Seams designed-for (built now, not implemented)

- `protocolState` is opaque per kind → `Barter`/`Combat` add their own without touching the runtime.
- The shop's single service call-site (`PaySale` during `Doing`) is what `Barter` replaces
  (negotiation rounds + comms-substrate dialog).
- `participants[].role` already carries roles → combat's attacker/defender fit.
- `Leave` is per-protocol → barter forfeiture / combat flee reuse the same hook.
- Somatic percepts already flow → when hunger gains consequences, promoting its urgency to forced
  preemption is a threshold change, not new plumbing.
- Building-entry doors admit at the entrance → when interiors land, "pick an interior position" slots in
  at the admit step.

## Testing

The Sim test suite is stale / being rewritten — **new tests only; do not trust the old pass/fail.**

**Queue invariants (Pillar A):**

- FIFO ordering of `Waiter` promotion.
- Capacity gating: never more than `capacity` participants in `Doing`.
- `Leave` from any position closes the gap and re-targets following waiters' slots.
- `FindOrCreate` is idempotent per anchor (one instance per anchor+kind).
- An emptied instance is reaped (`Forming → … → Closed`); no leaked instances.
- No agent in two instances at once.

**Preemption invariants (Pillar B):**

- A somatic percept (hunger) that outranks the current commitment causes a switch (and, if `Queued`, a
  `Leave`).
- Anti-thrash: a marginally-better action does **not** cause oscillation.
- A sleeping agent ignores low-salience percepts and wakes on a hit / loud-noise percept.

**Deadlock guard:** assert no agent stuck `Queued` indefinitely — the preemption loop is the backstop
(an agent always eventually re-values and either advances or leaves).

**Soak (honesty-histogram method):** run a town/region soak; histogram what everyone *does* across a
day. Expect: lines appear at popular shops; balk counts > 0; single-counter shops show throughput
back-pressure. Watch the known failure modes — no deadlocks (everyone served or balks), no
starvation-by-queue, no economy collapse from throttled `Buy`. Verify via the existing
`Sim.Host --soak` path.

---

## Key files (extend, don't replace)

| Concern | File |
| --- | --- |
| Activity/Phase data | `Assets/Sim/Registries/BehaviorRegistry.cs` (`ActivityKind`, add `Queued` phase) |
| Activity specs | `Assets/Sim/Systems/ActivityCatalog.cs` |
| Action sequences | `Assets/Sim/Systems/ActionCatalog.cs` (give `Open`/`Close` executors) |
| Ads / affordances | `Assets/Sim/Systems/Affordance.cs` |
| ODD decision loop | `Assets/Sim/Engine/Units/OddSystem.cs` (generalize to continuous revaluation) |
| ODD tree | `Assets/Sim/Systems/OddTree.cs` |
| Movement (door trigger) | `Assets/Sim/Engine/Units/MovementSystem.cs` |
| Economy / transaction | `Assets/Sim/Engine/Units/EconomySystem.cs` (`PaySale` stays; serialized by queue) |
| Occupancy (reference pattern) | (existing `OccupancyRegistry`) |
| **New** — shared-activity instance/registry/system | `Assets/Sim/...` (Pillar A) |
| **New** — somatic percepts + preemption | percept/perception path widened by the memory work (Pillar B) |
| Viewer | `Headless/Sim.Web/wwwroot/town3d.html` + client modules; one wire field + door mesh + inspector |

## Open questions for the implementation plan

- Exact anti-thrash margin / hysteresis for revaluation (tune in soak).
- Perception-tick cadence for re-valuation while `Queued` (every tick vs. every K ticks).
- Queue-slot geometry: approach vector and spacing per anchor type (door chokepoint vs. shop frontage).
- Whether `Close` behind every passer or only on curfew/exit (start: curfew + building-exit only).
