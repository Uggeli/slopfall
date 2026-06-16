# Stage 3 — Per-settlement public finance

Follows Stage 2 (regional loader, `docs/regional_loader_stage2.md`). Part of the
regional-sim direction (load a whole region as one continuously simulated world).

**Decisive choice: Stage 3 is plumbing only — behavior-preserving.** The economic
*intent* (hamlets poor, low/no tax, few/no guards) and the F1 lender-of-last-resort
crown fix are deferred to a post-observation tuning stage, so we observe the current
model per-settlement first (Stage 4) and tune against data, not guesses.

## Goal & scope

Give each settlement its **own treasury**: it taxes its **own** residents into it, pays
its **own** guards from it, receives its **own** crown remittance. After this every
settlement is an isolated economic unit — which is what makes the Stage-4 regional soak's
per-settlement rows meaningful.

**In scope:** distinct per-settlement `OwnerId`; per-settlement tax collection;
per-settlement crown remittance; guard pay from own treasury (already wired in Stage 2).
Same rates/guard-counts/crown-logic as today → single-town stays bit-identical.

**Out of scope (next, post-observation tuning stage):** settlement-kind-scaled tax rate
+ guard count, F1 lender-of-last-resort crown. Still out: production profiles
(food/raw/wares), caravans.

## File-by-file changes

### `SettlementRegistry.cs`
- `Add(...)`: `Treasury = new OwnerId(100 + id)` — revert the Stage-2 `OwnerId.Town`
  placeholder. Single-town becomes `OwnerId(100)`: same single pool, different id, so
  amounts are unchanged.

### `EconomySystem.cs` — the two remaining `OwnerId.Town` sites
1. **Crown remittance** (`Update`, currently `crownMinted = guardPaid;
   Treasury.Add(OwnerId.Town, …)`): bucket `guardPaid` per owner during the agent loop
   (cleared `Dictionary<OwnerId,double>` keyed by `emp.PublicOwner`), then after the loop
   remit each settlement's own guard pay to its own treasury. `crownMinted` ledger total
   = sum (unchanged global tally).
2. **`CollectMonthlyTax`** (currently iterates all coin → `OwnerId.Town`): iterate per
   settlement — for each `s`, tax `s.Residents` (sorted by `EntityId`, F3) at the current
   global rate/exemption into `s.Treasury`. Single-town = one settlement = same residents
   → identical.

Guard pay needs no change — it already draws from `emp.PublicOwner` (= `s.Treasury` since
Stage 2); once that's distinct it routes correctly. `SumCoin` already includes
`Treasury.Total` (all owners), so conservation is unaffected.

## Verification gates

1. Build clean.
2. **150 tests green** (regional bleed test still holds).
3. **Single-town soak bit-identical** to baseline — proves the finance change is pure
   plumbing. The gate; divergence = a bucketing/tax-routing bug.
4. **Betony `--loadregion` still loads** (smoke).
5. **New gated test:** after `LoadRegion(Betony)` + a month stepped — (a) each
   settlement's treasury ≥ 0 and funded only by its own residents; (b) two identical
   regional runs produce identical treasury balances (determinism).

## Risks & edge cases

- **Crown per-owner bucketing** — must remit to the same owner the guards drew from.
- **Float-order in `Treasury.Total`/`SumCoin`** — more owners, still unsorted; fine for
  conservation tolerance, noted for Phase-2 bit-exact hashing.
- **Tax determinism** — sort each settlement's residents before taxing.
- **Empty settlements** (a 2-resident Farm) — tax ~0, treasury = seed; harmless.

## Sequencing

1. `SettlementRegistry.Add` → distinct `OwnerId`.
2. `EconomySystem` crown bucketing → gate 3.
3. `EconomySystem` per-settlement tax → gate 3 again.
4. Gated regional finance test → gate 5.

Then Stage 4 (regional soak) to observe; only after that, tune (kind-scaled tax/guards
+ F1 lender-of-last-resort).
