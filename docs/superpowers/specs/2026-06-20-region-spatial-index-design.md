# Region Spatial Index — Viewport-Streamed Buildings & Agents

**Date:** 2026-06-20
**Status:** Design — approved for planning

## Problem

Rendering a whole Daggerfall region is hard not because the web client does too
much, but because of an **asymmetry in the server's region assembly**:

- **Terrain** is already viewport-streamed. The client requests a ring of map
  pixels around its camera (`town3d.html` `streamRegionTerrain`,
  `/asset/terraintile/{mx}/{my}`), and evicts tiles that drift out of the ring.
- **Buildings/flats** are *not*. `/asset/town` in region mode calls
  `assets.GetRegion(region, settlements)` and returns **every placement in every
  settlement at once** (`Program.cs:262`). The client renders all of them every
  frame with no frustum culling, no LOD, no streaming.
- **Agents** are *not*. The WebSocket pump sends **all** agents every snapshot
  (`Program.cs:416`, iterating the full `snap.Agents`), `GeoRemap`-projected but
  unfiltered.

So draw calls and payload scale with total region size regardless of where the
camera looks. Town mode is fine; region mode is not.

The fix is to give the server a **map-pixel spatial index** so buildings and
agents can stream in the *same ring* terrain already uses. There is no spatial
index today: `SettlementRegistry`, `BuildingRegistry`, and `PositionRegistry` are
flat lookups; the only spatial structures are the `TownGridData` walkability
raster and the ephemeral per-tick `SenseSystem` cell hash.

## Goals

- A persistent **map-pixel spatial index** of the region's overworld layout,
  owned by the **sim core** as world truth (reusable by future caravan /
  wilderness / LOD layers, not just rendering).
- Viewport streaming of **buildings + flats** via the existing terrain ring.
- Viewport filtering of **agents** to the camera ring over the WebSocket.
- **Conservation:** the streamed result must render exactly the set the old full
  dump rendered — nothing vanishes, nothing duplicates.

## Non-goals

- Frustum culling / LOD / imposters for far settlements (a later pass; this
  design only bounds *what is loaded*, by map pixel).
- Changing the sim's packed walkability grid or inter-settlement pathing.
- Any client-side decision about *what exists where* — the client stays a pure
  renderer that pulls `(mx,my)` and renders the bytes. All placement is
  server-owned.
- Town mode (single settlement) behavior — untouched.

## Delivery stages

Staged so the user-facing feature lands behind tests *before* the risky
relocation of geo computation.

- **Stage 1 — land `RegionGeography` + the index + streaming.** Introduce the
  `RegionGeography` sim registry (structure + query API) and the AssetExport
  `RegionPlacementIndex`, wire the `/asset/towntile` endpoint, the client
  `streamRegionStructures`, and the WS view-filter. Geo is still **computed where
  it is today** (`Program.cs:74-146`); the web host *seeds* `RegionGeography`
  post-boot via a `Seed`/`Set` method, the same way it already seeds the road/
  solid-cell payload. Delivers the whole feature; the conservation tests come in
  with this stage.
- **Stage 2 — relocate geo into the sim (pure refactor).** Move the
  `Program.cs:74-146` computation into `RegionLoader` Pass 3 (injecting
  `tileFloor` + `maxTerrainHeight`), so `RegionGeography` is built at load and the
  web-host computation is deleted. No behavior change — guarded by Stage 1's
  conservation + coordinate-diff tests.

Section 2 below is annotated with which stage each piece belongs to.

## Architecture & dependency constraints

Confirmed project references:

- **Sim core** (`Sim.Core` / `Sim.World`, compiling `Assets/Sim`) → references
  only `Sim.Data` (DaggerfallConnect). Does **not** see `AssetExport`.
- **`Sim.AssetExport`** → references only `Sim.Data`. Decoupled from the sim
  registries — that is why `GetRegion` takes a plain `settlements` tuple list,
  not the registry.
- **`Sim.Web`** → references all of the above; it is the composition root.

Consequence: the sim cannot *call* `assets.RegionTileFloor` (in `AssetExport`)
for terrain elevation. Geo placement is nonetheless **world truth** (a town sits
at a real overworld position and ground elevation), so it moves into the sim via
**dependency injection** — `RegionLoader` already takes a `WoodsFile`; it also
takes a `tileFloor` delegate supplied by the composition root. No project
reference is inverted; geo becomes fully sim-owned (XYZ).

Two pixel-keyed indices, by layer:

- **Sim `RegionGeography`** — world-spatial truth: settlements/POIs, agent
  remap, bbox/datum. The structure future sim systems query.
- **`AssetExport` `RegionPlacementIndex`** — the *derived render geometry*
  partition (RMB-parsed model placements + flats), bucketed by pixel. Fed by the
  sim's geo origins. Lives in `AssetExport` because it owns RMB→geometry.

## Section 1 — Sim core spatial structures

New registry **`RegionGeography`** in `Assets/Sim` (beside `SettlementRegistry`
/ `PoiRegistry`; exposed on `SimWorld` as `world.Geography`). Seeded once per
region boot (by the web host in Stage 1, by `RegionLoader` in Stage 2);
empty/absent in town mode.

Holds:

- `Bbox { int Mx0, My0, Mx1, My1 }`, `float TileSize`, `float Datum`
- per-POI geo origin → fills the existing `RegionPoi.OriginX/Y/Z`
  (`PoiRegistry.cs:50`; comment changes from "filled by the web boot" to
  load-time)
- **spatial index:** `Dictionary<long, List<int>> _poiByPixel`, key
  `PixelKey(mx,my) = ((long)mx << 32) | (uint)my`, value = POI ids whose pixel ==
  that key (settlements + exterior POIs)
- packed→geo remap entries, one per settlement: `(minX, minZ, maxX, maxZ, dx,
  dy, dz)` — the agent projection table currently built in `Program.cs:142-145`

Query API:

- `IReadOnlyList<int> PoisAt(int mx, int my)` — bucket lookup
- `IEnumerable<int> RingPois(int mx, int my, int r)` — boundary-inclusive ring
- `(float x, float y, float z) GeoRemap(float x, float z)` — moved verbatim from
  `Program.cs:363-369`
- `(int mx, int my) PixelOf(float geoX, float geoZ)` — inverse of the placement
  formula: `mx = Mx0 + round(geoX / TileSize)`, `my = My1 - round(geoZ /
  TileSize)` (matches `Program.cs:132-133` and the client's inverse at
  `town3d.html:221-222`)

### Agent ephemeral index

Agents move every tick and each client has its own camera, so there is **no
persistent agent structure** — this mirrors the existing per-tick `SenseSystem`
cell hash. In the web layer, the WS pump builds an `AgentGeoIndex`
(`Dictionary<long, List<int>>`, pixel → indices into the agent array) **memoized
on `Frame.Tick`** so it is built at most once per published frame regardless of
connected spectator count. Bucketing uses `world.Geography.PixelOf` on each
agent's `GeoRemap`-projected position.

## Section 2 — Boot wiring

### `RegionGeography` (sim registry) — Stage 1

- New registry on `SimWorld` as `world.Geography`, with the structure + query API
  from Section 1 and a `Seed(bbox, tileSize, datum, poiOrigins, remapEntries)`
  method.
- **Stage 1:** the web host computes geo as today and calls `world.Geography.Seed(...)`
  after boot (alongside the existing road/solid-cell payload build). The buckets
  (`_poiByPixel`) and remap table are assembled inside `Seed` from the POI origins.
- Consumers (`GeoRemap`/`PixelOf`/`RingPois`) read the registry from the start, so
  Stage 2 changes only *who seeds it*, not who reads it.

### Sim (`RegionLoader.LoadRegion`) — Stage 2

- Add parameter `Func<string /*region*/, string /*loc*/, int /*w*/, int /*h*/,
  float> tileFloor` (the composition root passes `assets.RegionTileFloor`) **and**
  `float maxTerrainHeight` (passed from `TerrainTile.MaxTerrainHeight` — an
  `AssetExport` constant the sim cannot reference, so it is injected). `TerrainPad`
  = 3 and the 0..999 / 0..499 map-pixel clamps are Daggerfall-fixed and stay as
  sim constants.
- Add **Pass 3** after the existing Pass 2 (`RegionLoader.cs:119-144`): compute
  the POI map-pixel bbox (+`TerrainPad`, clamped), the centre
  POI and shared `Datum`, each POI's `TownCentre` offset and geo origin XYZ
  (`padY = (floor - datum) * maxTerrainHeight`), the per-settlement
  remap entries, and call `RegionGeography.Seed(...)` — the logic currently in
  `Program.cs:74-146`, moved verbatim into the sim.
- `SimBoot.CreateRegion` threads the `tileFloor` delegate + `maxTerrainHeight`
  through.

### Web (`Program.cs` region block)

- **Stage 1:** unchanged geo computation; add the `world.Geography.Seed(...)` call
  and the `assets.GetRegionIndex(...)` build.
- **Stage 2:** collapses — build the world (passing `assets.RegionTileFloor` +
  `maxTerrainHeight`), then read `world.Geography` for
  `rMx0/rMy0/rMx1/rMy1/rTileSize/rDatum`, `rTowns`, `remap`/`settlements`, and
  `GeoRemap`. The inline bbox/centre/datum/origin/remap computation is deleted.

### Geometry partition (`AssetExport`) — Stage 1

- `GetRegionIndex(...)` builds all RMB placements once (reusing the existing
  `GetRegion` path) and buckets each placement + flat by its pixel, derived from
  its matrix translation (`tx` at column-major index 12, `tz` at index 14) via
  the same `PixelOf` formula. Result: `RegionPlacementIndex`,
  `Dictionary<long, RegionTile>` where
  `RegionTile { List<Placement> Placements; List<Flat> Flats; uint[] ModelIds; }`
  and `ModelIds` is the unique set *within* that tile.
- A large city whose footprint spills past one 819.2 m pixel **splits across the
  correct buckets** — this is why per-building (not just per-settlement)
  bucketing is needed.

## Section 3 — Endpoints & WS protocol

### `GET /asset/towntile/{mx:int}/{my:int}`

- Region mode only; returns that pixel's `RegionTile` (placements + flats +
  modelIds) from `RegionPlacementIndex`.
- Empty or out-of-bbox pixel → **200 with empty arrays** (not 404) so the client
  caches "nothing here" and never re-asks.
- `Cache-Control: public, max-age=86400` (static, like model/atlas assets).

### `/asset/town` (region mode)

- Returns empty/metadata, exactly as `/asset/terrain` already does in region
  mode (`Program.cs:272-273`). Town mode unchanged.

### Client (`town3d.html`)

- New `streamRegionStructures(dt)` piggybacks the ring already computed in
  `streamRegionTerrain` (`cmx/cmy/R`, lines 221-224): fetch
  `/asset/towntile/{mx}/{my}` for missing ring pixels (nearest-first, throttled,
  capped per tick — same shape as terrain), instantiate placements (clone model
  protos + `applyMatrix4`) and flats, track in a `regionStructures` Map.
- Evict with the same 2-pixel hysteresis as terrain (`town3d.html:261-274`):
  dispose instance groups, **keep** shared model protos and flat atlases (same
  rule the nature-flat eviction already follows).
- Per-tile `modelIds` load on demand (browser-cached).

### WebSocket view filtering

- Client sends `{type:"view", mx, my, r}` whenever `cmx/cmy` changes (it already
  detects the camera's pixel each `streamRegionTerrain` tick).
- Server receive loop (`Program.cs:443` switch) stores the last view per
  connection.
- Pump filters agents to the ring around `(mx,my)` via the per-frame
  `AgentGeoIndex` before serializing.
- **Back-compat:** no `view` received yet, or town mode ⇒ send all agents,
  exactly as today.

## Section 4 — Testing (TDD)

The load-bearing invariant is **conservation**: streaming renders exactly the
set the old full dump did.

- **Sim unit (`RegionGeography`)** — pure, inject a constant `tileFloor`:
  - POIs bucket to the correct pixel (`PixelOf` ∘ placement formula round-trips).
  - `RingPois(mx,my,r)` is boundary-inclusive and excludes outside-ring pixels.
  - `GeoRemap` maps a packed point inside a settlement rect to that settlement's
    geo delta; a point outside all rects returns identity.
  - A settlement spanning two pixels appears in **both** buckets.
- **AssetExport unit (`RegionPlacementIndex`)** — conservation: union of all
  buckets equals the flat `GetRegion` placement set, by count and identity; no
  placement lands in two buckets unless it genuinely straddles (its translation
  rounds to one pixel).
- **Integration smoke (headless)** — boot a small region (Gallotale 5×6 per the
  walled-town note), enumerate every `/asset/towntile` in the bbox, assert
  `Σ placements == ` the old `/asset/town` full count.
- Confirm the live test harness during planning — `Sim.Tests` is flagged stale /
  being rewritten, so place the unit tests where they actually run rather than
  assuming the old suite.
- **Manual** — run the viewer on a region, pan, confirm buildings stream in/out
  and the agent payload drops to the ring.

## Risks & mitigations

- **Geo logic relocation regressions.** Moving `Program.cs:74-146` into the sim
  must preserve exact coordinates. Mitigation: it is isolated to **Stage 2** and
  guarded by Stage 1's conservation tests plus a before/after coordinate diff of a
  known region — Stage 1 ships the feature without touching where geo is computed,
  so any Stage 2 drift shows up as a pure-refactor test failure, not a feature bug.
- **Stale test suite.** `Sim.Tests` may not run cleanly. Mitigation: verify the
  harness in planning; prefer pure unit tests that don't need a full boot.
- **`tileFloor` injection at load.** `RegionLoader` now needs the floor per POI
  at load — but `Program.cs:134` already calls `RegionTileFloor` per POI at boot,
  so this is relocation, not new cost.
- **Two pixel-keyed indices.** Sim `RegionGeography` (settlements/agents) and
  AssetExport `RegionPlacementIndex` (geometry) are distinct by layer and
  granularity; both keyed identically. Documented to avoid "why two?" confusion.

## Out of scope / follow-ups

- Frustum culling + far-settlement LOD/imposters.
- Wiring `RegionGeography` into the v3 caravan/wilderness routing it's designed
  to serve.
