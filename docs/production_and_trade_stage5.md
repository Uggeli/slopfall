# Stage 5 — Production & Trade

Follows the regional loader (Stages 2–4). The Stage-4 Betony soak showed the root
problem: settlements have almost no industry (Whitefort = **one armorer** for 286 people),
so 271 laborers have nothing to produce, food is *imported* (a sink), and consumer towns
deflate. This stage gives laborers real workplaces and turns production into circulation
+ export income.

## The economic model

```
  Workplaces (laborers: employed or self-employed)        General Store = MARKET HUB
  ───────────────────────────────────────────────        ─────────────────────────────
  Farmland / Fishery   ──produce Food──►                  • buys local production wholesale
  Craft workshops      ──produce Wares─►   ──────────►    • resells locally (B2C)  ── inner circulation
  (mines, etc. later)                                     • EXPORTS surplus off-map ── money IN (the faucet)
                                                          • IMPORTS only deficits   ── money OUT (exotic only)
                              ▲                                      │
                              └──────── residents buy food/goods ◄───┘
  Guilds = faction economic actors: own treasury (OwnerId), collect dues, pay their own,
           and ARE TAXED by the settlement like any coffer.
```

Key principles (from the design dialogue):
- **Money enters by exporting; it stays in inner circulation.** A town has *one* trade
  edge to the outside — its general stores — not one pipe per building.
- **General stores are the trade network.** They buy local output, resell it locally,
  export the surplus, and import only what isn't produced locally.
- **Food is local everywhere.** Farms (and coastal fishing) produce food; importing food
  is the exception, not the rule. This kills the import sink that drives deflation.
- **Prices follow supply & demand.** Abundant local goods are cheap; scarce/exotic
  (imported, low supply) are expensive — emergent, not authored.
- **Three employment modes:** owner (keeper), employee (wage), self-employed (owns a
  micro-workplace — a croft, a boat — and sells output to the store).
- **Guilds run their own economy and are taxed.**

## Phasing (each observable in the regional soak)

- **5a — Food loop.** Settlement farmland (+ coastal fishing) employs laborers to produce
  Food; the general store buys it, resells it, exports the surplus, and stops importing
  food. The complete local food circulation. *Biggest single fix — kills the food sink,
  gives laborers work, gives consumer towns an export.* Also fixes the bug where tiny
  Farm locations become all-guards.
- **5b — Wares & full hub.** Route craft wares through the store too (buy local, export
  surplus); generalize the market-hub logic; deprecate per-building off-map trade.
- **5c — Employment overhaul.** Assign laborers to workplaces by capacity/proximity
  (not round-robin to shopkeepers); formalize self-employment.
- **5d — Guild economies.** Guild `OwnerId` treasuries; dues → guild coffer; guild pays
  its steward; settlement taxes guild coffers.
- **5e — Dynamic supply/demand pricing.** Local price = f(local stock, local demand);
  exotic = expensive.
- **5f — Settlement-kind scaling + F1 + fishing polish.** Poor hamlets / low tax / few
  guards; crown lender-of-last-resort; coastal-detected fisheries.

## Phase 5a in detail — the food loop

**Goods.** Keep it minimal: farming AND fishing both produce **Provisions** (food) — same
good, different workplace flavor. Drink/Wares unchanged for now.

**Workplaces (settlement hinterland).** Rather than require travel to separate Farm
locations (that needs caravans, v3), give each settlement **farmland capacity** — its
surrounding hinterland — sized by population/region. Laborers work it (a `Farm`/`Labor`
activity) and produce Provisions. (Coastal settlements get a fishery producing the same
Provisions; coastal detection can be a fast-follow if terrain lookup is fiddly.)

**The loop (where coin moves):**
1. A laborer farms → produces Provisions → sells the harvest to the local general store at
   the **wholesale** price → laborer earns coin (self-employed primary producer).
2. The store resells Provisions to residents (B2C, existing `PaySale`) at **retail**.
3. The store **exports** Provisions above local demand off-map (money IN).
4. The store **imports** food only if local production < demand (should be rare).

Net: residents' coin → store → farmers → (farmers spend at store/tavern) → circulates;
money enters via exported food surplus. No more minted food imports.

**Code changes (5a):**
- `GoodsCatalog` / a new `SettlementProfile`: farming production rate (Provisions per
  labor-minute) by settlement kind/region; mark Provisions as locally produced.
- `EconomySystem`: a production pass — laborers doing farm work add Provisions to the
  settlement's general-store stock and are paid the wholesale value (replaces today's
  round-robin Labor wage from a shopkeeper's purse). General-store `RunBusiness` changes:
  buy local Provisions, export surplus, import only the deficit (was: import staples).
- `TownLoader.SeedEmployment`: laborers without a craft/service employer default to
  **farmland work** (self-employed) instead of being assigned to an over-subscribed keeper.
- Fix Farm locations (`HomeFarms`) so their residents are **farmers**, not the accidental
  2 guards (min-guard rule shouldn't apply to tiny settlements — preview of 5f scaling).

**Verification (regional soak on Betony):**
- Whitefort's poverty/broke **drops sharply** (laborers now earn from food).
- Food import → ~0; Provisions on shelves sustained by local production.
- A real export faucet appears that isn't the crown drip; 2nd-half money-supply drift
  shrinks toward stable.
- Conservation still holds; two-run determinism holds.
- Single-town soak will **change** (expected — this is a behavioral fix); assert the new
  shape (less deflation), not bit-identity.

## Risks

- **Coupling:** 5a touches production + employment + store trade together (they're one
  loop). Keep wares/guilds/pricing out of 5a to bound it.
- **Cashflow:** the store must afford to buy the harvest — its coin comes from B2C +
  exports; seed enough opening stock/coin so the loop can start.
- **Balance:** production/consumption/export rates are placeholders; tune against the soak
  (the soak is the rig). Don't pre-tune.
- **Determinism:** the new production/sale passes must walk sorted (F3) like the rest.

## Out of scope (later)

Caravans / physical inter-settlement trade (v3); mines and other primary industries;
the two-tier life/embodied LOD split (Phase 2 of the big plan).

## Implementation status (landed)

The core production-&-trade loop is implemented and verified:

- **`BuildingKind.Farm`** synthesized per settlement at load (`TownLoader.SeedFarmAndEmployment`),
  anchored at a reachable spot (the general store).
- **Goods**: farms produce `Provisions`; general stores import only `Drink` and source
  food locally (B2B from the farm). Food import sink is gone.
- **Workplaces + employment**: a settlement's laborers are employed at its farm; the
  farm's harvest scales with the hands working it (`_farmWorkers`), capped by
  `FarmCapacity` — the production throttle that bounds the export faucet.
- **Income distribution**: the farm pays its hands an even share of its till
  (`FarmWagePayoutFraction`), breaking the single-keeper wage bottleneck.
- **Settlement-kind guard scaling** (`GuardCountFor`): cities guarded, hamlets token,
  villages/farms/temples/taverns none — fixes the tiny-farm-all-guards artifact and cuts
  the crown faucet.
- **F1 crown = lender of last resort**: the (tax-filled) treasury funds guards; the crown
  mints only the shortfall. Tax recirculates instead of hoarding.
- **Civic dividend** (`DistributeTreasury`): each settlement's treasury is paid back to
  its residents (poor relief) — the recirculation that counters concentration.

**Verification**: 153 tests green; the 30-day **regional Betony soak passes** (money
supply stable, conserves, deterministic — two runs identical). Food is produced locally,
laborers do real work for real income, stores trade, money circulates and recirculates.

**Known / expected gaps** (future work): a single *isolated* hamlet still deflates — a
consumer hamlet isn't a viable closed economy (the Stage-5 premise; the region is the
unit). Residual high poverty/Gini in poor rural Betony — the real lever is **more
workplaces** (fishing for the island, mines, etc.) so laborers aren't underemployed,
plus dynamic supply/demand pricing (5e) and richer import sinks. Still to do: 5b full
wares-through-store hub, 5c formal self-employment, 5d guild own-economy + taxation, 5e
dynamic pricing, fishing, caravans (v3).
