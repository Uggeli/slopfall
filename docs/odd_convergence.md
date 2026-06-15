# ODD Convergence — from depth-1 to "true ODD"

*The staged refactor that grows our sim's decision layer into the full algorithm in
[`odd_spec.md`](odd_spec.md). Companion to [`decision_architecture.md`](decision_architecture.md)
(the senses→ads→choose split, already done) and [`action_catalog.md`](action_catalog.md).*

## Where we are
We are a faithful **depth-1 subset** of the spec. With `Ad.Enables = null`, the spec's Build is
trivial (C₀ *is* the buffer), Propagate is a no-op, and Traverse is plain argmax — which is exactly
our `GatherAds → score → argmax`. What we have, in spec terms:

| Spec | Ours | Gap |
|---|---|---|
| `Ad(Data, Preconditions, Enables)` | `Ad{Verb,Building,X,Z,Spec}` | no Preconditions/Enables |
| Object Zero | `AffordanceCatalog.Innate` (Idle/Wander) | no Panic/Rest, no goal-beacons |
| Collect → C₀ | `GatherAds` | flat (depth-1) |
| Score: V + GoalBonus | `ScoreAd` | **a verb `switch`**, no goals |
| Preconditions gate Collect | folded into ScoreAd as `return 0` | not separated |
| Build / Propagate (the tree) | — | absent (no chains yet) |
| Traverse + revalidate | argmax | no revalidation |
| Structural inertia | hourly re-decide + sticky | weaker (re-scores) |
| Interrupt (rung-2) | Perception → Execution | ✓ |
| Smart-Object Flags → Actions | `AffordanceCatalog` (kind→verbs) | simpler |
| Prediction (effects) | `Spec.Delta` | ✓ |
| Executor (fire-early) | `ExecutionSystem` (countdown) | no early-fire |

**Principle (proto discipline):** adopt the spec's *shapes and vocabulary* now; only build the *tree
machinery* once the world has chained actions to exercise it. Deferring Build/Propagate is correct,
not a shortcut — there is nothing to topologically sort until actions `Enable` one another.

## The phases

### O0 — Preconditions + uniform V  *(now; fixes the `switch` and the red test)*
- Give the catalog a **`Preconditions(ad, ctx)`** predicate; check it in `GatherAds` (gate Collect).
  Hard gates move out of scoring: `Work` = keeper ∧ 8–18 ∧ ¬holiday; `Visit` = hours ∧ prepotency>0;
  `EatTavern/Socialize` = hours.
- Make **`V`** a single uniform expression over `Ad.Data` (the modulator fields from
  `action_catalog.md`): `growth ? base·gate : gap·gate + base`, where `gate` is the product of the
  applicable soft modulators (distance, weather, social, trait, holiday). **No verb switch.**
- Re-tune the soft weights against the behavioral harness (the keeper/Visit balance is the first
  knob). Acceptance = the harness's healthy expectations hold; economy `??`s remain (E1's job).
- *Outcome:* "ad is ad" is true; Collect/Score match the spec; suite green.

### O1 — Vocabulary + structural inertia + revalidation  *(cheap, high-value alignment)*
- Rename toward the spec: `V`, Object Zero, `decay`, Collect/Score/Traverse. (`OddSystem` →
  `DeliberationSystem` once the test statics move.)
- **Structural inertia:** commit to the choice until an *interrupt*, instead of re-scoring on the
  hourly cap. Stronger anti-jitter, closer to the spec, fewer wasteful re-decisions.
- **Terminal revalidation:** re-check preconditions at execution time (world moved between decide and
  act) — already cheap given O0's predicate. Prevents stale-action bugs.
- Add the missing Object-Zero fallbacks as they earn material: `Rest` (≈ our Idle/Sleep), `Panic`
  (with the threat/flee loop).

### O2 — The tree: `Enables` + Build (Kahn's) + Propagate + tree Traverse  *(Stage E — needs chains)*
- The big one, gated on **chained actions existing** (Things/items: buy→carry→cook; move→dig→build).
  Until then there is nothing to sort.
- `Ad` gains `Enables`; implement the flat BFS buffer, Kahn's Build, the O(N) Propagate (geometric
  `decay`), and tree Traverse with the terminal revalidation from O1.
- Activities stop being flat catalog rows and become **subtrees** (an "activity" = a scored path);
  `ActionCatalog.DoingSteps` becomes real `Enables` edges.

### O3 — Goals + Beacons  *(directed, multi-step behaviour)*
- `AgentContext.Goals` with `MatchFn` (score bonus) + `BeaconAction` injected into Object Zero when no
  local ad matches. This is how quests, jobs-as-goals, and the player's directives steer the same
  marketplace. Pairs naturally with the quest loop and `RequestSystem` folding into the tree.

### O4 — Smart-Object Flags  *(richer interaction; with Things)*
- Generalize `AffordanceCatalog` (kind→verbs) into the spec's Flags→Actions→Effects: objects carry
  **Flags** advertising action keys; **Actions** carry requirements/prediction/routing/occupation;
  **Effects** live in subsystems with conditional branches; **override resolution** for traps/mimics
  /cursed items. This is where doors, containers, items, and deception come in — and where the
  player's `PlayerActivate` verbs and NPC affordances become one table.

### O5 — Executor fire-early pipeline  *(timing refinement; anytime)*
- Upgrade `ExecutionSystem` from countdown to the scheduler: `fire_at = started + duration − overhead`
  so effects land exactly on time; pre-built effect events; partial effects on interrupt; per-action
  duration computation. Mostly a correctness/perf refinement of what we have.

## Interleaving with the economy plan
ODD convergence and the economy loop ([`economy.md`](economy.md), E0–E4) co-evolve on the **depth-1**
ODD — none of E1–E3 needs the tree:
- **E1 resident jobs / E2 rent / E3 tax+guards** = new ads with **Preconditions + V** (O0's shapes) +
  small effect handlers. Each is a catalog row, not Decide surgery — the whole point of the refactor.
- **E4 shop stock + larder (Things)** is what finally creates the *chains* (buy→carry home→cook) that
  make **O2 (the tree)** worth building, and the *items* that make **O4 (Flags)** worth building.
- **O3 Goals/Beacons** lands with the quest loop (paid tasks) and the player as an agent.

So the order in practice: **O0 now → O1 → economy E1–E3 on depth-1 ODD → (E4 Things) → O2/O4 → O3**,
with O5 slotting in whenever execution timing needs it. Every step is gated on its test material
existing and verified by the behavioral harness, not by pinned mechanism numbers.

## Acceptance
"True ODD" = the runtime in `odd_spec.md` running the town: Collect (Preconditions) → Score (uniform
V + GoalBonus) → Build (Kahn's) → Propagate (decay) → Traverse (revalidate) → Interrupt, with
Smart-Object Flags feeding Collect and the fire-early executor carrying out chains. We get there one
proto-gated phase at a time, town behaving correctly (per the harness) at every step.
