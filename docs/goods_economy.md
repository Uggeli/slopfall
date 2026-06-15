# Goods economy — real things, a supply chain, and value

*The production/trade layer that makes the economy real. Supersedes the abstract `Buy` (coin → a
need-number) with actual goods that flow between businesses and give things value. This is Things /
E4 pulled forward — because, as the design crystallised, an economy of abstract need-reductions can't
work: a tavern needs real things to sell, and shops are where those things come from. Companion to
[`economy.md`](economy.md) (stages) and [`odd_spec.md`](odd_spec.md) (Smart-Object Things).*

## 0. Why
Abstract `Buy` is just a number going down — no value, no scarcity, no inter-business trade. Real
goods fix that: a thing is worth what someone will trade for it, and value compounds along a chain
(raw → retail → prepared). It also unblocks three roadmap items that were waiting on Things: the
**ODD action-chains** (O2: buy-ingredients → cook → sell is a real plan), the **smart-object Flags**
(O4), and the **circular flow** balancing (consumption becomes real spending that recirculates,
instead of a coin-burn).

## 1. The chain
```
off-map trade ──(import, $ out)──► SHOPS ──► consumers (buy provisions/wares, $→shop)
                                     │
local craftsmen ──(make wares)──► shops    └──► TAVERN ──(buy provisions, $→shop, B2B)
                                                  │
                                                  └──► consumers (buy prepared meals, $→tavern)
```
Two trade directions, both real (coin moves *and* a good moves): **B2B** (tavern buys provisions
from a store; a store buys wares from a craftsman) and **B2C** (you buy bread from the store, a meal
from the tavern). The tavern *transforms* provisions into meals — that markup is value created.

## 2. Goods (items)
A small authored set to start — expand later:
- **Provisions** (food/raw) — imported by general stores; bought by consumers and taverns.
- **Drink** (ale) — imported/brewed; sold by taverns.
- **Wares** (clothing, arms, sundries, gems…) — produced by local craftsmen, sold by their shops.

## 3. Stock & supply  *(decision: imports + local craft)*
- **`StockRegistry`**: each building holds `{good → quantity}`.
- **Imports** — general stores restock provisions/drink from the **off-map trade edge**: pay coin
  *out of town* (the controlled leak), gain stock. This is where most raw goods originate (urban
  towns have no farms).
- **Local craft** — craftsmen (smith, clothier, alchemist…) **produce wares** by working at their
  shop (Work adds stock from nothing / from cheap materials), then sell them.
- **B2B restock** — a tavern buys provisions from a store (coin → store, stock → tavern).

## 4. Trade & value  *(decision: authored base price + chain markup)*
- Each good has an **authored base price**. Markup is applied **down the chain**: import < wholesale
  < retail, and transformation adds value (provisions price < meal price). Predictable and tunable;
  no market-clearing simulation (emergent supply/demand pricing deferred — spiral risk).
- A purchase is a **transaction**: buyer pays `price`, seller's stock −1, buyer's need satisfied
  (or item enters buyer inventory once agents carry goods).

## 5. Services (institutions sell, not just goods)
Temple/guild/bank don't sell goods — they sell **services**, priced the same way:
- **Temple** — healing, cure-disease, blessings (paid when sick / seeking piety).
- **Guild** — training, quests, dues.
- **Bank** — deposits, loans, fees/interest.
A service transaction = pay the institution, receive the effect (a need met, a skill trained, etc.).

## 6. Who funds whom  *(the answer)*
Every building earns from **what it provides**; only the genuine commons rides on the crown:

| Building | Funded by |
|---|---|
| Shops | goods sold (B2C + B2B) |
| Taverns | meals/drink sold (bought provisions from shops) |
| Temple | **services (healing/cure/blessing) + alms/donations** |
| Guild | services (training, quests, dues) |
| Bank | services (deposits, loans, fees) |
| **Palace / guards** | **the crown — taxes (E3); the only true public sector** |

So institutions aren't special-cased — they're service-businesses; temples additionally take alms;
the tax man funds *only* defense/public order. This finally answers "who funds the temple": a **mix**,
led by the services it provides, topped up by alms, with the crown reserved for guards/palace.

## 7. Money edges (how it balances)
With real goods, **consumption becomes real spending** (coin → shops/taverns, recirculated) rather
than the flat cost-of-living *burn* — so that sink mostly goes away. The remaining edges:
- **Out:** net imports (coin leaving for off-map goods).
- **In:** **exports** (off-map buys local wares/surplus) and/or a small **crown mint** (tax-funded
  public spending). E3's treasury is where this nets out.
The loop is then mostly conserved internal transfers, with foreign trade the controlled edge — which
is why real goods make the economy actually balance (tuned once everything's in).

## 8. Data shapes
| Shape | Purpose |
|---|---|
| `Good` (enum) + `GoodsCatalog` | item kinds + authored base prices/markups |
| `StockRegistry` | building → {good → qty} |
| `Trade`/`PriceOf` in `EconomySystem` | the priced transaction primitive (extends the ledger) |
| `Buy` (rework) | now draws from a shop's stock at its retail price (real, not abstract) |
| `Restock`/`Produce` activities | import / craft into stock |
| service transactions | temple/guild/bank, priced like goods |

## 9. Build order (mechanisms first, tune last — [[no-premature-tuning]])
1. **G1** Goods + Stock (items, `StockRegistry`, `GoodsCatalog` prices).
2. **G2** Supply — shop imports (off-map), craftsmen produce wares.
3. **G3** B2C — rework `Buy` to draw real stock at retail price; tavern sells meals from stock.
4. **G4** B2B — taverns buy provisions from stores; stores buy wares from craftsmen.
5. **G5** Services — temple/guild/bank sell services (+ temple alms).
6. **G6** Edges — off-map import/export balance; fold into **E3** (crown/treasury/tax + guards).
7. **Then** tune prices/markups/import rates against the soak, all at once.

It interleaves with E3 (the crown side of the edges + public sector). Together, G1–G6 + E3 are the
full circular economy; the abstract `Buy`/cost-of-living placeholders retire as the real ones land.

## 10. Deferred
Agents carrying item inventories (vs needs satisfied on purchase); emergent supply/demand pricing;
crafting recipes/quality; banking depth (interest/loans as real mechanics).

**Emergent trade networks (future).** The "off-map import edge" is deliberately a *seam*, not a wall.
Today a general store importing a good it lacks pays an abstract off-map source. Later that source
resolves to **another town's surplus** — a store imports the missing good *from the nearest town that
has it*, paying that town's price + distance. Then trade routes, price gradients, and shortages
**emerge** from many local restock decisions instead of being authored: a glut here lowers a price
that pulls imports from there. The single off-map edge is the placeholder that becomes the inter-town
market once multiple towns are simulated together (ties into the multi-town/LOD work). Caravans/
hauliers and route risk are the embodied layer on top of that.
