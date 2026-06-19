# Dynamic Object Zero + guards-as-gatekeepers — recorded design (future pass)

**Date:** 2026-06-19
**Status:** Active — this is the milestone now being built. §6 open questions are resolved (see §6, rewritten as decisions). Walls render and gate cells are recoverable at runtime, so the dependency is satisfied.
**Depends on:** walls being rendered (`2026-06-19-town-walls-render-design.md`) — done first so there is a visible wall + gate to defend.

This is the next milestone after walls. It has two halves that only become meaningful together: monsters that respect walls and hunt through gates, and guards that emergently hold those gates. Both are built on a general ODD extension (**Dynamic Object Zero**), of which guards and hunting monsters are the *first consumers* — the mechanism is meant to serve the whole sim, not just guards.

> User note: *"the ODD extension will help rest of sim too, not just guards"* and *"monsters deserve to be agents too… that's a ton of work, pushed to a far-future milestone. For now monsters spawn outside when they get hungry, pick up the nearest civilian in the nearest settlement, and path to eat it."* Guards: *"post / react / patrol — all of those,"* to emerge from Dynamic Object Zero rather than a state machine.

---

## 1. The ODD reality (grounded in code)

The decision pipeline lives in `OddSystem.Decide` (`Assets/Sim/Engine/Units/OddSystem.cs`), backed by `OddTree` (`Assets/Sim/Systems/OddTree.cs`), `ActivityCatalog` / `OddScore` (`Assets/Sim/Systems/ActivityCatalog.cs`), and `DriveCatalog` (`Assets/Sim/Systems/DriveCatalog.cs`).

Per-tick, per agent:
1. **Gather ads** (`GatherAds`, ~`OddSystem.cs:290-348`): `Ad` objects from innate verbs (Idle, Wander, Flee, Attack), job-at-workplace, known buildings (PlaceMemory), and carried items; filtered by preconditions + stock.
2. **Consolidate** (~`:180-199`): keep the best-scoring ad per `ActivityKind` (incumbent stickiness ×1.4).
3. **Gate** (~`:201-211`): coin-sink and larder gates cull unaffordable / unsupplied actions.
4. **Build / Propagate / Traverse the OddTree** (~`:213-219`): expand each action's Enables-DAG, backward-propagate discounted subtree value (`LookaheadDecay=0.6`), pick the best root path.
5. **Publish** `IntentSetIntent` (~`:270-283`).

**Scoring is gap-closure, not multiplication.** `OddScore.Compute` (~`ActivityCatalog.cs:458-474`):

```
gap   = sqrt(L2 need-deficit now) - sqrt(L2 need-deficit after the activity's Δ)
score = max(0, gap) * timeGate + baseUtility
```

This differs from the idealized doc's `DirectScore = Effect × Need`, but is functionally the same idea: need-weighted improvement. An action that promises **no** need relief (Δ = 0) scores purely on `baseUtility` (+ a duty `baseGate` multiplier). That is exactly how Patrol/StandWatch will live on the table.

**"Object Zero" today** = the always-present innate verbs (Idle `baseUtility 0.002`, Wander `0.004`, plus fear-gated Flee/Attack) and the `baseUtility` backstop that keeps Idle > 0 when nothing else wins (`Affordance.cs:81`).

**Needs** (`NeedsRegistry.cs:11-21`): Hunger, EnergyDef, SocialDef, CoinDef (retired to 0 weight), GoodsDef, Fear, Attire. **Prepotency gates** (`DriveCatalog.cs:69-161`) hard-cull leisure when Hunger/Energy/Fear are high; Hunger↣Fear is `DesperationGraded` (grades down, never culls).

**Guards** are identified at runtime as `IsGuard = employment.PublicOwner != None` (`OddSystem.cs:175`); seeded in `TownLoader.SeedGuards` (`TownLoader.cs:197-218`), tied to a settlement via its treasury `OwnerId`, with combat targeting `NearestCreature` already wired (`OddSystem.cs:252-258`). Count policy: City 1/50 residents (min 2), Hamlet 1/100, else 0.

---

## 2. Dynamic Object Zero (the general mechanism)

Make Object Zero **context/role-aware**: an injectable provider that, given the agent's context (role, shift, sensed world), contributes extra `Ad`s into the marketplace *before* tree build. The seam is `OddSystem.Decide` right after `GatherAds` and before consolidation (~`:178-199`) — no change to OddTree, OddScore, or the propagation algorithm. Only the *input ad set* grows.

This is deliberately general (it will later serve hobbies, jobs, long-term goals — see §5). Guards and monsters are simply its first two consumers.

---

## 3. Guards as gatekeepers (first consumer)

New activities (`BehaviorRegistry` `ActivityKind` + `ActivityCatalog` specs):
- **`Patrol`** — walk between the town's gate cells. `baseGate ≈ 1.4` (work-like duty pull), `baseUtility ≈ 0.003`, Δ = 0 (no need relief), terminal (no Enables children).
- **`StandWatch`** — hold a post at a gate. `baseGate ≈ 1.5`, `baseUtility ≈ 0.002`, Δ = 0, terminal.

Dynamic Object Zero injects, for an on-duty guard: a `Patrol` ad (day) or `StandWatch` ad (night) **located at an assigned gate cell**; and, when a creature is sensed (via `SubjectiveViewRegistry` threat entries), a **boosted `Attack` ad** targeting `NearestCreature`.

**Shift model (resolved):** each town's guards split deterministically by guard index — **even index = day watch, odd = night watch** — gated by a day/night flag read from the existing game clock. No per-guard schedule state; CQRS-clean. A guard that is *off* its watch has no `Patrol`/`StandWatch` ad injected, so it falls back to normal civilian behaviour (rest/eat/social) — off-duty guards do **not** respond to breaches this pass (lean group scope).

**Attack-ad score (resolved):** proximity-derived, not a fixed constant. The injected `Attack` ad's score increases as the sensed creature nears the guard, and is **floored above the Patrol/StandWatch duty pull** so any real breach always out-scores standing watch. The guard's own Fear/personality still grades it through the existing `DesperationGraded` Hunger↣Fear prepotency — a terrified or starving guard can still break.

**All three behaviours the user asked for emerge — no state machine:**
- **Post:** with no needs/threats, `StandWatch` (baseUtility + duty gate) beats `Idle` → guard stands at the gate.
- **Patrol:** `Patrol` ad routes the guard between gate cells.
- **React / rush:** the sensed-creature `Attack` ad outscores Patrol → guard breaks to intercept the monster funnelling through the gate.
- **Abandon post (emergent "humanity"):** when Hunger's gap exceeds the duty `baseGate`, `EatHome` wins → guard leaves to eat. **Free** from existing prepotency; nothing to build.

**Gate cells (`GateMap`):** computed once at load from the runtime `TownGridData` (`Cost` grid + per-block `BlockGates` flags) — walkable openings in the otherwise-solid wall ring. No BSA re-parse needed (confirmed feasible in recon). Immutable after load (CQRS-clean). Guards are distributed across their town's gates deterministically (round-robin by guard index) at seed time.

**Curfew — functional gate open/closed (resolved):** authentic to Daggerfall (town gates shut at night). Modelled as a single clock-derived `GatesClosed` flag, **not** a mutation of the immutable cost grid. Pathfinding queries effective passability = static `Cost` grid **AND** a dynamic gate-closed overlay (gate cells become impassable while closed) — a read-only per-tick derivation, CQRS-clean.
- **Day:** gates open. Monster pathfinding funnels through the gap → guards on the day watch intercept at the gate.
- **Night:** gates closed. The gate cells block in the overlay, so a hungry monster's path to its prey **stalls at the wall** — it cannot enter. The night `StandWatch` guard attacks any monster that reaches the wall near its post. Walls genuinely protect the town after dark; the watch finishes what comes knocking. No gate HP / siege mechanic (monsters do not batter the gate this pass).

**Curfew visual (in, but cuttable):** town3d swaps the gate model **446 (open) ↔ 447 (closed)** by sim time of day. Both models already export. This is its own viewer task; if it fights the instanced renderer it may be dropped without blocking the gameplay (the sim-side curfew stands alone).

---

## 4. Monsters that respect walls and hunt through gates (first consumer, predator side)

Today `CreatureSystem` (`Assets/Sim/Engine/Units/CreatureSystem.cs`) moves creatures **straight-line, ignoring the cost grid** — they phase through walls and buildings, spawn relative to the NPC centroid, and pick wander targets geometrically. This makes walls meaningless to them.

Changes (this is the bridge until monsters-are-full-agents, the far-future milestone):
- Add a **hunger** drive to `CreatureData` (level + drift).
- **Spawn outside the wall** — at a walkable cell beyond the perimeter ring, not just centroid + 40 m.
- **When hungry:** pick the **nearest civilian in the nearest settlement**, then **pathfind** via the existing `TownPathfinder` (respecting cost 0 **and the gate-closed overlay**). By day the gate is the only opening in the ring, so the monster is *forced* to funnel through it — straight into any posted/patrolling guard. By night the closed gate removes that opening, so the path stalls at the wall and the monster cannot reach its prey. Eat on arrival (existing attack/damage path).
- **When not hungry:** keep cheap straight-line wandering, but **collide** with blocked cells (no more phasing).
- **Scope (resolved):** one monster hunts at a time — **no packs, no coordinated hunts** this pass. Pathfinding does nudge creatures toward becoming agents; that is the intended bridge to the far-future monsters-as-full-agents milestone, accepted knowingly.

This is what makes "guards keep monsters out" actually emerge: the chokepoint is real because the predator must use the gate.

---

## 5. Later layers of the ODD extension (not even in the guards pass)

The same Dynamic Object Zero seam is where these plug in; recorded for completeness, build later:
- **Goal-Nudge:** a long-term goal adds a `GoalBonus` term to `OddScore` for actions whose tags match the goal's tags (e.g. a guard who wants to be a musician practises lute off-duty). Requires a goal/tag model + one scoring addend.
- **Beacon (Horizon resolution):** when a goal's requirements aren't satisfiable locally, the goal injects a low-utility `SearchFor(<biome/place>)` navigation ad into Object Zero — "wandering with intent." Requires a goal→beacon ad generator.

These are general sim features (hobbies, jobs, ambitions), not guard-specific.

---

## 6. Resolved decisions (was: open questions — the "ton of stuff to discuss")

Decided 2026-06-19 with the user; these define the scope of this pass.

| Question | Decision |
| --- | --- |
| Shift model | Split by guard index: **even = day watch, odd = night watch**, gated by a clock day/night flag. No per-guard schedule state. (§3) |
| `Attack`-ad score | **Proximity-derived, floored above the Patrol/StandWatch duty pull** so a breach always wins. Fear/personality still grades it via existing prepotency. (§3) |
| Gate assignment vs guard count | Deterministic **round-robin by guard index** across the town's gate cells at seed time, regardless of gate≠guard count. (§3) |
| Functional gate open/closed | **Yes — night curfew.** Clock-derived `GatesClosed` flag + read-only pathfinding overlay (no cost-grid mutation). Day open, night closed. (§3) |
| Night behaviour of a hungry monster | **Blocked at the wall; the night watch attacks it there.** No gate HP / siege / battering. (§3, §4) |
| Group scope | **Lean — solo monster, nearest on-duty guard reacts.** No packs, no reinforcement; off-duty guards do not respond. (§3, §4) |
| Curfew visual (446↔447 swap) | **In, but cuttable** — its own town3d task; droppable if it fights the instanced renderer. Sim-side curfew stands alone. (§3) |
| Creatures → agents | Pathfinding nudges them that way; **accepted** as the intended bridge to the far-future agents milestone. (§4) |

**Honesty metrics (build with the pass, per "score what everyone DOES," [[scoring-honesty-not-numbers]]):** over a simulated day on a small walled town, histogram — monsters reaching prey via gate vs. blocked at wall; guard intercepts at gate; civilian deaths inside-walls vs. at-gate; guard post-abandonment events (Hunger beat duty). These are the signal that the chokepoint actually emerged, not the raw counts.

**Verification:** `Sim.Tests` is unbuildable/stale ([[test-suite-stale-being-rewritten]]) — do **not** gate on it. Verify via live `Sim.Web` endpoints and the metrics histogram, on a light walled town (**Gallotale**, 5×6 / 401 placements — [[town-walls-and-walled-towns]]), not Daggerfall city (laggy).

---

## Key file references

| Concern | File:line |
| --- | --- |
| Decision pipeline / ad injection seam | `OddSystem.cs:178-219` |
| Guard identity / targeting | `OddSystem.cs:175`, `:252-258` |
| Scoring formula | `ActivityCatalog.cs:458-474` |
| Object Zero baseline | `Affordance.cs:81` |
| Needs / prepotency | `NeedsRegistry.cs:11-21`, `DriveCatalog.cs:69-161` |
| Guard seeding / counts | `TownLoader.cs:197-218`, `:225-233` |
| Creature movement (to rework) | `CreatureSystem.cs:53-124` |
| Gate-cell source data | `TownGridData` (`Cost`, `Gates`), `BlockWalkability.ComputeGates` |
| Sensing / threat view | `SenseSystem.cs:42-88`, `SubjectiveViewRegistry` |
