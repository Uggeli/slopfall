# Region POI Unified Model — Design

**Date:** 2026-06-20
**Status:** Approved, ready for implementation plan
**Scope:** Render/place *all* region locations (not just the 6 settled types) behind a single unified POI model that captures each location's purpose, so per-POI simulation can hang off it later. This pass is **render-only** for the newly-added POIs; no simulation behavior.

## Background

The region loader currently simulates and renders only 6 "settled" location types — `TownCity`, `TownHamlet`, `TownVillage`, `HomeFarms`, `ReligionTemple`, `Tavern`. Every other Daggerfall location type in a region is enumerated, then discarded at the `IsSettled()` filter (`Assets/Sim/World/RegionLoader.cs:50`). The ignored types are:

- `DungeonLabyrinth`, `DungeonKeep`, `DungeonRuin`
- `Coven`
- `ReligionCult`
- `Graveyard`
- `HomeWealthy`, `HomePoor`
- `HomeYourShips` (player ship — excluded from this work)

Two facts make this tractable:

1. **The renderer is already type-agnostic.** `TownLayout.ResolveRegion` / `AddLocation` (`Headless/Sim.AssetExport/TownLayout.cs:75`) walks *any* location's RMB exterior blocks and emits model placements. It does not care whether the location is a city or a dungeon.
2. **The full location list is already enumerated.** `RegionLoader` iterates `region.MapNames[]` over `region.LocationCount` (`RegionLoader.cs:46`) and already records each settled location's real `MapPixelX/Y` from `MapsFile.LongitudeLatitudeToMapPixel(...)` (`RegionLoader.cs:99`). The geo positioning that turns map pixels into world offsets lives in `Headless/Sim.Web/Program.cs:74-138`.

So the missing piece is not rendering capability or data access — it is a **structure**: a per-POI record that carries each location's purpose, with rendering as its first consumer and simulation as the intended future one. This is the project's larger goal — reviving Daggerfall's cut living-world simulation — so the model is built to be the long-term single source of truth for "what is in a region," not a throwaway render hack.

## Goals

- A single unified POI model is the root for **every** location in a region. Settlements are a subset of POIs, not a parallel concept.
- Non-settled POIs are placed in the regional view at their true geographic map positions (render-only).
- Each POI carries an explicit semantic `Role` and a documented intended simulation purpose, giving future per-POI sim work a clean `Role`-keyed attach point.
- **The working settled economy is not destabilized.** `SettlementData` and all of its consumers continue to compile and behave identically.

## Non-Goals (this pass)

- Any actual simulation behavior for the new POI types (no monsters in dungeons, no witches in covens, etc.).
- Dungeon / building **interiors** (those are a separate DFU code path: `DaggerfallDungeon`, `DaggerfallInterior`).
- Visible map **markers** for geometry-light POIs whose exteriors render little/nothing. Recorded as a follow-up, not built here.
- `HomeYourShips` / `PlayerShip` — excluded.

## Design

### 1. Unified model — `RegionPoi` as the root

New file: `Assets/Sim/Registries/PoiRegistry.cs` (sibling to `SettlementRegistry.cs`).

**`RegionPoi`** — one entry per location in a region:

| Field | Type | Notes |
|---|---|---|
| `Name` | `string` | DF location name (the `region.MapNames[i]` key). |
| `MapId` | `uint`/`ulong` | From `RegionMapTable` (stable identity). |
| `LocationType` | `DFRegion.LocationTypes` | Raw Daggerfall type. |
| `Role` | `PoiRole` | Our semantic classification (see below). |
| `MapPixelX`, `MapPixelY` | `int` | From `LongitudeLatitudeToMapPixel(...)`. |
| `OriginX`, `OriginY`, `OriginZ` | `float` | World geo offset (filled by the positioning pass). |
| `BlocksWide`, `BlocksHigh` | `int` | Exterior dimensions. |
| `HasExterior` | `bool` | `false` when `ExteriorData.Width/Height <= 0`. |
| `Settlement` | `SettlementData?` | **Non-null only for settled POIs.** The existing economic object, unchanged. |

**`PoiRole`** enum — the semantic spine spanning every type:

```
City, Hamlet, Village, Farm, Temple, Tavern,   // settled (carry a Settlement)
Dungeon, Coven, CultShrine, Graveyard,          // non-settled
ManorWealthy, HovelPoor,
PlayerShip                                       // recorded but excluded from load/render
```

**`PoiRoleOf(DFRegion.LocationTypes) → PoiRole`** — the single classification switch. The `Dungeon*` triplet collapses to `Dungeon` (sub-type still available via `RegionMapTable.DungeonType` if needed later).

### 2. Settlements reparent under POIs (the safety mechanism)

`SettlementData` is **not** rewritten. The world/region model that today exposes a settlement collection gains a `IReadOnlyList<RegionPoi> Pois`, and the existing settlement accessor becomes a **derived view**:

```
Settlements => Pois.Where(p => p.Settlement != null).Select(p => p.Settlement)
```

Every current consumer of the settlement list keeps compiling and behaving identically — they read the same `SettlementData` instances, now reached through their POI. This is what makes "unified model" (option A) safe to adopt without economy churn.

### 3. Loader — `RegionLoader` builds POIs first

Replace the single settled-filter pass with:

1. **Enumerate all locations.** For each `i` in `region.LocationCount`, `GetLocation(regionName, region.MapNames[i])`. Build a `RegionPoi`: classify `Role` via `PoiRoleOf`, record `MapPixelX/Y`, `BlocksWide/High`, and `HasExterior`. Skip `PlayerShip`.
2. **Attach settlements.** For POIs whose `Role` is a settled role **and** `HasExterior`, run the **existing** settlement build (`TownLoader.LoadLocationInto` + civilian/employment/infrastructure seeding, unchanged) and assign the result to `poi.Settlement`.
3. Non-settled POIs are retained with `Settlement == null`.

The shelf-packing block-origin logic currently in `RegionLoader` is superseded by geographic positioning (§4) for the region view; retain it only if a non-geographic consumer still needs it (verify during implementation — if nothing reads the packed `OriginX/OriginZ`, remove it).

### 4. Positioning — geo offsets over all POIs

The geo-offset computation in `Program.cs:74-138` (map pixel → `geoX/geoZ` via `TileSize`/`BlockSide` and `TownCentre`, terrain floor → `padY`, plus the region pixel-bounds `mx0/my1` and shared datum) currently iterates only settlements. It now iterates **all POIs** (those that will render). Consequences:

- Region pixel bounds and the shared terrain datum are computed across all POIs, so the region view **grows to include peripheral dungeons/covens** at their true positions.
- Each POI's `OriginX/Y/Z` is filled in by this pass.

To keep concerns clean, the positioning pass should set `OriginX/Y/Z` directly on each `RegionPoi` rather than building a separate tuple list.

### 5. Rendering

The render input list passed to `TownLayout.ResolveRegion` is built from **every POI with `HasExterior == true`** (settled and non-settled alike): `(poi.Name, poi.OriginX, poi.OriginY, poi.OriginZ)`. `ResolveRegion` / `AddLocation` are unchanged.

POIs with `HasExterior == false` keep their `RegionPoi` record (so future simulation can still target them) but contribute no geometry this pass. The `/asset/town` endpoint (`Program.cs:251`) and `town3d.html` consume the result unchanged.

### 6. Purpose taxonomy (future-sim contract)

Documented on `PoiRole` (XML-doc comments + this table) so later per-POI simulation has a contract to build against:

| Role | Intended simulation purpose (future) |
|---|---|
| `Dungeon` | Threat / lair node — monster·bandit·undead source; danger to nearby settlements; loot/quest origin. |
| `Coven` | Witch enclave — night activity, reagent/potion trade, recruits from population. |
| `CultShrine` | Covert / heretical worship — cultists, rituals, clandestine recruitment. |
| `Graveyard` | Undead emergence at night — necromancy; threat seepage into nearby towns. |
| `ManorWealthy` | Isolated noble household — employs locals, trades, a family unit. |
| `HovelPoor` | Isolated subsistence family / hermit. |
| `PlayerShip` | Out of scope. |

## Components & boundaries

- **`PoiRegistry.cs`** (`RegionPoi`, `PoiRole`, `PoiRoleOf`) — pure data + classification. No I/O. Independently testable: given a `LocationType`, returns the right `Role`.
- **`RegionLoader`** — enumerates locations, builds POIs, attaches settlements. Depends on `MapsFile`/`TownLoader` (existing) and `PoiRegistry`.
- **Positioning (in `Program.cs` / extract a helper)** — pure transform from `(MapPixel, blocks, terrain floor)` → world offset. Already isolated math; now keyed off `RegionPoi`.
- **Rendering** — `TownLayout.ResolveRegion` unchanged; only its input list source changes.

## Error handling & edge cases

- **Empty exterior** (`Width/Height <= 0`): `HasExterior = false`; record kept, no geometry. Common for some dungeons/graveyards.
- **`GetLocation(...).Loaded == false`**: skip the POI entirely (matches current behavior at `RegionLoader.cs:48`).
- **Unknown `LocationType`** (e.g. `None`/`0xffff`): `PoiRoleOf` returns no renderable role; skip.
- **Region bounds growth**: confirm `town3d` camera/extent handling tolerates a larger region footprint once peripheral POIs are included.
- **Duplicate location names** within a region: keyed by `region.MapNames[i]` as today; rely on existing `seen` de-dup in `AddLocation` (`TownLayout.cs`).

## Testing

- **Unit:** `PoiRoleOf` maps every `DFRegion.LocationTypes` value to the expected `PoiRole` (table-driven), including the dungeon triplet → `Dungeon` and `None` → unrenderable.
- **Loader:** load a known small region; assert (a) settled POI count and their `Settlement != null` match the pre-change settlement count exactly (no economy regression), and (b) non-settled POIs now appear with `Settlement == null` and correct `Role`.
- **Positioning:** assert a non-settled POI's `OriginX/Z` equals the geo formula for its `MapPixel` (same formula as a settlement at the same pixel).
- **Render smoke:** `/asset/town` in region mode returns model placements for at least one non-settled POI that has an exterior; verify in `town3d` that dungeons/covens appear at plausible map-relative positions. (Test-suite caveat: `Sim.Tests` is mid-rewrite — rely on a targeted soak/probe + manual `town3d` check rather than the full suite.)

## Open follow-ups (not this pass)

- Visible markers (billboard/flat) for geometry-light POIs so they read on the map even with empty exteriors.
- Per-`Role` simulation systems (the purpose table above).
- Decide whether `SettlementKind` and `PoiRole` should eventually merge (currently `PoiRole` is the superset; `SettlementKind` stays as the economy's archetype).
