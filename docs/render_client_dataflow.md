# Render-client data flow & coordinate conventions

Map of where each rendered thing comes from and how it's indexed/transformed, so
orientation bugs can be reasoned about instead of guessed. Scale: `GlobalScale =
0.025`, one map pixel = one terrain tile = `32768 * 0.025 = 819.2 m`, one RMB block
= `4096 * 0.025 = 102.4 m`, tile heightfield = 129×129 samples, tilemap = 128×128
cells (= 8 blocks × 16 tiles).

The whole pipeline is a port of Daggerfall Unity (DFU); the bundled source under
`Assets/Scripts/` is the ground truth, and the conventions below were verified
against it. Net rule: **the server bakes everything into DFU's native world frame
(+X = east, +Z = north) and the client renders raw — no client-side flips/rotations.**

## World placement (region mode)
- A settlement at map pixel `(mx, my)` → tile origin `worldX = (mx - mx0)*819.2`,
  `worldZ = (my1 - my)*819.2`. So **MapPixelX → world X (east)**, and **MapPixelY →
  world Z but reversed: +Z = north** (DF `MapPixelY` increases southward, so Z counts
  down from the south-edge pixel `my1`). This matches DFU's terrain frame.
- Town is centred in its pixel: `+TownCentre = ((128 - w*16)/2)/16 * 102.4`
  (`Program.cs`). `GetRegionTile` discards `Generate`'s in-tile centring origin and
  grid-aligns each tile by pixel so wilderness neighbours seam.

## Layer 1 — terrain heightfield (geometry)
- **Source:** `WOODS.WLD`. `Generate()` (TerrainTile.cs) is a verbatim port of DFU's
  `DefaultTerrainSampler`: `GetHeightMapValuesRange1Dim(mx-2, my-2, 4)` (coarse) +
  `GetLargeHeightMapValuesRange(mx-1, my, 3)` (detail) + Perlin.
- **Server index:** `norm[x*hDim + y]`, `x` = first axis, `y` = second.
  `heights[i] = (norm[i] - datum) * MaxHeight`.
  - The coarse map sampled y-reversed (`S` rows 3,2,1,0 by `sfracy`) vs detail
    y-forward (`L` rows iy+0..3), and the asymmetric `(mx-2)`/`(mx-1)` ranges, are
    **DFU's own behaviour** — faithful, not bugs. (Earlier suspected as a Z flip;
    ruled out by comparing line-for-line with `DefaultTerrainSampler.cs`.)
- **Client (`buildTileMesh`):** `H(gx,gy) = heights[gx*dim + gy]`; vertex at
  `(x = gx*step, y = H, z = gy*step)`. So **gx → world X, gy → world Z**. Rendered
  raw; the `+Z = north` placement makes tile-internal `y` agree with world Z, so
  heights + autotiling render with no reflection.

## Layer 2 — terrain tilemap (ground texture: water/dirt/grass/stone, autotiling)
- **Source:** derived from `norm` (same heights) + Perlin climate noise. `BuildTilemap`.
- **Classify + march (server):** `baseType[cx*TDim + cy]` by absolute elevation
  (`norm[hx*hDim+hy]`, hx←cx, hy←cy) → water/dirt/grass/stone. Marching squares over
  `b0=(cx,cy)`, `b1=(cx+1,cy)` [+x, bit1], `b2=(cx,cy+1)` [+y, bit2], `b3=(cx+1,cy+1)`
  → `LookupTable[shape | ring<<4]` → byte `(rec | rot<<6 | flip<<7)`, stored
  `tilemap[cx*TDim + cy]`. `LookupTable` is a verbatim port of DFU `CreateLookupTable`;
  the neighbour/bit order matches DFU `AssignTilesJob`.
- **Atlas (`GetGroundAtlas`):** each record 0..55 is baked in its **4 orientations**
  (32×7 grid, 64px tiles); orientation `o` occupies column band `[o*8, o*8+8)`,
  record at `(col = rec%8 + o*8, row = rec/8)`. Band `o` = `Rotate90` applied `o`
  times (0/90/180/270°). `o = rot + 2*flip`, matching DFU's transformation index
  (`UpdateTileMapDataJob`: `record*4 + rot + 2*flip`) and the texture-array shader's
  `rotations[]`. `Rotate90` = DFU `RotateColors`; band 2 (180°) = `FlipColors`,
  band 3 = `RotateColors∘FlipColors`.
  - ⚠️ **The decoded record is V-FLIPPED before the orientations are baked** (resample
    `sy` counts from the bottom). DFU's `GetColor32` flips ground textures bottom-up
    and the lookup's rotate/flip values are authored against that orientation. Omitting
    this flip mirrors every transition tile's feathered edge → jagged/inverted borders.
    **This was the autotiling bug — see Root cause.**
- **Client:** `tile = tilemap[cx*td + cy]`, cell at `(x=cx, z=cy)`. **Identity UV** per
  quad (atlas `u` along +cx/+X, `v` along +cy/+Z); `col = rec%8 + o*8`, `row = rec/8`,
  `o = rot + 2*flip`. No client rotation/flip — orientation is fully pre-baked.

## Layer 3 — location ground tiles (cobblestone/roads, overlaid on town footprint)
- **Source:** `BLOCKS.BSA`, per RMB block `FldHeader.GroundData.GroundTiles`.
- **Server (`PaintLocationTiles`):** reads `ground[tileX, (16-1)-tileY]` (matches DFU
  `SetLocationTiles`' y-reversal), writes `tilemap[xpos*TDim + ypos]`,
  `xpos = tilePosX + blockX*16 + tileX`, `ypos = tilePosY + blockY*16 + tileY`. Byte
  `(rec | IsRotated<<6 | IsFlipped<<7)` reproduces DFU's `TileBitfield` exactly.
- **Client:** same tilemap → same atlas path as Layer 2, so it inherits the atlas
  V-flip fix; cobblestone/road tiles (directional) now orient correctly.

## Layer 4 — buildings
- **Source:** `BLOCKS.BSA` RMB `SubRecords.Block3dObjectRecords`; meshes from
  `ARCH3D.BSA` (was missing locally — restored from archive.org).
- **Server (`AddLocation`):** block matrix `Translate(offX + bx*blockSide, offY,
  offZ + by*blockSide)`; `bx → X, by → Z`. Sub at `(XPos, -, RMBDim - ZPos)`
  (⚠️ Z reversed). Region: `offX = geoX(centred)`, `offY = padY`, `offZ = geoZ`.
- **Client:** `applyMatrix4` per placement. No orientation transform.

## Layer 5 — agents (NPC billboards)
- **Source:** sim snapshot, packed sim coords.
- **Server WS:** `GeoRemap(x,z)` finds the settlement whose packed rect holds the
  agent, returns `(x+dx, padY, z+dz)`. Row `[id, x, z, act, phase, yaw, kind,
  groundY]`.
- **Client:** billboard at `(x, groundY + worldH/2, z)`, facing from frame-to-frame
  movement.

## Root cause (autotiling tile orientation, fixed)
Symptom: tiles in the right place, but *some* rotated/mirrored wrong; everything else
fine. Cause: `GetGroundAtlas` baked the 4 orientation variants from the **top-down**
decoded record, while DFU's `GetColor32` **V-flips** ground textures bottom-up before
baking, and the marching-squares lookup's `rot`/`flip` values are authored against the
flipped orientation. Result: every transition tile's feathered edge was vertically
mirrored — the transition curve pointed the wrong way.

Why it looked like "some, not all": a per-tile vertical flip is invisible on uniform
interior tiles (all-grass, all-dirt) and on isotropic textures, so only **transition
tiles** (dirt↔grass↔coast borders) and **directional tiles** (cobblestone/roads) showed
it — as jagged/inverted edges. Positions and tilemap bytes were always correct; it was
purely per-tile art orientation.

**Fix:** V-flip each tile during the resample in `GetGroundAtlas` (one spot; no client
change; positions/bytes untouched). Corrects both Layer 2 and Layer 3.

Why connectivity-based checks missed it: the marching squares is byte-faithful to DFU,
a synthetic concentric-ring test stays clean (a circle is symmetric under the flip),
and an edge-continuity metric barely distinguishes it on soft/isotropic transitions —
a globally-mirrored-but-connected tiling still "connects". Only directional content or
an explicit base-V-flip before/after comparison reveals absolute orientation.

**Diagnostic:** `dotnet run --project Headless/Sim.AssetExport -- tilemap <region>
<location>` (e.g. `Daggerfall "Daggerfall"`) renders the painted tilemap top-down, an
8-way dihedral variant grid, a synthetic-ring marching-squares test, and a
current-vs-base-V-flip side-by-side (`cmp_synth.png` shows the jagged→smooth diff).

(Historical note: an earlier revision of this doc hypothesised "heightfield + classification
need flip-Z, tile art needs a transpose." That was diagnosing an *older* client that did
UV transforms on the client — a `(1-v,u)` base plus per-tile rot/flip. The current client
pre-bakes orientation into the atlas and renders with identity UVs, so those client-side
flips/transpose no longer exist and are not needed; the real issue was the single missing
atlas V-flip above.)
