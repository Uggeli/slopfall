# ODD — Canonical Specification (north star)

> This is the destination algorithm the DFU sim converges toward — the concrete form of the
> [Atoms](../docs) cognitive model. It is the authoritative reference; our implementation is a
> faithful **depth-1 subset** of it today. See [`odd_convergence.md`](odd_convergence.md) for the
> staged plan from here to "true ODD." Do not edit the algorithm here to match the code — edit the
> code to match this. Three parts: the decision algorithm, the execution pipeline, the smart-object
> data architecture.

---

# Part 1 — ODD Decision Algorithm

## Definitions

**Agent** := `(Needs: float[], Params: float[P], Context: AgentContext)`

**Ad** := `(Data: any, Preconditions: Predicate[], Enables: Ad[]?)` — `Data` is opaque to the
algorithm; only `V` reads it. `Preconditions` gate collection. `Enables` defines tree edges (what this
action makes available).

**Node** := `(ParentIndex, ChildStart, ChildEnd, AdRef, DirectScore, PropagatedScore, IsTerminal)`
- `ChildStart..ChildEnd` is an inclusive range into the buffer. If `ChildStart > ChildEnd`, no children.
- `AdRef` links back to the source ad (needed for execution and revalidation).
- BFS fill guarantees children of any node occupy a contiguous slice.

**Buffer** := `Node[N]` — flat array, BFS-ordered, `Buffer[0]` = root (Object Zero)

**AgentContext** := `(Role, Shift, Goals[])` — feeds Dynamic Object Zero

**Goal** := `(MatchFn: Ad → float, BeaconAction: Ad?)` — `MatchFn` returns a score bonus (0.0 = no
match). `BeaconAction` is injected into Object Zero when no local ads return > 0.0 for this goal's
`MatchFn`.

## Algorithm

### 1. Collect
Gather root candidate set `C₀`:
```
C₀ = ObjectZero(agent.Context)
   ∪ { ad ∈ SmartObjects(perception_radius) | Preconditions(ad, world) = true }
```
`ObjectZero(ctx)` emits:
- Static fallbacks: `{Idle, Wander, Panic, Rest}`
- Role actions if `ctx.Shift = Active`
- For each goal in `ctx.Goals`: if no ad in `SmartObjects` satisfies `goal.MatchFn(ad) > 0`, inject
  `goal.BeaconAction` (if non-null)

`C₀` populates depth 1 of the buffer. Deeper levels are expanded during Build.

### 2. Score
```
DirectScore(a, g) = V(a, g) + GoalBonus(a, g)
GoalBonus(a, g)   = Σ { goal.MatchFn(a) | goal ∈ g.Goals }
```
`V(a, g)` is a user-defined value function over `(Ad, Agent)` returning a scalar. `V` is where all
domain-specific logic lives — personality, cultural modifiers, whatever. It is a plug point, not a
prescribed formula.

### 3. Build (Kahn's Topological Sort)
`Ad.Enables` defines a dependency DAG: A enables B ⟹ B depends on A. The buffer is filled by Kahn's
algorithm — layer-by-layer, each layer only actions whose dependencies are already placed.
```
placed = {}
Buffer[0] = Node(parent: -1, ad: ObjectZeroRoot)
cursor = 1
for each ad in C₀:                       // Layer 0 → 1: seed from C₀
    Buffer[cursor] = Node(parent: 0, ad: ad); placed.Add(ad); cursor++
for i = 0 to cursor-1:                    // Layer N → N+1: expand Enables
    childStart = cursor
    if Buffer[i].Ad.Enables ≠ null:
        for each enabled_ad in Buffer[i].Ad.Enables:
            if enabled_ad ∈ placed: skip                       // cycle/duplicate
            if Preconditions(enabled_ad, world) = false: skip
            Buffer[cursor] = Node(parent: i, ad: enabled_ad); placed.Add(enabled_ad); cursor++
    childEnd = cursor - 1
    Buffer[i].ChildStart = childStart; Buffer[i].ChildEnd = childEnd
    Buffer[i].IsTerminal = (childStart > childEnd) AND (i > 0)
```
Terminates when `i` catches `cursor`. Ads reachable only through a cycle are never placed — correct:
cyclic dependencies are unresolvable and silently excluded.

- **Invariant:** `∀ i > 0: Buffer[i].ParentIndex < i` (topological order)
- **Invariant:** children of a node occupy `Buffer[ChildStart..ChildEnd]` contiguously
- **Invariant:** each ad appears at most once (no double-counting during propagation)

### 4. Propagate (single backward pass, O(N))
```
for i = (cursor - 1) downto 1:
    total = Buffer[i].DirectScore + Buffer[i].PropagatedScore
    Buffer[Buffer[i].ParentIndex].PropagatedScore += total × decay
```
`decay ∈ (0,1)`. Post: a descendant at depth `d` below `n` contributes `score × decay^d` — the
geometrically discounted subtree sum.

### 5. Traverse
```
next = argmax { Buffer[c].DirectScore + Buffer[c].PropagatedScore
              | c ∈ Buffer[p.ChildStart .. p.ChildEnd] }
```
Before executing a terminal node, **revalidate** preconditions against current world state:
```
if next.IsTerminal:
    if Preconditions(next.AdRef, world) = false: discard buffer, goto 1   // stale — rebuild
    execute next.AdRef; discard buffer, goto 1
else:
    set p = next, repeat 5
```
Revalidation is necessary because world state may change between tree construction and execution.

### 6. Interrupt
```
if (flags.priority ≥ CRITICAL) OR (buffer = null): discard buffer; goto 1
```
No plan resumption. Rebuild from current world state.

## Structural Inertia
While traversing a committed tree, alternative root-level branches are **not re-evaluated**. Switching
requires an interrupt, not a score comparison. Binary commit/interrupt rather than continuous
re-scoring — the anti-jitter mechanism.

## Properties
| Property | Guarantee |
|---|---|
| Liveness | Object Zero ensures `|C₀| ≥ 1` always |
| No oscillation | Structural inertia; switching requires interrupt |
| Cycle-free | Kahn's placement set excludes cyclic/duplicate ads at build |
| O(N) build + propagate | Kahn's expansion + single backward pass |
| O(k) per-tick traversal | Compare `k` children via the child range |
| Cache-friendly | Contiguous flat buffer; parent at lower index; children contiguous |
| No failure state | Graceful degradation to Object Zero fallbacks |
| Staleness safety | Terminal revalidation catches stale preconditions → rebuild |

**Runtime:** Collect → Score → Build (Kahn's) → Propagate → Traverse (with revalidation) → Interrupt
check → repeat. Everything else — roles, goals, personality, culture, beacons — is input shaping.

---

# Part 2 — Action Execution Pipeline

**Core idea:** the executor is a *scheduler*, not a progress tracker. It knows each action's duration
and the pipeline overhead, and fires effect events EARLY so they land in registries exactly when the
duration expires.
```
fire_at  = started_at + duration - overhead
overhead = 2 ticks  (1 tick event delivery + 1 tick registry visibility)
```
Entity is "busy" for the full duration (structural inertia, no re-evaluation). When duration expires,
ODD reads registries — the effect is already there. Duration = real time, zero scheduling overhead.

**Why overhead = 2:** emit at tick N → processed at N+1 → visible at N+2. Fire 2 ticks early so the
effect lands on time. May vary per action; 2 is the default.

**Timeline** (move(5) + eat(8), from tick 0): executor starts move (fire_at=3); emits
`entity_moved` at 3; movement system writes position at 4; visible at 5 → starts eat (fire_at=13);
emits `food_consumed` at 13; metabolism restores at 14; visible at 15 → signals ODD to rebuild. The
2-tick tail only matters at chain end; mid-chain transitions have zero overhead.

**Interrupt:** on CRITICAL, cancel the scheduled fire, emit the *partial* effect immediately
(`eat_interrupted(ticks_done=4, total=8)`), signal ODD → rebuild. Interrupt latency: 1 tick.

**Executor state per entity:** `current_action, target, started_at, duration, fire_at,
effect_event (pre-built), chain[], chain_index`.

**Duration** computed at start from public data: `move = ceil(base × terrain_cost / speed)`, eat/drink/
dig/mate/birth/nurse from action definitions, enter/exit = 2 (minimum = overhead). Modifiers from
terrain, entity params, activity, age.

**Partial effects on interrupt:** eat/drink/nurse proportional (`ticks_done/total × full`); dig saves
progress; mate cancelled; move/birth/enter/exit non-interruptible.

**Relationship to ODD:** ODD decides WHAT (selects activity, constructs the action chain); the
executor handles WHEN (timing, firing effects). Handoff via a ConcurrentDictionary (ODD writes
decisions in Update, executor reads in ProcessEvents) — 1-tick latency, same as all event-driven
comms. Chain construction lives in ODD (forage = [move…, eat], seek-water = [move…, drink], etc.).

---

# Part 3 — Smart Object System (Pure Data Architecture)

Decouples **Ontology** (Flags), **Routing** (Actions), and **Execution** (Effects). The Reasoner is a
dispatcher: interprets inert Flags to find Actions, evaluates them, routes execution to subsystems.

**Data hierarchy:**
1. **FLAGS** — pure descriptors (what something IS); advertise action keys.
   `WATER_SOURCE → advertised_actions: [DRINK, FILL_CONTAINER, WASH]`; carry perception
   (base_visibility, hidden_by_tags) and signal propagation.
2. **ACTIONS** — interaction contracts linking flags to subsystems:
   - **requirements** (the logic filter): target_flags, actor states/forbidden_tags, context (range…)
   - **prediction** (the promise for AI planning): actor_effects/target_effects, duration
   - **execution** (routing): subsystem, handler, effect_key, animation
   - **occupation** (locking): locks_target/actor, max_simultaneous
3. **EFFECTS** — engine-side execution logic (mutations, conditionals, animations); live in the
   subsystem, not the Reasoner. e.g. `EFFECT_DRINK_LIQUID` mutates thirst/fill, with a conditional
   poison branch.

**Reasoner pipeline:** Scan (perception + flag propagation + visibility filter) → Discover (flags →
advertised_actions → requirements check) → Evaluate (template + score `Σ attribute × personality ×
need`) → Dispatch (lock → **override resolution** → construct payload → send to subsystem).

**Override resolution (traps/mimics):** deception is handled at Dispatch — the agent intends one
action, the Reasoner swaps the effect before handing it to the engine:
`(MIMIC, OPEN) → EFFECT_MIMIC_ATTACK`, `(TRAPPED, OPEN) → EFFECT_TRIGGER_TRAP`,
`(CURSED, EQUIP) → EFFECT_APPLY_CURSE`.

**Complete flow (thirsty villager at a poisoned well):** Scan sees `WATER_SOURCE` (poison hidden) →
Discover `DRINK` (requirements met) → Evaluate (predicts thirst −30, high utility) → Dispatch
(no override; route `ProcessDrink` to SUSTENANCE_SYSTEM) → Execute (`EFFECT_DRINK_LIQUID` reduces
thirst; conditional `is_poisoned` applies POISONED) → Feedback (memory records POISONED for this
well).
