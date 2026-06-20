# World Flora & Props — Flat Rendering + Per-Species Resource Registry — Design

**Date:** 2026-06-20
**Status:** Approved, ready for implementation plan
**Scope:** Render the rest of the world's exterior 3D/flat content — nature scenery and decorative flats — as billboards, AND build a per-species vegetation/resource **registry** populated from the same scenery data, as the data model for future harvesting. This pass renders the flats and records the flora; it does **not** implement harvesting. Road generation is explicitly out of scope (separate later cycle).

## Background

The region/town renderer currently emits only 3D architectural models — two `RmbBlock3dObjectRecord` collections (`TownLayout.cs:125`, `:155`): buildings, walls, gates, fountains. Everything else exterior is in the Daggerfall block data but never exported:

- **Nature scenery** — `RmbBlock.FldHeader.GroundData.GroundScenery[16,16]`, one `TextureRecord` per tile, indexing a climate-specific nature archive (500–511). Trees, rocks, bushes, plants.
- **Decorative flats** — `BlockFlatObjectRecords` (per subrecord) and `MiscFlatObjectRecords` (block level), each `RmbBlockFlatObjectRecord` with its own `TextureArchive`/`TextureRecord` and `XPos/YPos/ZPos`. Props, animals, light flats, signs.
- **Static people flats** — `BlockPeopleRecords` (and flat records with `FactionID != 0`). Excluded: the live sim already spawns and renders the population as animated sprites; these would double up.

Two findings shape the approach:

1. **The client already has a billboard path** (`town3d.html:625-697`): a camera-facing `PlaneGeometry`, bottom-anchored, UV-sampled from a per-archive sheet served by `/asset/spritesheet/{archive}` + `/asset/spritemeta/{archive}`. The sim-agent sprite work (kind/yaw/activity milestone) built it. Static flats reuse the *pattern*, minus animation.
2. **No species data exists anywhere.** `RmbGroundScenery` exposes only `TextureRecord`; nothing in Daggerfall or DFU labels what record N of a nature archive *is*. Per-species classification must be hand-authored — but accurately, by rendering each record to an image and viewing it (see §3).

This follows the pattern established by the POI work: build the unified data model now, with **rendering as the first consumer and simulation (harvesting) as the future one**, parsed independently on the render side and the sim side.

## Goals

- Every exterior flat (nature scenery + decorative props) renders as a billboard in the region/town view.
- A per-instance, per-species `FloraRegistry` records the world's vegetation/resources (nature scenery), classified against a comprehensively hand-authored `SpeciesCatalog`.
- Render and registry are independent components sharing only the scenery-parse convention — each understandable and testable alone.
- The catalog is authored from **direct visual inspection** of the actual texture art, not guessed.

## Non-Goals (this pass)

- Harvesting / resource consumption / regrowth — the registry is the hook; no behavior yet.
- Decorative flats in the registry — props (barrels, lights, signs) render but are **not** classified or recorded as resources. Catalog scope is nature `GroundScenery`.
- Static people flats (`FactionID != 0`) — skipped entirely; the sim owns population.
- Road generation — separate later cycle (Daggerfall map data carries no inter-location roads; it needs a generation algorithm).
- Interiors, dungeon (RDB) scenery — exteriors (RMB) only.

## Components & Boundaries

Three units with clean interfaces. **A** is visual (knows `archive/record/size`); **B** is semantic (knows `species/resource`); the **Catalog** is authored data consumed only by B.

| Unit | Project(s) | Responsibility | Depends on |
|---|---|---|---|
| **A — Flat render export** | `Sim.AssetExport`, `Sim.Web`, `town3d.html` | Emit billboard placements for all flats; serve flat sheets; render them | DF block data, climate→archive |
| **B — Flora registry** | `Sim.Core`, `Sim.World` | Per-instance flora records from nature scenery, classified | `SpeciesCatalog` |
| **Catalog** | `Sim.World` | `(archive,record)→SpeciesEntry`, authored | nothing (pure data) |
| **Contact-sheet tool** | `Sim.Host` | Render each nature archive to a labeled grid PNG for authoring | `AssetService` |

`Sim.Core` stays free of the Daggerfall API enums (same rule as the POI work): flora data stores `archive/record/climate` as `int` and `Category/Resource` as pure `Sim.Core` enums.

## 1. Render Path (A)

### Export (`TownLayout`)
Add a `Flat` placement type next to the existing model `Placement`:

```
struct Flat { int Archive; int Record; float X, Y, Z; float WorldW, WorldH; }
```

`TownData` gains a `List<Flat> Flats` (and a `List<int> FlatArchives` for prefetch, mirroring `ModelIds`). For each location block, at the same block origin used for buildings, parse:

- **Nature** — `GroundData.GroundScenery[x, 15-y]` over the 16×16 grid; skip `TextureRecord < 1`. Position `= (x*256, NatureOffsetY=-2, y*256+256) * GlobalScale` relative to block origin (DFU's `AddNatureFlats` math, `RMBLayout.cs:239-269`). Archive resolved once per location via `ClimateSwaps.GetNatureArchive(natureSet, season)`, where `natureSet`/`season` derive from the location's climate (`TownLayout` already resolves `ClimateBase`).
- **Decorative** — `BlockFlatObjectRecords` (per subrecord exterior) and `MiscFlatObjectRecords` (block), at each record's `XPos/YPos/ZPos`, using its own `TextureArchive/TextureRecord`. **Skip any record with `FactionID != 0`.**

`WorldW/H` replicate DFU's `GetScaledBillboardSize` (texture record pixel dimensions × scale).

### Serving (`Sim.Web` + `AssetService`)
Two new endpoints, reusing the existing sprite-sheet packer:
- `GET /asset/flatsheet/{archive}` — PNG packing every record of the archive as a static single-frame cell.
- `GET /asset/flatmeta/{archive}` — JSON: per-record cell rect (u/v) + `worldW/H`, plus sheet dimensions.

Decorative-flat archives and nature archives both flow through these endpoints (any `TextureArchive`).

### Client (`town3d.html`)
A new `flats` scene group. On region/town load, group `data.flats` by archive, fetch each archive's `flatsheet`+`flatmeta`, and render each flat as a **Y-axis camera-facing billboard**, bottom-anchored to its `Y`, UV fixed to its record's cell (no animation). Existing buildings/agents rendering is unchanged.

> **Implementation note (this pass):** the first cut renders one `Mesh` per flat with a per-frame `lookAt` (the simple, directly-verifiable approach). A region holds **thousands** of static flats, so for region-scale performance the follow-up is to render them **batched per archive** (instanced mesh / sprite batch with a billboard vertex step) rather than thousands of per-frame `lookAt` meshes. Per-mesh is acceptable for town-scale validation now; instanced batching + LOD/culling is tracked in Open follow-ups.

## 2. Catalog (authored data)

`SpeciesCatalog` (`Sim.World`, `Assets/Sim/World/SpeciesCatalog.cs`):

- `enum FloraCategory { Unknown, Tree, Bush, Plant, Crop, Rock, Water, Deadwood }` and `enum ResourceKind { None, Wood, Forage, Stone, Reed, Herb }` — both in `Sim.Core` (pure enums). The exact member sets are **finalized during catalog authoring** (§2), once the contact sheets reveal which categories/resources the art actually contains; the lists above are the expected starting set.
- `class SpeciesEntry { string Name; FloraCategory Category; ResourceKind Resource; }` — id-indexed list.
- A lookup `(archive, record) → speciesId`. Winter archives (505/507/509/511) **inherit** their summer counterpart (504/506/508/510) by record index via a derived `winter→summer` map — authored once for the 8 base archives (500,501,502,503,504,506,508,510).
- An `Unknown` entry (id 0) is the fallback for any unclassified `(archive,record)` so the registry never drops an instance.

### Authoring method (how the catalog content is produced)
1. **Contact-sheet tool** — `Sim.Host --flatsheet <archive> <outPng>` (and `--flatsheet-all`) iterates `TextureFile.RecordCount` records, renders each via `AssetService.GetTexturePng`, and packs them into one labeled grid PNG with the record index drawn on each cell. One sheet per base nature archive.
2. **Visual classification** — the contact sheets are read (image inspection) and each record is assigned a `SpeciesEntry` from the actual art. This is the authoritative source for the catalog content.
3. The tool stays in the tree as a reusable command for re-auditing.

## 3. Registry (B)

- `FloraRegistry` on `SimWorld` (`Sim.Core`, `Assets/Sim/Registries/FloraRegistry.cs`) — `List<FloraInstance>`, `Add(FloraInstance)` at load, `All`/`Count`, no-op `Update`. Same shape as `PoiRegistry`.
- `FloraInstance` (`Sim.Core`): `{ int Archive, Record, Climate; float X, Y, Z; int SpeciesId; FloraCategory Category; ResourceKind Resource; }` — `SpeciesId` indexes the catalog; `Category`/`Resource` denormalized for cheap "what's harvestable here" queries.
- **Parse** — in `TownLoader` (sim side), during region load, iterate each block's `GroundScenery`, resolve `(natureArchive, record)` through `SpeciesCatalog`, compute world position (block origin + tile, same formula as the render export), and add a `FloraInstance` to `world.Flora`.
- **Invariant:** the render-side (`TownLayout`) and sim-side (`TownLoader`) scenery-position formulas must match, so billboards and flora instances coincide. (Same render/sim independence as POIs.)
- Registry covers nature scenery only; nothing consumes `world.Flora` yet.

## Error handling & edge cases

- `TextureRecord < 1` (nature) / negative → empty tile, skipped.
- `FactionID != 0` on a flat record → static NPC, skipped (render and registry).
- Unclassified `(archive,record)` → `Unknown` species (id 0), still recorded with raw archive/record/position.
- Winter archive with no summer counterpart entry → `Unknown` fallback.
- Missing/oversized texture archive → serving endpoint returns 404; client skips that archive's flats (logs once).
- Empty-exterior locations (no blocks) → no flats, no flora (consistent with POI `HasExterior`).

## Testing

Per project convention, the headless xUnit suite (`Sim.Tests`) is unavailable during the engine rewrite — verification is deterministic probes + a clean build, plus visual confirmation:

- **Build:** `Sim.World`, `Sim.AssetExport`, `Sim.Web`, `Sim.Host` build clean.
- **`--flatsheet` output:** contact sheets generate for all 8 base archives; record counts > 0; used as the catalog-authoring artifact.
- **`--floracheck <region>`:** prints a per-category / per-resource / per-species histogram across the region; assert total flora instances > 0 and that the `Unknown` fraction is below an agreed threshold once the catalog is authored (a high Unknown count means catalog gaps). Run on Betony (small) and Daggerfall (scale).
- **Render/registry coincidence:** a spot check that a known block's flat count on the export side equals the flora-instance count for that block on the sim side (same parse formula).
- **Manual `town3d`:** nature billboards appear on the ground around/between buildings, upright and camera-facing, at plausible density; existing buildings/agents unchanged. (The one thing probes can't assert.)

## Open follow-ups (not this pass)

- Harvesting / resource economy on top of `FloraRegistry`.
- Registering resource-bearing decorative flats (animals, crop flats) if later wanted.
- Wilderness flora *between* locations (terrain scatter outside RMB blocks).
- Road generation (separate cycle).
- LOD / culling for dense flat fields if client performance needs it.
