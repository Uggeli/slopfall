# Economy & Property — a circulating model, and the staged path to it

*Planning artifact, not code. Companion to [`behavior.md`](behavior.md) — this is the deep
dive on the economy loop that document names as the highest-value gap.*

## 0. Why this exists

The 30-day soak settled what the economy actually is today: not a runaway collapse, but a
**frozen two-class system**.

- Coin total is stable (self-stabilizes ~1.0× over a month) — but only by accident: cost-of-living
  is a flat sink that floors at zero, so *being broke is free*. As residents hit zero they stop
  draining the town, the constant keeper-wage faucet catches up, and poverty pins at **~24% of the
  population**. It's an equilibrium nobody designed.
- Underneath the stable total, two pathologies: a **permanent trapped underclass** (residents have
  no income and no way up) and **unbounded accumulation at the top** (one keeper reached 60× the
  mean and was still climbing linearly — keepers earn but have nothing to spend on).

The cause is structural and maps onto three absences mapped in the basics pass:
1. **No ownership** — `Residency = {building, Role∈{Resident,Keeper}}` is occupancy, not property.
   Nobody owns anything; `HouseForSale` buildings are inert.
2. **No rent / housing cost** — residents pay nothing to live; the only outflow is cost-of-living,
   paid *to no one*.
3. **Commerce is taverns only** — `EatTavern`/`Socialize` move coin patron→keeper. Everything else
   is a faucet or a sink: non-tavern keepers earn a **conjured wage** ("their customers aren't
   simulated"), residents earn **nothing**, alms redistribute by charity.

This doc designs a **circulating** economy — coin flows in a closed loop bounded by recirculation,
not by an accidental floor — and stages its construction so each piece earns its place against the
soak before the next lands.

## 1. Design principles

1. **Money is conserved.** Every flow is a transfer between two ledgers. No conjuring (the fake
   keeper wage dies), no vanishing (cost-of-living becomes *buying food from a shop*, not deletion).
2. **Faucets and sinks are explicit, named, and few** — and they live only at the **off-map trade
   edge** and (optionally) at the Crown's mint. Everything inside the walls is redistribution.
3. **Recirculation bounds inequality, not a floor.** Wages-from-revenue, rent, and taxes pull coin
   back down from the top and push it toward the bottom. Poverty should be *earned and escapable*,
   not a trap held shut by a zero-floor.
4. **Affordance/obligation-shaped.** Jobs, shops, rent, and taxes are the Stage-A marketplace made
   concrete (behavior.md §3): buildings/persons *advertise offers* (Work, Buy, Hire) and *carry
   obligations* (Rent due, Tax due). Decisions flow through the same argmax — no new blocks in
   `OddSystem.Decide()`.
5. **Build only what the town can exercise.** Per behavior.md's proto discipline: abstract the
   parts the world can't yet test (goods as items wait for Things, Stage E), model the parts it can
   (coin flows, employment, obligations) now, and re-soak after every stage.

## 2. Actors & balance sheets

| Actor | Income | Outgoings | Notes |
|-------|--------|-----------|-------|
| **Resident** | wage from employer, alms | rent to landlord, food/goods at shops & taverns, poll tax | today: zero income. The underclass. |
| **Business** (shop/tavern/temple keeper) | sales to customers | employee wages, rent (if not owner), business tax, restock | today: tavern sales real; others conjured. |
| **Landlord** | rent from tenants | property tax, upkeep | **new role.** Owns ≥1 house/shop. Candidate: wealthy residents / nobility. |
| **Treasury** (town/Crown) | property + business + poll tax | guard wages, palace upkeep, (almshouse) | **new.** The macro balancer. Net ≈ 0 over a month by design. |
| **Guard** | wage from treasury | spends in town (rent, food, goods) | **new.** Recirculates public money; bootstraps the norm loop. |
| **Off-map trade** | restock payments from shops | goods into town | the **controlled sink** — coin that leaves represents real imports. |

## 3. The circular flow

```
                     ┌──────────────────── TREASURY ───────────────────┐
                     │  in:  property tax · business tax · poll tax     │
                     │  out: guard wages · palace upkeep · almshouse    │
                     └───▲────────▲──────────────────────────┬─────────┘
                  taxes  │        │ taxes                     │ wages
                         │        │                           ▼
        rent          ┌──┴──┐   ┌─┴────────┐               ┌──────┐
   RESIDENT ────────► │LAND-│   │ BUSINESS │ ◄── spend ─── │GUARD │
      ▲   │           │LORD │   └─┬──▲───┬──┘    (everyone) └──┬───┘
      │   │ spend     └─────┘     │  │   │ restock             │ spends
 wage │   └─────────────► ........│..│...▼.....................│ in town
      │                  (food,   │  │  OFF-MAP TRADE  ◄───────┘
      └──────── wage ─────────────┘  │  (controlled edge)
                                      └── wage ──► RESIDENT / GUARD
```

The loop closes: residents earn wages → spend at businesses → businesses pay wages, rent, and
taxes → landlords and treasury pay tax / fund guards → guards and landlords spend → back to
businesses. The **only** leak is restock-to-off-map (real imports), optionally balanced by Crown
minting or export income. Inequality is bounded because every upward flow (sales, rent) has a
matching downward flow (wages, guard pay, almshouse).

## 4. Components

### 4.1 Ownership & property — `OwnershipRegistry`
A building gets an **owner** entity (separate from its keeper/residents). Map both directions:
`owner → building[]` and `building → owner`. Assigned at load by policy:
- Shops/taverns: owned by their keeper (owner-operator) **or** by a landlord — a decision (§8).
- Houses: owned by a **landlord** (a wealthy resident or noble), tenanted by the 2 residents.
- Palace / public: owned by the **Treasury**.
`HouseForSale` becomes a real affordance later (a tenant who saves enough can buy — escape hatch
from rent). Building `Quality` (1–20, already on `BuildingRow`) scales rent and tax.

### 4.2 Employment & wages — `EmploymentRegistry`
Distinguish **where you live** (Residency) from **where you work** (Employment): `entity → {employer
business, wageRate}`. At load, assign residents to jobs by building demand (a tavern needs servers,
a shop a clerk/porter, the temple acolytes, plus generic town labor — deliveries, washing, portering).
Crucially, **wages are paid out of the business's cash-on-hand**, not conjured — a business that
isn't selling can't pay, which creates real economic pressure and ties wages to commerce. The fake
non-tavern wage faucet is deleted.

### 4.3 Commerce — shops actually sell
Today only taverns take money. Generalize: shops afford **Buy** (residents convert coin → need
satisfaction: a general store sells food for the larder, etc.). v1 is **abstracted** — `Buy` is a
coin transfer customer→business that satisfies a need, no item modeled — because Things are a later
stage (behavior.md Stage E). This already gives shopkeepers *real customers*, kills the conjured
wage, and gives residents somewhere to spend. The home **larder** (buy food → store → `EatHome`
consumes it → empty larder forces shopping) is the first natural item and the bridge to Stage E.

### 4.4 Rent — `RentSystem`
Tenants owe rent to their landlord on a cycle (weekly feels right at sim pace; monthly is coarse).
Rent scales with house `Quality`. Implemented as an **obligation**: a `RentDue` flag the marketplace
weighs (a resident behind on rent feels coin pressure → seeks work / cheaper living). Can't pay →
arrears → eventually eviction to `HouseForSale`/streets (a real downward path, and test material for
the request/charity and — later — crime loops). Rent is the **wealth-scaled sink** that recirculates
hoarded coin from the top toward landlords (and via their tax, the treasury).

### 4.5 Taxation & the tax man — `TaxSystem`
The macro redistributor, fired on the **`NewMonthSimEvent`** (already emitted by `TimeSystem`).
A **Tax Collector** assesses each taxable actor and transfers coin to the Treasury:
- **Property tax** — per owned building, scaled by Quality (hits landlords & owner-operators).
- **Business tax** — a cut of the month's revenue (hits profitable businesses hardest — the
  anti-hoarding lever).
- **Poll tax** — a small flat per-adult head tax (optional; regressive, so maybe skip or exempt
  the destitute — a design knob with social-texture consequences).

Start **abstract** (a monthly sweep), then make it **embodied** — a visible tax man walking
door to door, reusing the request-system's embodied-asking machinery. An embodied collector who can
be *refused* or *resented* is rich behavior fuel (and a future crime hook: tax evasion).

### 4.6 The treasury & the public sector — guards
The Treasury holds the town purse and pays salaries — first and foremost **guards**. This is where
the economic model and the *cognitive* roadmap converge: **funding guards from taxes is the
prerequisite for the norm/conscience loop** (behavior.md Stage D). Guards funded → guards patrol →
guards witness and punish transgression → "a kit watches the warren punish a transgressor →
consolidates a conscience." So the tax man isn't only an economic balancer; he's the install vector
for the OUGHT layer. The treasury should target **net ≈ 0 over a month** (taxes in ≈ wages +
upkeep out); persistent surplus or deficit is the calibration signal the soak will surface.

## 5. Data shapes (new / changed)

| Shape | Kind | Purpose |
|-------|------|---------|
| `OwnershipRegistry` | registry | building ↔ owner entity |
| `EmploymentRegistry` | registry | entity → employer business + wage rate |
| `TreasuryRegistry` | registry | town purse(s) — one global, or per-faction via `FactionId` |
| business cash + revenue-this-period | registry (extend) | so wages/taxes pay *out of revenue*, not thin air |
| `RentSystem` | system | weekly rent obligations tenant→landlord |
| `TaxSystem` | system | monthly assessment → treasury; the tax man |
| `PayrollSystem` (or extend `EconomySystem`) | system | treasury→guards, business→employees |
| `AffordanceCatalog` | data (extend `ActivityCatalog`) | Work/Buy/Hire offers per building kind (Stage A) |
| `ObligationRegistry` | registry | RentDue / TaxDue weights the marketplace reads |

Most of these are thin `ConcurrentDictionary` registries in the established single-writer style; the
new systems slot into the tick order after `EconomySystem`. **Stage A (the affordance refactor)
should land first** so jobs/shops/rent are table rows, not new `Decide()` blocks.

## 6. Conservation & calibration

The soak (`--soak`) is the test rig. Each stage targets specific metrics:

| Quantity | Today | Target after model |
|----------|-------|--------------------|
| Resident income | 0 | wage ≥ rent + subsistence on average |
| Broke (% pop) | ~24%, floor-trapped | low single digits, *escapable* |
| Gini | plateaus ~0.53 (accidental) | bounded by tax/rent recirculation, *designed* |
| richest/mean | 60×, climbing | bounded — profit pulled back by tax + wages |
| Coin total | self-stabilizes by accident | conserved by design; only off-map trade leaks |
| Treasury balance | n/a | net ≈ 0 / month; surplus or deficit = miscalibration |

New soak columns to add as the model lands: treasury balance, total rent paid, total tax collected,
total wages paid, evictions, guard headcount.

## 7. Staging

Each stage is behavior-soak-gated and, where possible, behavior-preserving against the 119 tests.

| Stage | Adds | Kills / fixes | Soak should show | Unlocks |
|-------|------|---------------|------------------|---------|
| **E0** | Stage-A affordance refactor; ledger-based `EconomySystem` (cash-on-hand per business) | conjured wage faucet (route through revenue) | unchanged behavior, conserved coin | everything below as table rows |
| **E1** | Resident **employment + wages-from-revenue** | the trapped underclass | broke % drops; bottom lifts | Buy has customers with money |
| **E2** | **Ownership + rent** | unbounded hoarding (rent recirculates) | Gini bounded by design; richest/mean falls | eviction → request/crime test material |
| **E3** | **Tax man + treasury + guards** | no public sector | treasury net ≈ 0; guards visible | **the norm/conscience loop** (Stage D) |
| **E4** | Real shop **stock + home larder** (Things) | abstracted Buy | larder-driven shopping rhythm | full goods economy (behavior.md Stage E) |

## 8. Open decisions (with a recommendation)

1. **Who are the landlords?** → *Recommend:* a small class of wealthy residents/nobles own the
   houses; shops are owner-operated (keeper owns). Keeps the actor count low while creating a real
   rentier flow. Alternative: the Crown owns everything and all rent is tax (simpler, less texture).
2. **One treasury or per-faction?** → *Recommend:* one global town treasury for v1; `FactionId` on
   buildings is there to split it later (temples vs Crown vs guild).
3. **Restock: off-map sink or internal?** → *Recommend:* controlled off-map sink (imports are real),
   balanced by a small Crown mint or export faucet so the town doesn't slowly deflate. Make both
   knobs explicit and soak them.
4. **Embodied tax man now or later?** → *Recommend:* abstract monthly sweep first (E3), embody once
   it works — embodiment reuses request-system machinery and is cheap to add after.
5. **Poll tax?** → *Recommend:* skip in v1 (regressive, would deepen the underclass we're fixing);
   revisit as a deliberate social-pressure knob.

## 9. Deliberately deferred

- **Items/Things** for goods (Stage E) — Buy stays abstract until then.
- **Banking/loans/interest** — the Bank building stays a flavor keeper for now.
- **Inter-town trade & caravans** — folded into the single off-map edge until multi-town.
- **Inheritance / business succession** on death — no deaths yet; revisit with lifecycle.
- **Crime-driven economy** (theft, fencing, bribes) — arrives *with* the norm loop that E3's guards
  unlock, not before.

---

**One-line summary:** replace the accidental two-class equilibrium with a designed circular flow —
residents earn wages from real businesses, pay rent to landlords, and everyone pays a tax man who
funds the guards — built in five soak-gated stages, with the affordance refactor first and the tax
man doubling as the install vector for the conscience layer.
