# Industry layers — a tiered production graph for the goods economy

*Planning artifact. Replaces the flat four-`Good` model + "`Wares` produced from nothing" with
**raw → material → finished** tiers, each industry a **recipe** (inputs + labor → output). Companion to
[`goods_economy.md`](goods_economy.md) (the supply chain this deepens),
[`production_and_trade_stage5.md`](production_and_trade_stage5.md) (the food loop this generalizes),
[`items_and_inventory.md`](items_and_inventory.md) (a good IS an item — this resolves its open
question 3, the `Wares` decomposition), and [`action_catalog.md`](action_catalog.md) (crafting is
`Work` consuming inputs → producing an output item).*

## Why

`Good` today is four lumps — `Provisions, Drink, Wares, Ore` — and craft `Wares` is **produced from
nothing**: a keeper "works" and stock appears. That can't carry a real economy — no material scarcity,
no inter-industry dependency, no reason for specialist towns to trade, and `Wares` hides the actual
things people want (clothes, tools, weapons, furniture…). The Gothway Garden soak showed the symptom:
a food-only town with nothing else to make **deflates**, because its one product is eaten in-kind and
never sold or exported. The fix is the layer the docs deferred ([`goods_economy.md`](goods_economy.md)
§10 "crafting recipes", items doc Q3): **real production chains.** Value compounds up the tiers (raw
cheap → finished dear), each step is a building's recipe, and a town's **surplus finished goods are the
export faucet** that makes it solvent.

## The model

Goods form a **production graph**. Each industry is a node with a **recipe** — it consumes input
good(s) + labor and produces an output good — so production is **gated on its inputs** (a weaver with no
wool makes no cloth; it pulls wool from a pasture, B2B). The **general store stays the trade hub**
([`production_and_trade_stage5.md`](production_and_trade_stage5.md)): it aggregates local output, sells
B2C, exports the surplus (money IN), imports only what the chain can't make locally (money OUT). Every
good is an **item** ([`items_and_inventory.md`](items_and_inventory.md)) — raw/material carry
`Material` + `Weight`; finished carry their affordance (`Edible`/`Wearable`/`Wieldable`/`Drinkable`) +
`Valuable`. So the goods taxonomy *is* the item taxonomy, and recipes move items.

## The tiers

| Tier | Goods (priority set; expand later) | Producer |
|---|---|---|
| **Raw** — extracted from the land (primary) | Grain, Fish, Wool, Hide, Ore, Wood, Hops, Gems, Herbs | Farm, Fishery, Mine + synth Pasture, Forest |
| **Material** — raw transformed (secondary) | Flour, Cloth, Leather, Metal, Lumber, Ale | synth Mill, Weaver, Tanner, Smelter, Sawmill, Brewery |
| **Finished** — consumer goods (tertiary) | Bread, Clothes, Shoes, Tools, Weapons, Armor, Furniture, Books, Potions, Jewelry | Baker, ClothingStore, WeaponSmith, Armorer, FurnitureStore, Bookseller, Alchemist, GemStore |

The current four migrate in: **Provisions** → the food chain (Grain/Fish → Flour → Bread/meals);
**Drink** → **Ale** (Brewery: Hops + Grain → Ale); **Wares** → the finished crafts, decomposed; **Ore**
stays raw (feeds Metal).

## The recipes (the graph)

- Grain →(Mill)→ Flour →(Baker)→ **Bread** — eaten
- Wool →(Weaver)→ Cloth →(ClothingStore)→ **Clothes** — worn
- Hide →(Tanner)→ Leather →(Cobbler/Armorer)→ **Shoes / Armor**
- Ore →(Smelter)→ Metal →(WeaponSmith)→ **Weapons** · (Armorer + Leather)→ **Armor** · → **Tools**
- Wood →(Sawmill)→ Lumber →(FurnitureStore)→ **Furniture**
- Hops + Grain →(Brewery)→ **Ale** — drunk
- Herbs →(Alchemist)→ **Potions** · Gems + Metal →(GemStore)→ **Jewelry**

## Data model

A **recipe table** — `buildingKind → { output, inputs[], laborRate }` — generalizing today's
`GoodsCatalog.Produces` (which outputs from nothing), with `B2BNeeds` **derived from a recipe's inputs**
(a producer must source the inputs it doesn't itself make). The production pass in
`EconomySystem.RunBusiness` becomes: *have the inputs (in stock, sourced B2B) → consume them + scale by
the hands working → produce the output*; no inputs → no output — the dependency that drives trade.
Multi-input supported (armor = metal + leather). Authored data, run in the existing **sorted**
production pass → deterministic.

## Buildings: synth the missing tiers, like the Farm

DF's block data gives us shops (`ClothingStore`, `WeaponSmith`, `Armorer`, `FurnitureStore`,
`Bookseller`, `Alchemist`, `GemStore`) — these become the **finished**-good producers, ~1:1 with the
recipe table. It does **not** give us mills, weavers, smelters, pastures or forests — so we
**synthesize them as hinterland workplaces**, exactly as Stage 5 synthesizes the `Farm`
(`TownLoader.SeedFarmAndEmployment`): a settlement gets the workshops its region's raw materials
support, and laborers staff them. This keeps the layering independent of the DF building set, and gives
the underemployed laborers (the soak's ~319 farm/fishery hands all splitting two tills) **layered
work**.

## How it lands — generalize the food loop, one chain at a time

Stage 5 already runs the prototype: Farm → store hub → consumer + export. Every chain works the same
way; we generalize it, not special-case:

1. **Recipe model** — the `{output, inputs[], labor}` table + the input-gated production pass;
   `B2BNeeds` derived from recipe inputs.
2. **Decompose the goods** for the proof chain (add the new `Good`s; migrate flow-by-flow — never
   big-bang the enum, per the items doc's economy bridge).
3. **Synth the workshops** for the proof chain (Pasture, Weaver).
4. **Proof chain end-to-end: Wool → Cloth → Clothes.** Pasture produces wool → weaver buys wool (B2B),
   makes cloth → ClothingStore buys cloth (B2B), makes clothes → store sells B2C + exports surplus.
   Verify in the regional soak (the new goods-flow + professions metrics read it directly): the chain
   produces/sells/exports, the workshop hands earn, a non-food town gets a faucet.
5. **The rest are table rows** — Drink (Hops→Ale, the brewery), Tools/Weapons/Armor (Ore→Metal→…),
   Furniture, Leatherwork, etc. — no new code, just recipes + synth workshops.
6. **Region-wide + caravans** — with intermediates real and towns specialized (a wool valley sells
   cloth to a smith town), the off-map import edge resolves to **another settlement's surplus**
   ([`goods_economy.md`](goods_economy.md) §10); caravans are the embodied haulage on top.

## Why this fixes the soak's findings

- **Deflation** — a town's surplus *finished* goods (clothes, tools, ale) are an export faucet; it
  earns coin instead of only bleeding it to imports.
- **Inequality** — laborers spread across layered workshops (weaver, smelter, baker…) instead of 300+
  hands splitting two farm tills; each tier's value-add pays the hands working it.
- **The items pass** — finished goods are the carriable, ownable, stealable things the items model
  wants; stealing a *tool* or a *bolt of cloth* (not only bread) becomes meaningful, and a
  finished good's atoms (`Wearable`, `Wieldable`) finally get exercised.

## Open / deferred

Recipe **ratios** (2 wool → 1 cloth) and **byproducts** (multi-output); **quality** tiers
([`goods_economy.md`](goods_economy.md) §10); the full DF building catalog (baker/cobbler aren't DF
kinds — fold into the nearest shop or synth one); demand-driven recipe selection; caravans / route-risk
(v3).

## v1 scope

The recipe data model + the input-gated production pass + the **Wool → Cloth → Clothes** chain
end-to-end (its goods, its synth workshops), verified in the regional soak. Everything else — drink,
metalwork, furniture, the other raws/materials — is reserved as recipe-table rows that drop in with no
new code. The economy migrates **flow-by-flow**; the four-`Good` model coexists until each chain pulls
its decomposition.
