# Town walls in town3d — design

**Date:** 2026-06-19
**Status:** Approved, ready for implementation plan
**Scope:** This session — make walls (and all block-level 3D objects) visible in the 3D viewer. Guards, gates-as-function, and monster behaviour are explicitly *out* (see the companion doc `2026-06-19-odd-dynamic-object-zero-guards-design.md`).

## Problem

Daggerfall walled cities are built from `WALL*` RMB blocks. Two facts from recon:

1. **Walls are already solid in the sim.** `BlockWalkability.cs` bakes every cell of a `WALL*` block to cost 0, exactly like a building footprint. Pathfinding already routes agents around walls and through the gate gaps — so *"agents walk in and out freely" already works today*. Nothing about the simulation needs to change for walls.
2. **Walls are invisible in the viewer.** An RMB block carries 3D geometry in two places:
   - each subrecord's `Block3dObjectRecords` — the buildings;
   - the block's `RmbBlock.Misc3dObjectRecords` — block-level objects including the **wall segments** (and gates, fountains, misc props).

   `TownLayout.AddLocation` (`Headless/Sim.AssetExport/TownLayout.cs:123-147`) iterates **only** the subrecord models. The misc records are never read — there is an explicit TODO at `TownLayout.cs:10`: *"Block-level misc objects (walls etc.) are a follow-up."* So a walled city renders its buildings but not its walls.

`town3d.html` is *the* viewer (per project direction; there is intentionally no 2D viewer). So "walls in" means **walls a person can see in town3d**.

## Goal

For **every** town that has walls, render the wall ring (and gates) in `town3d.html`, using authentic Daggerfall geometry, with no change to the simulation and no special-casing of individual towns. Generalize to streaming in **all** block-level 3D objects present in the data (the TODO says "walls **etc.**") — walls are the headline, but every misc 3D object comes along as a fidelity win.

## Approach

Finish the follow-up: extend `TownLayout.AddLocation` to also emit placements for each block's `Misc3dObjectRecords`, appending them to the same `Placements` / `ModelIds` lists the buildings already use.

**Why nothing else changes:**
- The browser render path is geometry-agnostic. `town3d.html` fetches `/asset/town`, loads each unique `modelId` via `GLTFLoader` from `/asset/model/{id}`, clones it, and applies the placement's column-major matrix (`town3d.html` ~line 686-702). Wall segments are just more `(modelId, matrix)` pairs — they flow through untouched. **No viewer change required.**
- Models are instanced (one shared mesh, many matrices), so even an 8×8 city's full wall ring is cheap.
- Unwalled villages have no `WALL*` blocks and therefore no wall segments in their misc records — they render exactly as before. The fix is automatically town-agnostic.
- `ResolveRegion` calls the same `AddLocation`, so region-wide rendering gets walls for free.

### The one correctness risk: the misc transform differs from the subrecord transform

Subrecord models (current code, `TownLayout.cs:123-141`) use a nested chain:

```
world = blockM * subRecordTRS(XPos, RMBDimension - ZPos, -YRot) * objectTRS(XPos, -YPos, ZPos, -YRot, scale)
```

Misc objects have **no subrecord wrapper** and a different position convention. Per `RMBLayout.AddProps` (`Assets/Scripts/Utility/RMBLayout.cs:913-919`), the reference renderer uses:

```
modelPosition = (obj.XPos, -obj.YPos + propsOffsetY, obj.ZPos + RMBDimension) * GlobalScale
modelRotation = (-obj.XRotation, -obj.YRotation, -obj.ZRotation) / RotationDivisor
modelScale    = GetModelScaleVector(obj)
modelMatrix   = TRS(position, rotation, scale)        // NOT wrapped in a subRecordMatrix
```

So the export must compute, for each misc record:

```
world = blockM * miscObjectTRS
  where miscObjectTRS position = (obj.XPos, -obj.YPos + propsOffsetY, obj.ZPos + RMBDimension) * GlobalScale
        rotation = RotateY(-obj.YRotation / RotationDivisor)
        scale    = ScaleOf(obj)
```

Notes:
- The Z convention is `ZPos + RMBDimension`, **not** the subrecord's `RMBDimension - ZPos`. Mirror `AddProps` exactly — do not assume it matches the subrecord path.
- Include `propsOffsetY` (the DFU props Y offset constant) — find its value in the DFU source and replicate it.
- As with the existing code, only the dominant **Y** rotation is applied; rare per-object X/Z tilts stay unapplied (consistent with the current `TownLayout.cs:9` limitation). Acceptable for walls, which are Y-aligned.
- Reuse the existing `Mat` helpers, `ScaleOf`, `seen`/`ModelIds` dedup, and `Bounds` accumulation — the misc loop is structurally a sibling of the subrecord loop inside the same `bx,by` block iteration.

## Components touched

| Component | Change |
| --- | --- |
| `Headless/Sim.AssetExport/TownLayout.cs` | Add a misc-records loop in `AddLocation` (after the subrecord loop, same block scope). Update the file-header comment (`:7-10`) — the "follow-up" is now done. |
| `town3d.html`, `Program.cs`, `WorldRunner.cs` | **No change.** Existing `/asset/town` payload and render path carry the new placements as-is. |
| Simulation (`BlockWalkability`, pathfinding, etc.) | **No change.** Walls are already solid in the cost grid. |

## Out of scope (this session)

- **Functional gates** (open/close state, blocking). Gates render visually as part of the misc geometry; their *behaviour* belongs to the guards pass.
- **Guards as gatekeepers**, monster hunger/hunting, and the Dynamic Object Zero ODD extension — recorded in `2026-06-19-odd-dynamic-object-zero-guards-design.md`.
- Per-object X/Z tilt rotations (pre-existing limitation, not introduced here).

## Verification

1. Build: `dotnet build` the Sim.Web / Sim.AssetExport projects (requires `DAGGERFALL_ARENA2` → `/home/uggeli/df-data/arena2`).
2. Run Sim.Web; open `town3d.html` against a **walled city** (e.g. Daggerfall, an 8×8 `TownCity` confirmed to carry `WALLAA02.RMB`). Confirm:
   - the wall ring is visible and sits on the perimeter where the cost-grid solids are (cross-check against the `solidRuns` the server already computes);
   - gates appear in the ring;
   - wall segments are positioned/oriented sanely relative to the buildings (no floating, no half-block Z shift — this is the misc-transform check).
3. Open an **unwalled village** (Gothway Garden). Confirm it is unchanged (no walls, no regressions).
4. Sanity-check model count / load time for the 8×8 city is acceptable (instancing should keep it cheap).

## Success criteria

- Loading any walled town in town3d shows its authentic wall ring + gates, correctly placed.
- Unwalled towns are visually unchanged.
- No simulation behaviour changes.
- All other block-level 3D misc objects also render (general fidelity improvement).
