# Action catalog — the unified agent verb vocabulary

*Planning artifact. The player is just another agent obeying the same rules, so there is ONE verb
vocabulary for player, civilians, and creatures. This catalogs it, divides it into atomic **actions**
and composite **activities**, shows how multi-step **chains emerge** from composing them (we author
the blocks, never the sequences), and scopes what we populate now vs reserve. Companion to
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
| `Take` / `Drop` / `Use` | item handling: `Take` (pick up → now *carried*), `Drop` (set down — on the ground, or *into a building* = store), `Use` (apply an item's affordance — `Eat` an Edible, drink, read). Each carries physical **preconditions** + **effects** (see [§ chains emerge](#chains-emerge--author-the-blocks-never-the-sequences)), which is what lets them chain. | live (items) |
| `Equip` | wield / wear into a slot | reserved (until weapons/armour pull it) |
| `Open` / `Close` / `Lockpick` / `Bash` / `Read` | world-object interaction | reserved |

**Reserved** = named in the vocabulary so player/AI/items drop in as rows later, but no executor yet
(nothing in the town exercises them — proto discipline). The items pass moves `Take`/`Drop`/`Use` *off*
this list — the world now exercises them. **Reactions** (`TakeDamage`, `Die`, `Knockback`, `Paralyze`,
`Fall`, `LevelUp`) are triggered consequences, not chosen actions, and live in their own systems.

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

This decomposition is the data in `ActionCatalog.DoingSteps`. Each activity is a tiny action-plan —
but the *larger* plans (go to the shop → take a loaf → carry it home → store it) are **not** authored
here or anywhere. How those arise without being written is the next section.

## Chains emerge — author the blocks, never the sequences

Actions and activities are the **building blocks**; a *chain* (go to the shop → take a loaf → carry it
home → store it, or → eat it) is an emergent **sequence** of them. **We never author a chain** — there
is no "steal-then-store" plan written down, the same way the decider has no hardcoded candidate block
(`decision_architecture.md`). A sequence is a *trajectory*, not a script.

What makes a chain form is two properties every block carries:

- **Preconditions** — physical state that gates whether the block is even offered. You can only
  `Use`/`Drop` a thing you are *carrying*; you can only `Take` a thing you *perceive*. A block reaches
  the marketplace only when the world — including the agent's own inventory — permits it.
- **Effects** — the block changes physical state instead of teleporting it. `Take` → the item is now
  *carried*; `Use`(eat) → *consumed*, hunger falls; `Drop`-into-home → it *sits in the home*. (Before
  items, effects teleported between ledgers — buy jumped shop→larder, eat jumped larder→full belly,
  steal jumped shelf→larder — with nothing physical in between. Items + honest effects end the
  teleporting.)

Finishing one block changes the state that affords and motivates the next, so a chain falls straight
out of the existing **decide → act → re-decide** loop (`OddSystem`'s single-step argmax). No plan tree,
no plan-constructor, no cursor — *yet*; lookahead planning is a later layer (`behavior.md` Stage E).
Today the chain is greedy and emergent:

> hungry agent perceives a loaf → `Take it` is the top-scoring block → now carrying it, still hungry →
> re-decide → `Use`(eat) wins if starving, `carry home → Drop`(store) wins if it's more about stocking
> up. Same blocks, different chain, decided by state + drives — nothing scripted.

Theft is the same story (unification #2): no `Steal` *plan*, and ultimately no `Steal` *verb* — only
`Take`, offered on a perceived item, scored by its value *to me*, a crime exactly when the owner isn't
me. `ActivityKind.Steal` is a transitional alias that dissolves into `Take` + the ownership check.

**So a block's authored data is its preconditions, effects, and value — never the company it keeps.**
Give `Take`/`Use`/`Drop` honest pre/effects and both the steal-and-eat and steal-and-store chains
appear without either being written.

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
catalog rows rather than new code. The **items pass** activates `Take`/`Use`/`Drop` (with physical
pre/effects) and lets theft, eating, and storing chain emergently off them — the first blocks whose
*effects* are physical rather than teleported.

## Player-as-agent status
- **Already shared, kind-agnostic, in our sim:** movement, damage/heal, effects (`HealthSystem`/effect
  pipeline treat every entity identically).
- **Player-only UI in DFU that must become catalog actions:** trade/services, talk/haggle, crime, rest,
  fast-travel — almost all already on the roadmap (economy loop, norm loop, social actions). So "player
  as agent" is not a separate epic; it's the same actions the NPCs are getting, with the player as one
  more chooser in the marketplace.
