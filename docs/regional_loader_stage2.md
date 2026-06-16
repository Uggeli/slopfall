# Stage 2 — Regional load path into one combined coordinate space

Part of the regional-sim direction: load a whole Daggerfall region as one continuously
simulated world (see the soak findings in `economy.md`/`goods_economy.md` and the
two-tier "LOD the body, not the life" plan). Stage 1 (settlement data model) is landed;
this is Stage 2.

## Goal & scope

Load **all settled locations of a region** into one `SimulationContext`, one combined
`TownGrid`, one unified world-coordinate space, with every building and resident tagged
to its settlement. The economy still runs region-global (single `OwnerId.Town`) —
public-finance separation is Stage 3.

**In scope:** multi-location load, combined grid, coordinate offsetting, settlement
membership, per-settlement *structural* seeds (employment/knowledge), `RegionLoader`
+ a host entry to smoke it.

**Out of scope:** per-settlement treasury/tax/guards (Stage 3 — needs `EconomySystem`
edits), caravans/wilderness nav (v3), the regional soak verdict (Stage 4).

## Key design decisions

1. **Packing layout (v1):** shelf-pack settlements in *block* units into the combined
   grid — left-to-right, wrap to a new shelf when a row exceeds a max width (~32 blocks).
   Each settlement separated by a **≥1-block blocked buffer** so their walkable cells
   don't connect (no inter-settlement wandering before caravans exist). Real map-pixel
   positions are *stored* on `SettlementData` for v3 routing but do **not** drive v1
   placement. Betony packs tiny (~15×3 blocks ≈ 184k cells).

2. **Treasury stays global in Stage 2.** All settlements' `SettlementData.Treasury =
   OwnerId.Town`, and `SeedGuards` keeps using `OwnerId.Town` exactly as today. This is
   what preserves single-town behavior without touching `EconomySystem`. (Stage-1 tweak:
   `SettlementRegistry.Add` must set `Treasury = OwnerId.Town`, not `OwnerId(100+id)`.)
   Stage 3 flips to distinct `OwnerId`s together with the economy edits.

3. **Refactor, don't fork.** Extract the per-location block-walk + spawn into one
   reusable core; both single-town `Load` and `RegionLoader` call it.

## File-by-file changes

### `TownLoader.cs` — extract a placeable core (the critical refactor)

New private core writing one location into a *provided* grid at a *block offset*,
tagging a settlement:

    static void LoadLocationInto(SimulationContext ctx, in DFLocation location, BlocksFile blocksFile,
                                TownGridData grid, int blockOriginX, int blockOriginY, SettlementData s)

- Same `y,x,subrecord` walk order as today (preserves RNG/EntityId order → determinism).
- Cost copy target offset by `(blockOriginY+y)*cells` rows, `(blockOriginX+x)*cells` cols
  into combined `grid.Cost`; gates at `(blockOriginY+y)*grid.BlocksWide + (blockOriginX+x)`.
- Building world coords add the origin; `BlockX/BlockY` = **global** combined-block coords
  (so the coarse pathfinder works per-settlement).
- Each `ctx.Buildings.Add` → `s.Buildings.Add(idx)`; `SpawnCivilians` records each spawned
  id → `s.Residents.Add(id)`.

`SpawnCivilians`/`Spawn` gain a `SettlementData s` param. `Load` (single-town) becomes a
thin wrapper that **must produce identical output**:

    var s = ctx.Settlements.Add(location.Name, location.RegionName, KindOf(location));
    var grid = NewGrid(width, height, location.RegionIndex);
    LoadLocationInto(ctx, location, blocksFile, grid, 0, 0, s);
    ctx.TownGrid.Set(grid);
    SeedKnowledge(ctx, s); SeedEmployment(ctx, s); SeedGuards(ctx, s); // per-settlement
    SeedStock(ctx);                                                    // global — unchanged

Seeds refactored to take a `SettlementData` and iterate `s.Residents`/`s.Buildings`
instead of `ctx.Residency.All`/`ctx.Buildings.All`. For a single settlement this is the
same set in the same sorted order → identical. `SeedGuards` still uses `OwnerId.Town`.

New helpers: `KindOf(DFLocation) → SettlementKind`, `NewGrid(w,h,regionIdx)`.

### `RegionLoader.cs` (new)

    public static RegionLoadResult LoadRegion(SimulationContext ctx, MapsFile maps, BlocksFile blocks, string regionName)

- **Pass 1:** enumerate region locations, `GetLocation` each, keep only settled types
  (City/Hamlet/Village/Farm/Temple/Tavern — skip dungeons/graveyard/coven/ships). Store
  `DFLocation` + `w×h` + map-pixel pos (`LongitudeLatitudeToMapPixel`).
- Compute packing → each settlement's `(blockOriginX, blockOriginY)` + combined size.
- Allocate one combined `TownGridData`.
- **Pass 2:** `ctx.Settlements.Add(...)`, set origins/pixel/dims, `LoadLocationInto(...)`
  at offset, per-settlement seeds. `ctx.TownGrid.Set(combined)`; return totals.

### `SimBoot.cs` + `Program.cs`
- `SimBoot.CreateRegion(arena2, regionName, timeScale, seed)` mirroring `CreateTown`.
- `Program.cs`: `--loadregion <region>` smoke command (per-settlement counts).

### `SettlementRegistry.cs` (Stage-1 tweak)
- `Add(...)`: `Treasury = OwnerId.Town` (placeholder; Stage 3 → distinct).

## Verification gates (must pass, in order)

1. Build clean.
2. **All 149 tests green** — single-town `Load` refactor is behavior-preserving.
3. **Single-town soak bit-identical** — Gothway Garden 30-day vs baseline. Non-negotiable.
4. **Betony smoke** (`--loadregion Betony`): settlement count == settled-location count;
   Σ civilians == total; Whitefort ≈ its solo load (~113 buildings).
5. **Gated test** (ARENA2-guarded): after `LoadRegion(Betony)` — (a) every resident's
   PlaceMemory ⊆ their settlement's buildings; (b) no employee employed across settlements.

## Risks & edge cases

- **RNG/order determinism** — rests on `LoadLocationInto` keeping the exact walk order; gate 3.
- **Empty/failed locations** — skip gracefully in Pass 1.
- **Grid scale** — fine for Betony; flagship needs multi-res nav (v3).
- **Coarse pathfinder across buffers** — buffer = 0-cost blocks, impassable (`Walkable` is `Cost>0`).
- **Region-iteration API** — confirm `GetRegion`/`MapNames` shape during implementation.

## Sequencing within Stage 2

1. Stage-1 `Treasury = OwnerId.Town` tweak.
2. Refactor `TownLoader` (core + wrapper + per-settlement seeds) → gates 1–3.
3. Add `RegionLoader` + `SimBoot.CreateRegion` + `--loadregion` → gate 4.
4. Add the gated regional test → gate 5.

Step 2 lands and verifies the risky refactor *before* any regional code exists.
