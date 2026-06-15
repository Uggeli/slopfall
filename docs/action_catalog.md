# Action catalog — the unified agent verb vocabulary

*Planning artifact. The player is just another agent obeying the same rules, so there is ONE verb
vocabulary for player, civilians, and creatures. This catalogs it, divides it into atomic **actions**
and composite **activities**, and scopes what we populate now vs reserve. Companion to
[`decision_architecture.md`](decision_architecture.md) (senses→ads→choose) — discovery returns
activities from this catalog.*

## Two unifications the DFU scour revealed

1. **`PlayerActivate.cs` is the player's `Discover(thing) → actions`.** Clicking a thing dispatches by
   thing-type to its verbs (door→open/lock/bash, container→loot, NPC→talk/pickpocket, lever→pull,
   book→read). That is exactly our affordance resolver — the player already has per-thing action
   discovery; the AI has its own (`EnemyMotor.TakeAction`). Unifying means **both resolve the same
   thing → the same actions** through one catalog. This is the concrete "player = agent" seam.
2. **Crime is not a verb — it's a legitimacy tag on a normal action.** `Steal` = `Take` without
   permission; `Murder` = `Attack` on an innocent; `Trespass` = `Enter` private property. The catalog
   has `Take`/`Attack`/`Enter`; *context* decides legitimacy. Keeps the catalog small and is the exact
   foundation for the norm/conscience loop (behavior.md Stage D): guards punish illegitimate actions.

## Atomic actions — one operation each (player & AI share the executor)

| Action | What it does | Status |
|---|---|---|
| `MoveTo` | relocate toward a point (modes walk/run/crouch/climb/swim/levitate/ride are **modifiers**, not verbs) | live |
| `Sustain` | be at the spot applying need-Δ over time (eat / rest / socialize / visit / idle all reduce to this) | live |
| `Transfer` | move coin (pay / buy / sell / give / earn / deposit / donate) | live |
| `Speak` | greet / inform / haggle → a `RelationImpulse` | live |
| `Ask` / `Answer` | raise / answer a request (grant or refuse) | live |
| `TrainSkill` | accrue skill XP | live |
| `Attack` / `CastSpell` / `ApplyEffect` | combat & magic (Damage/Heal/effects exist as ops; no chooser yet) | reserved |
| `Take` / `Drop` / `Equip` / `Use` | item handling | reserved |
| `Open` / `Close` / `Lockpick` / `Bash` / `Read` | world-object interaction | reserved |

**Reserved** = named in the vocabulary so player/AI/items drop in as rows later, but no executor yet
(nothing in the town exercises them — proto discipline). **Reactions** (`TakeDamage`, `Die`,
`Knockback`, `Paralyze`, `Fall`, `LevelUp`) are triggered consequences, not chosen actions, and live
in their own systems.

## Composite activities — durative plans = `MoveTo` + Doing-phase actions (the choosable unit)

| Activity | MoveTo target + Doing actions |
|---|---|
| Idle | (in place) `Sustain` |
| Wander | `MoveTo`(random) |
| Sleep | home → `Sustain` |
| Work | workplace → `Sustain` + `Transfer`(earn) |
| EatHome | home → `Sustain` |
| EatTavern | tavern → `Transfer`(pay) + `Sustain` |
| Socialize | tavern → `Transfer`(pay) + `Sustain` |
| Visit | landmark → `Sustain` |
| Chat | (in place) `Speak` |
| SeekHelp | target → `Ask` |
| *(near)* Fight/Pursue/Flee/Loot/Shop/Steal/Rest/Travel | `MoveTo` + Attack/Take/Transfer… |

This decomposition is the data in `ActionCatalog.DoingSteps`. Every activity is a tiny action-plan —
the seed of the eventual ODD-tree, but flat for now.

## Services = directed transactions
DFU's services (Buy/Sell/Repair/Train/Bank/Enchant/Rent/Donate) are player-only *UI flows* that are
really `Transfer` + receive-good at a provider. As agent actions they **are the economy loop (E1–E3)** —
a shopkeeper running `Buy` for a resident customer is the same machinery, no special player path.

## The catalog, assembled
- **`AffordanceCatalog`** (thing-kind → activities offered; agent-relative for home/workplace/person).
- **`ActionDiscovery.Discover(agent, thing) → ads`** — the resolver: resolves a thing into the
  activities available *to this agent* (the CAN side). `GatherAds(agent)` = innate ∪ known-place ads.
- **`ActivityCatalog`** (each activity's duration / served-Δ / target) + **`ActionCatalog.DoingSteps`**
  (its action decomposition).
- **`ActionKind`** — the atomic vocabulary above.

## v1 scope
Populate what the town exercises (the 10 activities + their ~7 atomic actions); reserve the rest as
named `ActionKind`s. `Discover`/`GatherAds` reproduce today's `OddSystem` candidate set as **data**, so
the E0b-4 rewrite consumes them with no behavior change, and E1 shops / unify-agents combat land as
catalog rows rather than new code.

## Player-as-agent status
- **Already shared, kind-agnostic, in our sim:** movement, damage/heal, effects (`HealthSystem`/effect
  pipeline treat every entity identically).
- **Player-only UI in DFU that must become catalog actions:** trade/services, talk/haggle, crime, rest,
  fast-travel — almost all already on the roadmap (economy loop, norm loop, social actions). So "player
  as agent" is not a separate epic; it's the same actions the NPCs are getting, with the player as one
  more chooser in the marketplace.
