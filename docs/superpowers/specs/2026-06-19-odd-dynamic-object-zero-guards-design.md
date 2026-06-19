# Dynamic Object Zero + guards-as-gatekeepers — recorded design (future pass)

**Date:** 2026-06-19
**Status:** Recorded for a future session — **NOT** being built now. Captured so the guards pass starts from a written plan.
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

**All three behaviours the user asked for emerge — no state machine:**
- **Post:** with no needs/threats, `StandWatch` (baseUtility + duty gate) beats `Idle` → guard stands at the gate.
- **Patrol:** `Patrol` ad routes the guard between gate cells.
- **React / rush:** the sensed-creature `Attack` ad outscores Patrol → guard breaks to intercept the monster funnelling through the gate.
- **Abandon post (emergent "humanity"):** when Hunger's gap exceeds the duty `baseGate`, `EatHome` wins → guard leaves to eat. **Free** from existing prepotency; nothing to build.

**Gate cells (`GateMap`):** computed once at load from the runtime `TownGridData` (`Cost` grid + per-block `BlockGates` flags) — walkable openings in the otherwise-solid wall ring. No BSA re-parse needed (confirmed feasible in recon). Immutable after load (CQRS-clean). Guards are distributed across their town's gates deterministically (round-robin by guard index) at seed time.

---

## 4. Monsters that respect walls and hunt through gates (first consumer, predator side)

Today `CreatureSystem` (`Assets/Sim/Engine/Units/CreatureSystem.cs`) moves creatures **straight-line, ignoring the cost grid** — they phase through walls and buildings, spawn relative to the NPC centroid, and pick wander targets geometrically. This makes walls meaningless to them.

Changes (this is the bridge until monsters-are-full-agents, the far-future milestone):
- Add a **hunger** drive to `CreatureData` (level + drift).
- **Spawn outside the wall** — at a walkable cell beyond the perimeter ring, not just centroid + 40 m.
- **When hungry:** pick the **nearest civilian in the nearest settlement**, then **pathfind** via the existing `TownPathfinder` (respecting cost 0). Because the gate is the only opening in the ring, the monster is *forced* to funnel through it — straight into any posted/patrolling guard. Eat on arrival (existing attack/damage path).
- **When not hungry:** keep cheap straight-line wandering, but **collide** with blocked cells (no more phasing).

This is what makes "guards keep monsters out" actually emerge: the chokepoint is real because the predator must use the gate.

---

## 5. Later layers of the ODD extension (not even in the guards pass)

The same Dynamic Object Zero seam is where these plug in; recorded for completeness, build later:
- **Goal-Nudge:** a long-term goal adds a `GoalBonus` term to `OddScore` for actions whose tags match the goal's tags (e.g. a guard who wants to be a musician practises lute off-duty). Requires a goal/tag model + one scoring addend.
- **Beacon (Horizon resolution):** when a goal's requirements aren't satisfiable locally, the goal injects a low-utility `SearchFor(<biome/place>)` navigation ad into Object Zero — "wandering with intent." Requires a goal→beacon ad generator.

These are general sim features (hobbies, jobs, ambitions), not guard-specific.

---

## 6. Open questions for the guards pass (the "ton of stuff to discuss")

- Shift model: how are day/night watches assigned across a town's guards? Deterministic per settlement + index?
- `Attack`-ad score: fixed high constant vs. derived from threat proximity/severity? How does it interact with a guard's own Fear/personality?
- Gate assignment when gates ≠ guard count (more gates than guards, or vice-versa).
- Should gates have functional open/closed state (night curfew?), or stay always-open visually with guards as the only filter?
- Multiple creatures / pack behaviour; reinforcement (do off-duty guards respond?).
- Tuning + honesty metrics (per the "score what everyone DOES" principle): monsters reaching prey via gate vs. blocked at wall; guard intercepts at gate; civilian deaths inside-walls vs. at-gate; post-abandonment events.
- Does giving creatures pathfinding pull them toward becoming agents sooner than planned?

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
