# Items & inventory — things that exist in the world

*Planning artifact. Companion to [`action_catalog.md`](action_catalog.md) (the verb vocabulary —
items activate its reserved `Take`/`Drop`/`Equip`/`Use` rows) and
[`decision_architecture.md`](decision_architecture.md) (senses→ads→choose — an item's atoms are the
data those ads read). Today goods are abstract fungible `Good` quantities; this introduces discrete,
atom-described items that **exist**: they can be carried, owned, perceived, stolen, given, inherited.*

## Why items: you can't steal an abstraction

A fungible `Good` is an *accounting entry* — you can consume it or move it between ledgers, but you
can't take it against someone's will, because there is no "it" and no one it belongs to. Theft (and
every object-mediated behavior after it) needs a thing to **exist**, which means three properties a
quantity can't have:

- **Persistence as itself** — survives the transfer, so it is *the stolen ring*, recoverable as that
  same object (not "any provisions back").
- **An owner** — a property of the object, so taking it *is* the wrong, intrinsically.
- **Perceivability** — an object that exists can be *seen carried*, *seen change hands*, *noticed
  gone*. A quantity can't be witnessed changing — which is exactly why `Steal` is invisible today
  (see [§ pulling behavior](#the-pulling-behavior-witnessed-theft--bounty)). Existence is the
  perception fix and the ownership fix at once.

This generalizes: loot, gifts, heirlooms, contraband, a bribe, a quest letter, evidence — all need
things to exist in this sense. Theft is just the first behavior that pulls it.

## The model: an item is a bundle of atoms

Do **not** author item *types* in code (no `Bread` class, no `Sword` class). An item is a bundle of
**atoms** describing what it is and what it affords — the same data-vocabulary the minds use ("ad is
ad" = `Preconditions(data) + uniform V`). Agents read those atoms through the existing
senses→ads→`V()` pipeline, so a *new item is a new atom bundle with no per-item code*.

| Atom family | Examples | Role |
|---|---|---|
| **Affordance** — what you can DO with it | `Edible(nutrition)`, `Drinkable`, `Wieldable(damage)`, `Wearable(slot, protection)`, `Readable`, `Burnable`, `Openable`, `Valuable(worth)` | generate **ads** (the CAN side) |
| **Property** — what it IS | `Weight`, `Perishable(freshness, decayRate)`, `Quality`, `Material`, `Ownable` | feed `V()` + economy + stacking |
| **Identity** — per-instance state | `Owner`, `Provenance(origin, tick)`, `Name`, `Wear`, `Enchantment` | make a *unique* unique; carry "stolen" / heirloom-ness |

An item is `template-ref + per-instance atom overrides` — the template is a named atom-bundle (shared,
not repeated); the instance adds/overrides only what differs (owner, freshness, wear).

### Location ≠ ownership (the heart of "stolen")

Two separate fields on every item:

- **`location`** — where it physically is: `CarriedBy(agent)` / `EquippedBy(agent, slot)` /
  `InBuilding(b)` / `OnGround(pos)` / `Destroyed`.
- **`owner`** (an `Ownable`-atom value) — whose it is.

Theft = **`location` moves, `owner` doesn't** → held-by ≠ owned-by *is* the stolen state, a thing the
fungible model literally cannot represent. Recovery = `location` returns to `owner`. **Inheritance =
`owner` → heir on death** — the same operation, which is why items serve both the theft thread and the
L2 lifecycle (`living_world_L2_lifecycle.md`: heirloom-to-heir vs escheat) on one substrate.

### Stacks fall out — stackability is derived, not declared

A *stack* is N items merged because their atoms **and state are identical**. You never tag a thing
"stackable"; the *absence of distinguishing atoms* makes it so:

- freshly-baked loaves stack; a 3-day-old loaf is a different stack (different `freshness`),
- a worn sword never merges with a pristine one (different `Wear`),
- a named/owned heirloom never stacks at all (it carries identity atoms).

So "fungible" = "an item whose atoms carry no instance identity," and a fungible quantity is just a
homogeneous stack. There is one model, not two.

## Integration: items ride the existing pipeline

The seam already exists. `AffordanceCatalog` maps *thing-kind → activities offered* and
`ActionDiscovery.Discover(agent, thing)` resolves a thing into the activities available to this agent
— the data-driven form of DFU's `PlayerActivate` dispatch (door→open, container→loot, book→read).
Items generalize it from **hardcoded thing-kind → verbs** to **read the thing's affordance atoms →
verbs**. Worked trace — a hungry agent and a loaf:

1. `SenseSystem` surfaces the item; `SubjectiveSystem` reads its affordance atoms.
2. `ActionDiscovery` sees `Edible` → emits an **Eat** ad (`Take` + `Sustain` on that item).
3. `OddSystem.V()` scores it: `hungerGap × nutrition-atom × modulators` — *the same uniform V*, no
   new branch; the atom is just more data feeding `Preconditions + V`.
4. argmax → the agent eats. Add "fish" (`{Edible:0.4, Valuable:0.03, Perishable}`) and it is found
   and eaten with **zero new code**.

This is what finally activates the reserved `Take`/`Drop`/`Equip`/`Use` rows — proto discipline is
satisfied because the world now exercises them. `Take` of an item whose `Owner ≠ self` is the crime;
`ChargeCriminalGuilt(thief, Take)` mirrors the existing `CombatSystem.ChargeViolenceGuilt` and the V()
conscience factor already penalises it. Giving an item = `Transfer`'s analogue (move `location` *and*
`owner`).

## Recognition, THINGS, and emergent categories

An item's atom-set **is a signature** — exactly what the deferred S3 **THINGS** store wants to
remember and recognise (today only the PLACES proto, `PlaceMemoryRegistry`, exists). It also means
item *categories can emerge*: `MeaningsSystem` MINT can cluster many `{Wieldable, Sharp, Metal}`
signatures into a learned "weapon" concept the agent forms itself, rather than one we author. Items
are the cleanest place to first exercise that signature machinery.

## Determinism

Replay-exactness is a hard constraint, so:

- a **fixed `AtomKind` enum** + a **stable ordering** over an item's atom set (never iterate a
  hash-bag in nondeterministic order),
- deterministic `ItemId` allocation — `hash(originEntity, tick, salt)` per the runtime-spawn
  convention,
- a single-writer `ItemSystem` owning the registries; ordered (`ItemId`-keyed) passes,
- snapshot-serialised like the other registries (no separate save path today).

## Registries (new)

- **`ItemRegistry`** — `ItemId → ItemInstance` (`ConcurrentDictionary`, single-writer, matching
  `StockRegistry` conventions). Source of truth.
- **Location indexes** — reverse views for queries: per-agent inventory (`agent → ItemId[]`),
  per-building contents. Derived, rebuilt deterministically.

## Bridge from the fungible economy (incremental, not big-bang)

The current four `Good`s are coarse *implicit* atom-bundles —
`Provisions ≈ {Edible, Valuable:0.02, Perishable}` — and `Wares` is a monolith lumping arms + cloth +
gems that atoms can finally **decompose** into real things. The economy is 153-green and soak-passing,
so migrate it flow-by-flow as a behavior pulls each, **not** in one rewrite:

1. Stand up `ItemRegistry` and put *significant* items (heirlooms/valuables) in it first — theft +
   inheritance pull this now, **zero economy change**.
2. Migrate fungible flows when a behavior pulls them. **Spoilage / the larder-consumption sink** (a
   deferred roadmap item) is the likely first puller — that is when food earns discreteness; convert
   `Larder` then `Stock` to hold item-stacks.
3. End state: the `double`-quantity arithmetic in `EconomySystem` (import / produce / export / B2C
   `PaySale` / B2B `Acquire` / `StealProvisions` / in-kind / `EatHome`) operates on stacks; the
   `Good` enum survives only as a derived view, if at all.

## The pulling behavior: witnessed theft → bounty

`ActivityKind.Steal` already exists (`EconomySystem.StealProvisions` moves provisions shop→larder),
but it is **invisible and consequence-free** — `SenseSystem` perceives only present agents, never the
act or the loss. Items close that, and the slice is the v1 justification:

1. **Theft becomes perceivable & owned** — the stolen thing is an item with `Owner ≠ thief`; a witness
   present at the act perceives it (carried-item perception). *(New event-perception link — the
   keystone gap.)*
2. **Grievance → standing bounty** — the victim posts a **persistent, discoverable** request: a new
   `BountyRegistry` row, the generalization of `RequestSystem` from "ask whoever is adjacent now" to
   "a request others find later." It names the item to recover.
3. **A taker resolves it** — discovers the bounty as a place-rooted ad, recovers the item
   (`location` → `owner`), is paid; the thief takes resentment (`RelationImpulseEvent` via
   `SocialSystem`) and a `Take` conscience charge. No combat — sanction is social + economic.

Witnessed-only for v1 (you can only grieve a thief you/​a witness saw) — it closes false-accusation by
construction; the unwitnessed/detective version is deliberately-richer v2. This section can spin out
to its own doc once items land.

## v1 scope

Thinnest cut that makes things exist and proves the loop: `ItemRegistry` + atoms
`{Edible, Valuable, Ownable}` + identity atoms; `Take`/`Drop` executors; **seeded** household
heirloom-valuables (not produced); carried-item perception; the witnessed-theft tie-in. **Economy
untouched. Combat untouched.** Reserve `Equip`/`Use`/`Open`/`Lockpick` until weapons / world-objects
pull them.

## Open questions

1. **Atom storage** — fixed struct with optional fields (fast, SoA-friendly) vs a sparse
   `(AtomKind, value)` set (flexible, ordering-sensitive). Determinism favours the former.
2. **`Good` enum's fate** — retired once flows migrate, or kept as a derived aggregate view for the
   economy's bulk math?
3. **Wares decomposition granularity** — how fine do we split the monolith (weapon/armour/clothing/
   gem), and does each map to a building-kind producer?
4. **Alms as item-gifts** — does `RequestSystem` charity become a `Give`-item (bread to a beggar)
   rather than coin, once items exist?
5. **Client dispatch parity** — how the eventual render client's `PlayerActivate`-style
   thing→verbs UI reads the same affordance atoms, so player and NPC resolve identically.
