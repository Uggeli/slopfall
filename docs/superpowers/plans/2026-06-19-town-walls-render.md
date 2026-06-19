# Town Walls in town3d — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render authentic Daggerfall wall (and all block-level 3D) geometry in the `town3d.html` viewer for every walled town, by exporting block `Misc3dObjectRecords` that `TownLayout` currently drops.

**Architecture:** An RMB block holds buildings in each subrecord's `Block3dObjectRecords` and block-level objects (walls, gates, misc props) in `RmbBlock.Misc3dObjectRecords`. `TownLayout.AddLocation` exports only the former. We add a sibling loop that emits placements for the misc records — using the misc transform (`RMBLayout.AddProps`), which differs from the subrecord transform — into the same `Placements`/`ModelIds` lists. The browser render path, the `/asset/town` payload, and the simulation are all unchanged: walls are just more `(modelId, matrix)` pairs flowing through the existing instanced-model path.

**Tech Stack:** C# / .NET 10, xUnit, DaggerfallConnect readers (`MapsFile`, `BlocksFile`, `DFBlock`), Three.js viewer (untouched).

## Global Constraints

- Real game data lives at `$DAGGERFALL_ARENA2` → `/home/uggeli/df-data/arena2`. Tests no-op silently when that directory is absent (mirror the existing `Arena2DataTests` gate). Exercising them requires the env var set.
- The misc-object transform MUST mirror `RMBLayout.AddProps` (`Assets/Scripts/Utility/RMBLayout.cs:913-919`), **not** the subrecord chain: position `(obj.XPos, -obj.YPos + propsOffsetY, obj.ZPos + BlocksFile.RMBDimension) * GlobalScale`, with `propsOffsetY = -4f`; **no** subrecord matrix wrapper.
- Apply only the dominant **Y** rotation (`-obj.YRotation / RotationDivisor`); rare per-object X/Z tilts stay unapplied — consistent with the existing limitation noted at `TownLayout.cs:9`.
- Matrices are column-major (THREE `Matrix4.fromArray` layout); reuse the existing `Mat` helpers and `ScaleOf`.
- No changes to `town3d.html`, `Program.cs`, `WorldRunner.cs`, or any simulation code.

---

### Task 1: Export block-level `Misc3dObjectRecords` from `TownLayout`

**Files:**
- Modify: `Headless/Sim.Tests/Sim.Tests.csproj` (add a project reference)
- Create: `Headless/Sim.Tests/TownLayoutTests.cs`
- Modify: `Headless/Sim.AssetExport/TownLayout.cs:104-150` (add misc loop in `AddLocation`); `:7-10` (header comment)

**Interfaces:**
- Consumes: `Sim.AssetExport.TownLayout.Resolve(string arena2, string region, string location) → TownLayout.TownData`; `TownData.Placements` (`List<Placement>`, one entry per emitted model instance), `TownData.ModelIds` (deduped). DaggerfallConnect `MapsFile`, `BlocksFile`, `DFBlock` (from Sim.Data), `BlocksFile.RMBDimension`.
- Produces: no new public API — `AddLocation` simply emits additional placements. Later tasks rely only on the observable growth of `Placements`/`ModelIds`.

- [ ] **Step 1: Add the project reference and confirm the test project still builds**

In `Headless/Sim.Tests/Sim.Tests.csproj`, add to the existing `<ItemGroup>` of `ProjectReference`s:

```xml
    <ProjectReference Include="..\Sim.AssetExport\Sim.AssetExport.csproj" />
```

Run: `dotnet build Headless/Sim.Tests/Sim.Tests.csproj`
Expected: BUILD SUCCEEDED (the reference resolves; `TownLayout` is now visible to the test project). If it fails on duplicate types, stop — the reference strategy needs revisiting; do not proceed.

- [ ] **Step 2: Write the failing test**

Create `Headless/Sim.Tests/TownLayoutTests.cs`. It independently recomputes, from the raw block data, how many model instances the export *should* contain (subrecord objects **plus** misc objects), then asserts the export matches. Before the misc loop exists, the export only contains subrecord objects, so the count is short and the test fails.

```csharp
using System;
using System.IO;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using Sim.AssetExport;
using Xunit;

namespace Sim.Tests
{
    /// Verifies TownLayout exports block-level Misc3dObjectRecords (wall
    /// segments, gates, misc props) in addition to subrecord building models.
    /// No-ops silently when the ARENA2 data directory is absent.
    public class TownLayoutTests
    {
        static string Arena2 =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";

        static bool Available => Directory.Exists(Arena2);

        [Fact]
        public void WalledCity_ExportsMiscObjects_IncludingWalls()
        {
            if (!Available) return;

            // Independently count the model instances the walled city should
            // yield, walking blocks exactly as TownLayout.AddLocation does.
            var maps = new MapsFile(Path.Combine(Arena2, "MAPS.BSA"), FileUsage.UseMemory, true);
            var blocks = new BlocksFile(Path.Combine(Arena2, "BLOCKS.BSA"), FileUsage.UseMemory, true);
            DFLocation loc = maps.GetLocation("Daggerfall", "Daggerfall");
            Assert.True(loc.Loaded);

            int width = loc.Exterior.ExteriorData.Width;
            int height = loc.Exterior.ExteriorData.Height;
            int subCount = 0, miscCount = 0;
            for (int by = 0; by < height; by++)
            for (int bx = 0; bx < width; bx++)
            {
                string name = loc.Exterior.ExteriorData.BlockNames[by * width + bx];
                var block = blocks.GetBlock(name);
                if (block.Type != DFBlock.BlockTypes.Rmb || block.RmbBlock.SubRecords == null)
                    continue;
                foreach (var sub in block.RmbBlock.SubRecords)
                    if (sub.Exterior.Block3dObjectRecords != null)
                        subCount += sub.Exterior.Block3dObjectRecords.Length;
                if (block.RmbBlock.Misc3dObjectRecords != null)
                    miscCount += block.RmbBlock.Misc3dObjectRecords.Length;
            }

            // The test is only meaningful if this walled city actually carries
            // block-level misc geometry (it does: wall segments + props).
            Assert.True(miscCount > 0, "expected Daggerfall to carry Misc3dObjectRecords");

            var town = TownLayout.Resolve(Arena2, "Daggerfall", "Daggerfall");

            // One placement per emitted model instance: subrecord + misc.
            Assert.Equal(subCount + miscCount, town.Placements.Count);
        }
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Headless/Sim.Tests/Sim.Tests.csproj --filter "FullyQualifiedName~TownLayoutTests"`
Expected: FAIL — `Assert.Equal()` reports actual `Placements.Count` equal to `subCount` only, short by `miscCount`.

- [ ] **Step 4: Implement the misc loop**

In `Headless/Sim.AssetExport/TownLayout.cs`, inside `AddLocation`, after the `foreach (var sub in block.RmbBlock.SubRecords)` loop closes (after line 147) and before the end of the `bx` loop body (before line 148's closing brace of the `for (int bx...)` block), add:

```csharp
                    // Block-level misc 3D objects: wall segments, city gates,
                    // fountains, props. No subrecord wrapper — positioned
                    // directly in block space per RMBLayout.AddProps (note the
                    // ZPos + RMBDimension convention and the -4 props Y offset).
                    if (block.RmbBlock.Misc3dObjectRecords != null)
                    {
                        const float propsOffsetY = -4f;
                        foreach (var obj in block.RmbBlock.Misc3dObjectRecords)
                        {
                            float mx = obj.XPos * GlobalScale;
                            float my = (-obj.YPos + propsOffsetY) * GlobalScale;
                            float mz = (obj.ZPos + BlocksFile.RMBDimension) * GlobalScale;
                            float mAng = Deg2Rad(-obj.YRotation / rd);
                            float[] objM = Mat.Mul(Mat.Mul(Mat.Translate(mx, my, mz), Mat.RotateY(mAng)), ScaleOf(obj));

                            float[] m = Mat.Mul(blockM, objM);
                            data.Placements.Add(new Placement { ModelId = obj.ModelIdNum, Matrix = m });
                            if (seen.Add(obj.ModelIdNum))
                                data.ModelIds.Add(obj.ModelIdNum);

                            b.Add(m[12], m[13], m[14]);
                        }
                    }
```

Then update the file-header comment at `TownLayout.cs:9-10`, replacing:

```csharp
// not yet applied. Block-level misc objects (walls etc.) are a follow-up.
```

with:

```csharp
// not yet applied. Block-level misc objects (walls, gates, props) are emitted
// after the subrecord models, using the AddProps transform (no subrecord
// wrapper; ZPos + RMBDimension; -4 props Y offset).
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Headless/Sim.Tests/Sim.Tests.csproj --filter "FullyQualifiedName~TownLayoutTests"`
Expected: PASS — `Placements.Count == subCount + miscCount`.

- [ ] **Step 6: Commit**

```bash
git add Headless/Sim.Tests/Sim.Tests.csproj Headless/Sim.Tests/TownLayoutTests.cs Headless/Sim.AssetExport/TownLayout.cs
git commit -m "Sim/: export block-level misc 3D objects (walls, gates, props) from TownLayout

TownLayout only emitted subrecord building models; block-level
Misc3dObjectRecords (the wall segments + gates) were dropped. Emit them
after the subrecord loop using the AddProps transform (no subrecord
wrapper; ZPos + RMBDimension; -4 props Y offset). Walled towns now carry
their wall geometry into the /asset/town payload; the viewer renders it
through the existing instanced-model path, no client change.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Visual verification in town3d (manual)

This confirms the misc transform is correct — the part that cannot be asserted numerically. No code changes; this task gates the feature as visually correct.

**Files:** none (run + observe).

**Interfaces:**
- Consumes: the running Sim.Web server and `/asset/town`, `/asset/model/{id}` endpoints (unchanged); `town3d.html`.

- [ ] **Step 1: Build and run Sim.Web**

Run (from repo root):
```bash
export DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2
dotnet build Headless/Sim.Web/Sim.Web.csproj
```
Expected: BUILD SUCCEEDED. Then start the server per its usual invocation (e.g. `dotnet run --project Headless/Sim.Web` with the region/location args it accepts), pointing it at the **walled city** `Daggerfall`/`Daggerfall`.

- [ ] **Step 2: Open town3d on the walled city and confirm walls**

Open `town3d.html` (served by Sim.Web) against `region=Daggerfall&location=Daggerfall`. Confirm:
- A wall ring is visible around the city perimeter.
- City gates appear in the ring (open/closed gate models).
- Wall segments sit on the perimeter where the cost-grid solids are, and are not floating, sunk, or shifted by ~half a block in Z (the Z-convention check) or by a constant vertical offset (the `propsOffsetY` check). Cross-check perimeter placement against the `solidRuns` the server already computes (`Program.cs:170-197`).

Expected: walls + gates render in roughly the right place and orientation relative to the buildings. If they are shifted/rotated wrongly, the misc transform in Task 1 Step 4 is off — revisit the position/rotation expression before declaring done.

- [ ] **Step 3: Confirm an unwalled village is unchanged**

Open town3d against `region=Daggerfall&location=Gothway Garden` (a village with no `WALL*` blocks).
Expected: no walls; scene is visually unchanged from before this work (no regressions, no stray misc geometry breaking the view).

- [ ] **Step 4: Sanity-check load**

Confirm the 8×8 city loads in town3d without an unreasonable model-count / load-time hit (instancing shares meshes across the many wall-segment matrices, so this should be cheap).
Expected: acceptable load; no per-instance mesh explosion.

---

## Self-Review

**Spec coverage:**
- "Export `Misc3dObjectRecords` / all block 3D objects" → Task 1 Step 4. ✓
- "Mirror `AddProps` transform (no subrecord wrapper, `ZPos + RMBDimension`, `propsOffsetY = -4`)" → Global Constraints + Task 1 Step 4 code. ✓
- "Y-rotation only" → Global Constraints + code (`RotateY` only). ✓
- "No viewer/sim change" → Global Constraints; no such files in any task. ✓
- "Works for every walled town; villages unchanged" → Task 2 Steps 2-3. ✓
- "Authentic geometry, visible walls + gates" → Task 2 Step 2. ✓
- "Cheap (instanced)" → Task 2 Step 4. ✓
- Header-comment TODO retired → Task 1 Step 4. ✓

**Placeholder scan:** No TBD/TODO/"handle errors" language; all code steps carry complete code; the one manual task (visual check) is inherent to the deliverable and spells out exact expectations. ✓

**Type consistency:** `TownLayout.Resolve(string,string,string) → TownData`; `TownData.Placements`/`ModelIds`; `Mat.Mul`/`Mat.Translate`/`Mat.RotateY`/`ScaleOf`/`Deg2Rad`/`rd`/`blockM`/`seen`/`b` all match the existing `TownLayout.cs` definitions read during planning. `propsOffsetY = -4f` matches `RMBLayout.cs:37`. `BlocksFile.RMBDimension` used as in existing code. ✓
