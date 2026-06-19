# Guards-as-Gatekeepers + Dynamic Object Zero Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make town guards emergently hold their gates and intercept hungry monsters that pathfind in through the only opening in the wall, with an authentic night curfew, all driven by an injectable role-aware "Dynamic Object Zero" ad provider rather than a state machine.

**Architecture:** The guard→creature kill chain already exists (`CombatSystem` strikes any agent `Doing Attack` within 2.5 m of a creature → `DamageEvent` → `HealthSystem` death → `CreatureSystem` respawn). This pass is almost entirely **decision-side**: (1) bake gate geometry from `WALL*` blocks into the town grid at load; (2) give guards a gate post + day/night shift at seed; (3) inject `Patrol`/`StandWatch` and a proximity-boosted `Attack` ad into `OddSystem.Decide` so posting/patrolling/intercepting/abandoning all emerge from scoring; (4) rework `CreatureSystem` so hungry monsters pathfind to the nearest civilian (funneling through the gate by day, blocked at the wall by night via a read-only curfew overlay); (5) instrument the soak with honesty metrics; (6) swap the gate model 446↔447 in town3d on the live `night` flag.

**Tech Stack:** .NET 10, C#, parallel CQRS sim engine (`Assets/Sim/`), headless soak runner (`Headless/Sim.Host`), THREE.js viewer (`Headless/Sim.Web/wwwroot/town3d.html`).

**Spec:** `docs/superpowers/specs/2026-06-19-odd-dynamic-object-zero-guards-design.md`

## Global Constraints

- **CQRS purity:** registries are the sole writers; systems read settled tick-N data and publish intents for tick N+1. NEVER mutate a settled registry row in place inside a system. New per-tick effects go through `Events.Publish(...SetIntent / ...Event)`. (See `MovementSystem`, `CombatSystem` as the reference pattern.)
- **Determinism is law:** the parallel engine must stay bit-for-bit identical to the serial twin. After any change to a `System`, verify with `--engineworld` (below). All randomness uses the existing `Hash(id.Value, tick)` / `Hash01(...)` helpers — NEVER `System.Random`, `Date`, or wall-clock.
- **`ActivityKind` ordinals are wire-stable:** append `Patrol`, `StandWatch` at the END of the enum (after `Weave`), never insert. (See the `Weave` comment in `BehaviorRegistry.cs`.)
- **Δ=0 duty activities score on `BaseUtility` ALONE.** In `OddSystem.V`, the score of an activity whose `Delta` is all-zero is `effectiveBase = BaseUtility` (the `BaseGate`/`DayGate`/`NightGate` gate multiplies the *gap* term, which is 0). So Patrol/StandWatch are tuned by `BaseUtility`, set above `Wander` (`0.004`) and `Idle` (`0.002`). The spec's earlier "baseGate ≈ 1.4" is wrong — ignore it.
- **Night definition (uniform):** `bool night = hour < 6 || hour >= 18;` (matches `SunlightSystem` / `DaggerfallDateTime.DawnHour=6`, `DuskHour=18`). Use this exact expression everywhere curfew/shift logic needs day vs night.
- **No unit tests.** `Headless/Sim.Tests` is stale and does not build (engine rewrite). Do NOT add tests there or gate on it. Every task is verified by **building `Sim.Host`, running a headless soak or the `--gatecheck` / `--engineworld` diagnostic on a small walled town, and reading the printed output.** Light walled `TownCity` for all runs: **Gallotale** (5×6, Daggerfall region). Daggerfall city itself is too heavy.
- **ARENA2 data path:** every run needs `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2` in the environment.
- **Build command (use after every code change):** `dotnet build Headless/Sim.Host/Sim.Host.csproj`
- **Lean scope:** one monster hunts at a time, nearest on-duty guard reacts, no packs, no reinforcement, no gate HP/siege. Curfew blocks the *monster* pathfinder only (agents path normally).

---

## File Structure

| File | Responsibility | Tasks |
| --- | --- | --- |
| `Assets/Sim/Registries/TownGridRegistry.cs` | `TownGridData` — add `GateBlock[]` flag + `IsGateCell` helper | 1 |
| `Assets/Sim/World/TownLoader.cs` | allocate + populate `GateBlock` at load; compute per-settlement gate posts; assign guard gate+shift in `SeedGuards` | 1, 2, 4 |
| `Assets/Sim/Registries/SettlementRegistry.cs` | `SettlementData` — add `GatePosts` list + `GatePost` struct | 2 |
| `Assets/Sim/Registries/EmploymentRegistry.cs` | `EmploymentData` — add `GateIndex`, `NightShift`, `GateX/GateZ` | 4 |
| `Assets/Sim/Systems/TownPathfinder.cs` | `FindPath` gains a `blockGates` overlay param | 3 |
| `Assets/Sim/Registries/BehaviorRegistry.cs` | append `Patrol`, `StandWatch` to `ActivityKind` | 5 |
| `Assets/Sim/Systems/ActivityCatalog.cs` | `Patrol`/`StandWatch` specs + `SpecFor` mapping | 5 |
| `Assets/Sim/Engine/Units/OddSystem.cs` | Dynamic Object Zero injection after `GatherAds` | 6 |
| `Assets/Sim/Registries/CreatureRegistry.cs` | `CreatureData` — add `HungerLevel` | 7 |
| `Assets/Sim/Engine/Units/CreatureSystem.cs` | hunger drift, spawn-outside, hunt-via-pathfinder, collide | 7 |
| `Assets/Sim/Engine/Units/MetricsSystem.cs` (new) | tally deaths (at-gate vs inside), read-only diagnostic | 8 |
| `Assets/Sim/Engine/EngineSoak.cs` | print the honesty histogram | 8 |
| `Headless/Sim.Host/GateDiag.cs` (new) | `--gatecheck` diagnostic, built up across tasks 1–4 | 1–4 |
| `Headless/Sim.Host/Program.cs` | wire `--gatecheck` | 1 |
| `Headless/Sim.Web/wwwroot/town3d.html` | gate model 446↔447 swap on `world.night` | 9 |
| (system wiring site) | add registries to `OddSystem`/`CreatureSystem` ctors + add `MetricsSystem` | 6, 7, 8 |

---

### Task 1: Gate-block flags on the town grid + `--gatecheck` diagnostic

Bake, at load, which blocks of the town grid are `WALL*` blocks (the perimeter ring whose only walkable cells are the gate openings). This is the foundation for both the curfew overlay and gate-post derivation. Add a `--gatecheck` diagnostic so every later task has an observable check.

**Files:**
- Modify: `Assets/Sim/Registries/TownGridRegistry.cs` (the `TownGridData` class, ~lines 26-68)
- Modify: `Assets/Sim/World/TownLoader.cs` (`NewGrid` ~92-105; `LoadLocationInto` ~155-159)
- Create: `Headless/Sim.Host/GateDiag.cs`
- Modify: `Headless/Sim.Host/Program.cs` (switch ~20-51, usage ~70-71)

**Interfaces:**
- Produces: `TownGridData.GateBlock` (`bool[]`, length `BlocksWide*BlocksHigh`, indexed `by*BlocksWide+bx`); `TownGridData.IsGateCell(int x, int y)`; `GateDiag.Run(string region, string location)`.

- [ ] **Step 1: Add the `GateBlock` field + helper to `TownGridData`**

In `Assets/Sim/Registries/TownGridRegistry.cs`, inside `public sealed class TownGridData`, add after the `Gates` field (line 35):

```csharp
        public bool[] GateBlock;     // BlocksWide * BlocksHigh; true = a WALL* block (perimeter, gate openings live here)
```

And add this helper next to `Walkable` (after line ~? where `Walkable` is defined):

```csharp
        /// True if cell (x,y) lies in a WALL* (perimeter) block — the cells that
        /// seal at night under curfew. False when GateBlock isn't baked (no walls).
        public bool IsGateCell(int x, int y)
            => GateBlock != null && InBounds(x, y)
               && GateBlock[(y / CellsPerBlock) * BlocksWide + (x / CellsPerBlock)];
```

- [ ] **Step 2: Allocate `GateBlock` in `NewGrid`**

In `Assets/Sim/World/TownLoader.cs`, in the `NewGrid` object initializer (after `Gates = new BlockGates[...]` on line 103), add:

```csharp
                GateBlock = new bool[blocksWide * blocksHigh],
```

- [ ] **Step 3: Flag `WALL*` blocks during the stitch loop**

In `Assets/Sim/World/TownLoader.cs`, inside `LoadLocationInto`, right after the cost copy (after line 159, inside the `for (int x...)` block where `blockName` and `gx,gy` are in scope), add:

```csharp
                    // Perimeter wall blocks (model 444/445 walls + 446/447 gates):
                    // their only walkable cells are the gate openings. Flag the block
                    // so the curfew overlay can seal it and gate posts can be derived.
                    if (blockName.StartsWith("WALL"))
                        grid.GateBlock[gy * grid.BlocksWide + gx] = true;
```

- [ ] **Step 4: Create the `--gatecheck` diagnostic**

Create `Headless/Sim.Host/GateDiag.cs`:

```csharp
using System;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;

namespace DaggerfallWorkshop.Sim.Host
{
    /// Inspects the baked gate geometry of one town: which blocks are WALL*,
    /// how many walkable cells each holds (the gate openings), and (in later
    /// tasks) the derived gate posts, the day/night path test, and guard posting.
    public static class GateDiag
    {
        public static int Run(string region, string location)
        {
            region ??= "Daggerfall";
            location ??= "Gallotale";
            Console.WriteLine($"gatecheck {region}/{location}…");
            var w = SimBoot.CreateTown(SimBoot.DefaultArena2Path, region, location, 600f, 12345);
            var g = w.TownGrid.Current;
            if (g == null) { Console.WriteLine("no town grid"); return 1; }

            int wallBlocks = 0, gateOpeningBlocks = 0;
            const int cells = TownGridData.CellsPerBlock;
            for (int by = 0; by < g.BlocksHigh; by++)
                for (int bx = 0; bx < g.BlocksWide; bx++)
                {
                    if (g.GateBlock == null || !g.GateBlock[by * g.BlocksWide + bx]) continue;
                    wallBlocks++;
                    int walk = 0;
                    for (int row = 0; row < cells; row++)
                        for (int col = 0; col < cells; col++)
                            if (g.Cost[(by * cells + row) * g.Width + (bx * cells + col)] > 0) walk++;
                    if (walk > 0) gateOpeningBlocks++;
                    Console.WriteLine($"  WALL block ({bx},{by}) walkable cells={walk}");
                }
            Console.WriteLine($"grid {g.Width}x{g.Height} cells, {g.BlocksWide}x{g.BlocksHigh} blocks; " +
                              $"WALL blocks={wallBlocks}, with gate openings={gateOpeningBlocks}");
            return 0;
        }
    }
}
```

- [ ] **Step 5: Wire `--gatecheck` into the runner**

In `Headless/Sim.Host/Program.cs`, add a case to the `switch (args[0])` (e.g. after the `--probe` case, ~line 23):

```csharp
                case "--gatecheck":
                    return GateDiag.Run(args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null);
```

And add `--gatecheck region location` to the `Usage()` string (line 70-71).

- [ ] **Step 6: Build**

Run: `dotnet build Headless/Sim.Host/Sim.Host.csproj`
Expected: build succeeds, 0 errors.

- [ ] **Step 7: Run the diagnostic on Gallotale and confirm walls are detected**

Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Host -- --gatecheck Daggerfall Gallotale`
Expected: prints several `WALL block (bx,by)` lines, a non-zero `WALL blocks=` count, and `with gate openings=` ≥ 1 (the gate blocks have walkable cells; pure wall blocks have ~0). If `WALL blocks=0`, STOP — the `WALL` name assumption is wrong for this data; report it before continuing.

- [ ] **Step 8: Commit**

```bash
git add Assets/Sim/Registries/TownGridRegistry.cs Assets/Sim/World/TownLoader.cs Headless/Sim.Host/GateDiag.cs Headless/Sim.Host/Program.cs
git commit -m "Sim/: bake WALL-block gate flags into the town grid + --gatecheck diag"
```

---

### Task 2: Per-settlement gate posts

Derive concrete gate post positions (world X,Z) from the gate-opening blocks and store them on the settlement, so guards can be assigned a post and `Patrol`/`StandWatch` have a target.

**Files:**
- Modify: `Assets/Sim/Registries/SettlementRegistry.cs` (the `SettlementData` class, ~lines 25-56)
- Modify: `Assets/Sim/World/TownLoader.cs` (`LoadLocationInto` — inside the existing subrecord loop, ~line 162)
- Modify: `Headless/Sim.Host/GateDiag.cs`

**Derivation (recon-confirmed, supersedes a centroid approach):** a `WALL*` block is mostly open ground with a thin wall ring — its walkable-cell centroid is NOT the gate. The real gates are the gate **3D models** (446 = open, 447 = closed) carried as subrecord objects in `WALL*` blocks. Gallotale has exactly 4 (model 446) at the N/E/W/S walls. So one gate post per 446/447 model instance, at the model's world position. The gate models live where `LoadLocationInto` already walks subrecords (`sub`, `gx`, `gy`, `blockSide`, `blockName` all in scope), so compute posts there — the grid alone can't see them.

**Interfaces:**
- Consumes: `block.RmbBlock.SubRecords[i].Exterior.Block3dObjectRecords[j]` → `.ModelIdNum` (`uint`), `.XPos`/`.ZPos` (`int`); `sub.XPos`/`sub.ZPos`; `gx`/`gy`/`blockSide`/`GlobalScale`/`BlocksFile.RMBDimension` (all already in `LoadLocationInto`).
- Produces: `SettlementRegistry.GatePost` struct `{ float X, Z; }`; `SettlementData.GatePosts` (`List<GatePost>`), populated during load.

- [ ] **Step 1: Add `GatePost` + `GatePosts` to settlement data**

In `Assets/Sim/Registries/SettlementRegistry.cs`, add the struct (top of the namespace, near `SettlementData`):

```csharp
    /// A town-wall gate opening (gate model 446/447) — a post a guard holds / patrols to.
    public struct GatePost { public float X, Z; }
```

And inside `public sealed class SettlementData`, alongside `Residents` (line ~55):

```csharp
        public readonly List<GatePost> GatePosts = new List<GatePost>();
```

- [ ] **Step 2: Record gate-model positions inside the subrecord loop**

In `Assets/Sim/World/TownLoader.cs`, inside `LoadLocationInto`'s existing `for (int i = 0; i < block.RmbBlock.SubRecords.Length; i++)` loop (after `var sub = block.RmbBlock.SubRecords[i];`, ~line 164), add a scan for gate models. The world transform mirrors the `BuildingRow.X/Z` math already in this loop (subrecord origin) plus the gate object's intra-subrecord offset — this exact additive form was verified by recon to place Gallotale's 4 gates correctly:

```csharp
                    // Gate posts: a guard-holdable opening is a gate 3D model
                    // (446 open / 447 closed) carried by a WALL* block's subrecord.
                    // World pos = subrecord origin (same math as BuildingRow.X/Z below)
                    // + the gate object's offset within the subrecord.
                    if (blockName.StartsWith("WALL") && sub.Exterior.Block3dObjectRecords != null)
                    {
                        foreach (var obj in sub.Exterior.Block3dObjectRecords)
                        {
                            if (obj.ModelIdNum != 446 && obj.ModelIdNum != 447) continue;
                            float gateX = gx * blockSide + (sub.XPos + obj.XPos) * GlobalScale;
                            float gateZ = gy * blockSide
                                          + (BlocksFile.RMBDimension - sub.ZPos) * GlobalScale
                                          + obj.ZPos * GlobalScale;
                            settlement.GatePosts.Add(new GatePost { X = gateX, Z = gateZ });
                        }
                    }
```

(If `sub.Exterior.Block3dObjectRecords` or `obj.ModelIdNum`/`XPos`/`ZPos` don't resolve, confirm the exact field path with `grep -rn "Block3dObjectRecords\|ModelIdNum" Headless/Sim.AssetExport/TownLayout.cs` — that file reads the same records. Do NOT dedup near-coincident gates; a double-wide gate yielding two adjacent posts is fine.)

- [ ] **Step 3: (no separate call needed)**

`GatePosts` is populated during `LoadLocationInto`, which already runs before `SeedGuards` for each settlement — so posts exist when guards are seeded (Task 4). No extra wiring. Confirm the ordering with `grep -n "LoadLocationInto\|SeedGuards" Assets/Sim/World/*.cs` (SeedGuards must run after the location's blocks are loaded; if any path violates this, note it but do not reorder without flagging).

- [ ] **Step 4: Print gate posts in `--gatecheck`**

In `Headless/Sim.Host/GateDiag.cs`, after the WALL-block summary line, add:

```csharp
            foreach (var st in w.Settlements.All)
            {
                Console.WriteLine($"settlement '{st.Name}' gate posts={st.GatePosts.Count}");
                for (int i = 0; i < st.GatePosts.Count; i++)
                    Console.WriteLine($"  gate {i}: ({st.GatePosts[i].X:F1}, {st.GatePosts[i].Z:F1})");
            }
```

- [ ] **Step 5: Build**

Run: `dotnet build Headless/Sim.Host/Sim.Host.csproj`
Expected: build succeeds.

- [ ] **Step 6: Run `--gatecheck` and confirm posts**

Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Host -- --gatecheck Daggerfall Gallotale`
Expected: `settlement 'Gallotale' gate posts=4` — the four model-446 gates at roughly `(345.6, 88.0)`, `(422.4, 369.6)`, `(89.6, 446.4)`, `(140.8, 523.2)` (one per N/E/W/S wall). If you get ~18 posts you are still centroid-ing WALL blocks (wrong); if you get 0, the gate-model field path is wrong — investigate before committing.

- [ ] **Step 7: Commit**

```bash
git add Assets/Sim/Registries/SettlementRegistry.cs Assets/Sim/World/TownLoader.cs Headless/Sim.Host/GateDiag.cs
git commit -m "Sim/: derive per-settlement gate posts from gate models (446/447) in wall blocks"
```

---

### Task 3: Curfew-aware pathfinding overlay

Give `TownPathfinder.FindPath` an optional `blockGates` flag: when set, gate-block cells are treated as impassable (the night curfew), without mutating the immutable cost grid. The monster pathfinder (Task 7) passes `blockGates: night`.

**Files:**
- Modify: `Assets/Sim/Systems/TownPathfinder.cs` (`FindPath` ~50-127 and its internal neighbor expansion ~176-187)
- Modify: `Headless/Sim.Host/GateDiag.cs`

**Interfaces:**
- Consumes: `TownGridData.IsGateCell`.
- Produces: `TownPathfinder.FindPath(TownGridData g, float fromX, float fromZ, float toX, float toZ, List<PathPoint> result, bool approachTarget = true, bool blockGates = false)`.

- [ ] **Step 1: Add the `blockGates` parameter and thread it to the passability check**

In `Assets/Sim/Systems/TownPathfinder.cs`, add `bool blockGates = false` as the last parameter of `FindPath` (line 50). Thread it to wherever the neighbor passability test reads the cost grid (the recon located `byte cost = g.Cost[ny * W + nx]; if (cost == 0) return;` ~line 179). Change that test to:

```csharp
                byte cost = g.Cost[ny * W + nx];
                if (cost == 0) return;
                if (blockGates && g.IsGateCell(nx, ny)) return;   // curfew: gate sealed at night
```

If the neighbor expansion is a separate local/static method, add a `bool blockGates` parameter to it and pass `blockGates` from `FindPath` at the call site so the flag reaches the test. Do NOT change any existing call site of `FindPath` (the new param defaults to `false`, so `MovementSystem` etc. are unaffected).

- [ ] **Step 2: Add a day/night path probe to `--gatecheck`**

In `Headless/Sim.Host/GateDiag.cs`, after the gate-post loop, add a test that paths from outside the wall to a town-interior point, open vs curfew. Place it inside the per-settlement loop, using the first gate post as the interior-adjacent anchor and the grid's farthest walkable cell as the "outside" start:

```csharp
                if (st.GatePosts.Count > 0)
                {
                    var gate = st.GatePosts[0];
                    // an "outside" start: a walkable cell on the grid's edge nearest origin
                    float outX = g.WorldX(0), outZ = g.WorldZ(0);
                    var path = new System.Collections.Generic.List<PathPoint>();
                    bool dayOk = TownPathfinder.FindPath(g, outX, outZ, gate.X, gate.Z, path, true, blockGates: false);
                    int dayLen = path.Count;
                    bool nightOk = TownPathfinder.FindPath(g, outX, outZ, gate.X, gate.Z, path, true, blockGates: true);
                    int nightLen = path.Count;
                    Console.WriteLine($"  path to gate 0: day found={dayOk} len={dayLen}; curfew found={nightOk} len={nightLen}");
                }
```

(Add `using DaggerfallWorkshop.Sim;` if `PathPoint`/`TownPathfinder` need it — they live in the sim namespace already imported.)

- [ ] **Step 3: Build**

Run: `dotnet build Headless/Sim.Host/Sim.Host.csproj`
Expected: build succeeds.

- [ ] **Step 4: Run `--gatecheck` and confirm curfew changes pathing**

Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Host -- --gatecheck Daggerfall Gallotale`
Expected: the `path to gate 0` line shows the curfew path is blocked or strictly longer than the day path (`curfew found=False`, or `nightLen > dayLen`). If day and curfew are identical, the gate-block overlay isn't taking effect — investigate before committing.

- [ ] **Step 5: Verify determinism is unaffected**

Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Host -- --engineworld Daggerfall Gallotale 20000`
Expected: ends with `DETERMINISTIC — parallel == serial OK` (the new param defaults false, so live behavior is unchanged).

- [ ] **Step 6: Commit**

```bash
git add Assets/Sim/Systems/TownPathfinder.cs Headless/Sim.Host/GateDiag.cs
git commit -m "Sim/: curfew pathfinding overlay (blockGates) + gatecheck path probe"
```

---

### Task 4: Guard shift + gate assignment at seed

Assign each guard a gate post (round-robin) and a day/night shift (by guard index) at seed time, stored on `EmploymentData`.

**Files:**
- Modify: `Assets/Sim/Registries/EmploymentRegistry.cs` (the `EmploymentData` class, ~lines 11-15)
- Modify: `Assets/Sim/World/TownLoader.cs` (`SeedGuards` ~202-226)
- Modify: `Headless/Sim.Host/GateDiag.cs`

**Interfaces:**
- Consumes: `SettlementData.GatePosts`.
- Produces: on `EmploymentData`: `int GateIndex` (default `-1` = unassigned), `bool NightShift`, `float GateX, GateZ`.

- [ ] **Step 1: Add gate/shift fields to `EmploymentData`**

In `Assets/Sim/Registries/EmploymentRegistry.cs`, inside `public sealed class EmploymentData`, add:

```csharp
        public int GateIndex = -1;      // assigned gate post in the settlement (−1 = no gates / civilian fallback)
        public bool NightShift;         // true = night watch, false = day watch (assigned by guard index)
        public float GateX, GateZ;      // world position of the assigned gate post
```

- [ ] **Step 2: Assign gate + shift in `SeedGuards`**

In `Assets/Sim/World/TownLoader.cs`, in the `for (int i = 0; i < guards; i++)` loop of `SeedGuards`, replace the `world.Employment.Seed(...)` call with a version that fills the new fields. Use the settlement's gate posts (now computed before `SeedGuards` per Task 2):

```csharp
            for (int i = 0; i < guards; i++)
            {
                var id = residents[i];
                var emp = new EmploymentData { Employer = EntityId.None, PublicOwner = s.Treasury };
                emp.NightShift = (i % 2) == 1;          // even index = day watch, odd = night watch
                if (s.GatePosts.Count > 0)
                {
                    emp.GateIndex = i % s.GatePosts.Count;   // round-robin across posts
                    emp.GateX = s.GatePosts[emp.GateIndex].X;
                    emp.GateZ = s.GatePosts[emp.GateIndex].Z;
                }
                world.Employment.Seed(id, emp);
                if (world.Identity.TryGet(id, out var ident) && ident != null && ident.Name != null
                    && !ident.Name.Contains("(Guard)"))
                    ident.Name += " (Guard)";
            }
```

(Keep the existing treasury-seed line after the loop unchanged.)

- [ ] **Step 3: Print guard assignments in `--gatecheck`**

In `Headless/Sim.Host/GateDiag.cs`, after the per-settlement path probe, add a guard dump:

```csharp
            int guards = 0;
            foreach (var kv in w.Employment.All)
            {
                var e = kv.Value;
                if (e == null || e.PublicOwner.IsNone) continue;
                guards++;
                Console.WriteLine($"  guard {kv.Key.Value}: gate={e.GateIndex} shift={(e.NightShift ? "night" : "day")} at ({e.GateX:F1},{e.GateZ:F1})");
            }
            Console.WriteLine($"total guards={guards}");
```

(If `Employment.All` is not exposed, use the same enumeration style the other registries use — `grep -n "public .* All" Assets/Sim/**/EmploymentRegistry.cs`. If there is no `All`, add one mirroring another registry's `All` property.)

- [ ] **Step 4: Build**

Run: `dotnet build Headless/Sim.Host/Sim.Host.csproj`
Expected: build succeeds.

- [ ] **Step 5: Run `--gatecheck` and confirm guards have posts + alternating shifts**

Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Host -- --gatecheck Daggerfall Gallotale`
Expected: `total guards=` matches the count policy (Gallotale is a City → ≥2), each guard line shows a valid `gate=` index (0..N-1) and shifts alternate day/night across guard indices.

- [ ] **Step 6: Commit**

```bash
git add Assets/Sim/Registries/EmploymentRegistry.cs Assets/Sim/World/TownLoader.cs Headless/Sim.Host/GateDiag.cs
git commit -m "Sim/: assign guards a gate post + day/night shift at seed"
```

---

### Task 5: `Patrol` + `StandWatch` activities

Add the two duty activities. They relieve no need (Δ=0) and live on `BaseUtility` set above the `Wander`/`Idle` baselines so an on-duty guard prefers them over aimless wandering — but a real need (hunger) still outscores them (emergent abandonment) and the boosted `Attack` ad (Task 6) outscores them (emergent interception).

**Files:**
- Modify: `Assets/Sim/Registries/BehaviorRegistry.cs` (`ActivityKind` enum, ~lines 6-31)
- Modify: `Assets/Sim/Systems/ActivityCatalog.cs` (add specs; extend `SpecFor`)

**Interfaces:**
- Produces: `ActivityKind.Patrol`, `ActivityKind.StandWatch`; `ActivityCatalog.Patrol`, `ActivityCatalog.StandWatch` specs; `SpecFor` returns them.

- [ ] **Step 1: Append the enum values**

In `Assets/Sim/Registries/BehaviorRegistry.cs`, append to the END of `enum ActivityKind` (after `Weave`):

```csharp
    Patrol,      // guard duty: hold/patrol the town gate by day (Object-Zero injected, Δ=0)
    StandWatch,  // guard duty: hold the town gate by night (Object-Zero injected, Δ=0)
```

- [ ] **Step 2: Add the specs**

In `Assets/Sim/Systems/ActivityCatalog.cs`, add (near the other `public static readonly Spec` definitions):

```csharp
        // Guard duty. Δ=0 → scores on BaseUtility alone (the gate term multiplies a
        // zero gap). BaseUtility sits above Wander (0.004)/Idle (0.002) so an on-duty
        // guard holds the gate over aimless wandering, but below a real hunger gap
        // (abandonment) and below the proximity-boosted Attack ad (interception).
        public static readonly Spec Patrol = new Spec
        {
            Kind = ActivityKind.Patrol,
            DurationMinutes = 20,
            BaseUtility = 0.010,
        };

        public static readonly Spec StandWatch = new Spec
        {
            Kind = ActivityKind.StandWatch,
            DurationMinutes = 30,
            BaseUtility = 0.010,
        };
```

- [ ] **Step 3: Map them in `SpecFor`**

Find `SpecFor` in `ActivityCatalog.cs` (`grep -n "case ActivityKind" Assets/Sim/Systems/ActivityCatalog.cs` — it is a switch). Add cases:

```csharp
                case ActivityKind.Patrol: return Patrol;
                case ActivityKind.StandWatch: return StandWatch;
```

- [ ] **Step 4: Build**

Run: `dotnet build Headless/Sim.Host/Sim.Host.csproj`
Expected: build succeeds (no missing-case warnings on the `ActivityKind` switch).

- [ ] **Step 5: Verify the activities exist and don't perturb a baseline soak**

Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Host -- --engineworld Daggerfall Gallotale 20000`
Expected: `DETERMINISTIC — parallel == serial OK`. (No injection yet, so `Patrol`/`StandWatch` should NOT yet appear in behavior — they're added to the marketplace in Task 6.)

- [ ] **Step 6: Commit**

```bash
git add Assets/Sim/Registries/BehaviorRegistry.cs Assets/Sim/Systems/ActivityCatalog.cs
git commit -m "Sim/: add Patrol + StandWatch guard-duty activities (Δ=0, BaseUtility-tuned)"
```

---

### Task 6: Dynamic Object Zero — inject guard ads in `OddSystem.Decide`

The general seam: right after `GatherAds`, inject role/context-aware ads for guards. On-shift → a `Patrol` (day) or `StandWatch` (night) ad at the assigned gate. A creature sensed nearby → a proximity-boosted `Attack` ad (floored above duty) so the guard breaks to intercept. All three behaviours (post / patrol / react) plus abandonment emerge from scoring; no state machine.

**Files:**
- Modify: `Assets/Sim/Engine/Units/OddSystem.cs` (`Decide` — inject after line 178; add a helper; add the `SubjectiveViewRegistry` dependency)
- Modify: the `OddSystem` construction site (add the registry argument)

**Interfaces:**
- Consumes: `EmploymentData.{GateIndex,NightShift,GateX,GateZ}`; `SubjectiveViewRegistry.TryGet` → `SubjectiveViewData.Entities` (`EntityRead.{Other,Threat}`); `PositionRegistry.TryGet`; `ActivityCatalog.SpecFor(Patrol/StandWatch)`; `OddSystem.IsNightFor` is NOT used here — use the `night` already computed in `Decide` (line 151) **OR** the uniform `hour<6||hour>=18`; see Step 2.

- [ ] **Step 1: Add the `SubjectiveViewRegistry` dependency to `OddSystem`**

In `Assets/Sim/Engine/Units/OddSystem.cs`, add a `readonly SubjectiveViewRegistry _subjectiveView;` field, a constructor parameter `SubjectiveViewRegistry subjectiveView`, and `_subjectiveView = subjectiveView;` in the body. Then update the construction site: run `grep -rn "new OddSystem(" Assets/ Headless/` and add the world's subjective-view registry argument (e.g. `world.SubjectiveView`) at the matching position. (If `OddSystem` already holds `SubjectiveViewRegistry` under another name, reuse it and skip this step.)

- [ ] **Step 2: Inject guard ads after `GatherAds`**

In `Decide`, immediately after `var ads = GatherAds(id);` (line 178), add:

```csharp
            if (sc.IsGuard) InjectGuardAds(id, sc, ads, hour);
```

Use the curfew/shift night definition consistent with the rest of the system. `Decide` already computed `bool night` (line 151) from the agent's chronotype; for guard *duty* use the plain civic clock instead so shifts align with the town curfew, not personal chronotype:

- [ ] **Step 3: Implement `InjectGuardAds`**

Add this method to `OddSystem` (near `GatherAds`):

```csharp
        // Dynamic Object Zero (guards): role/context-aware ads injected into the
        // marketplace before consolidation. Posting/patrol/interception/abandonment
        // all emerge from scoring — no state machine.
        const double GuardAttackFloor = 0.02;   // > Patrol/StandWatch BaseUtility (0.010): a breach beats duty
        const double GuardAttackGain  = 0.06;   // proximity adds up to this at point-blank
        const float  GuardSenseRange  = 12f;    // matches SenseSystem sight radius

        void InjectGuardAds(EntityId id, ScoreContext sc, List<Ad> ads, int hour)
        {
            if (!_employment.TryGet(id, out var emp) || emp == null || emp.PublicOwner.IsNone) return;
            bool civicNight = hour < 6 || hour >= 18;

            // On-shift duty ad at the assigned gate post (skip if this guard has no gate).
            if (emp.GateIndex >= 0 && emp.NightShift == civicNight)
            {
                var verb = civicNight ? ActivityKind.StandWatch : ActivityKind.Patrol;
                ads.Add(new Ad
                {
                    Verb = verb, Building = -1, X = emp.GateX, Z = emp.GateZ,
                    Spec = ActivityCatalog.SpecFor(verb),
                });
            }

            // Sensed creature → proximity-boosted Attack ad. Δ=0 so it scores purely
            // on BaseUtility (independent of the guard's own Fear): floor + proximity,
            // always above duty. Targeting is resolved to NearestCreature downstream.
            if (_subjectiveView.TryGet(id, out var view) && view != null
                && _position.TryGet(id, out var gp) && gp != null)
            {
                float best2 = GuardSenseRange * GuardSenseRange;
                bool have = false; float tx = 0, tz = 0;
                for (int i = 0; i < view.Entities.Count; i++)
                {
                    var e = view.Entities[i];
                    if (e.Threat <= 0) continue;
                    if (!_position.TryGet(e.Other, out var cp) || cp == null) continue;
                    float dx = cp.X - gp.X, dz = cp.Z - gp.Z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 < best2) { best2 = d2; tx = cp.X; tz = cp.Z; have = true; }
                }
                if (have)
                {
                    double prox = 1.0 - System.Math.Sqrt(best2) / GuardSenseRange;   // 0..1, 1 at point-blank
                    double util = GuardAttackFloor + prox * GuardAttackGain;
                    ads.Add(new Ad
                    {
                        Verb = ActivityKind.Attack, Building = -1, X = tx, Z = tz,
                        Spec = new ActivityCatalog.Spec
                        {
                            Kind = ActivityKind.Attack,
                            DurationMinutes = 10,
                            BaseUtility = util,
                        },
                    });
                }
            }
        }
```

(The consolidation step keeps the best-scoring ad per `ActivityKind`, so this boosted `Attack` (util ≥ 0.02) beats the innate fear-driven `Attack` ad (≈0 for a fearless guard). When `bestKind == Attack` and `IsGuard`, the existing branch at lines 252-258 already retargets to `NearestCreature`.)

- [ ] **Step 4: Build**

Run: `dotnet build Headless/Sim.Host/Sim.Host.csproj`
Expected: build succeeds.

- [ ] **Step 5: Soak Gallotale and confirm guards post + intercept emerges**

Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Host -- --soak Daggerfall Gallotale 1`
Expected in the per-sample activity histogram:
- Daytime samples (hour 06–18) show `Patrol=` among the population; night samples (hour 18–06) show `StandWatch=`. Counts are small (only on-shift guards) but non-zero.
- Some samples show `Attack=` once creatures wander within sense range of a posted guard.
If neither `Patrol` nor `StandWatch` ever appears, the injection/shift gating is wrong — investigate before committing.

- [ ] **Step 6: Verify determinism still holds**

Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Host -- --engineworld Daggerfall Gallotale 20000`
Expected: `DETERMINISTIC — parallel == serial OK`.

- [ ] **Step 7: Commit**

```bash
git add Assets/Sim/Engine/Units/OddSystem.cs
# plus the OddSystem construction-site file(s) changed in Step 1
git commit -m "Sim/: Dynamic Object Zero — inject guard Patrol/StandWatch + proximity Attack ads"
```

---

### Task 7: Hungry monsters that pathfind through gates (and stall at the wall under curfew)

Rework `CreatureSystem` so creatures gain a hunger drive, spawn outside the wall, and — when hungry — pathfind to the nearest civilian via `TownPathfinder` (funneling through the gate by day, blocked at the wall by night). When not hungry they wander but collide with blocked cells instead of phasing.

**Files:**
- Modify: `Assets/Sim/Registries/CreatureRegistry.cs` (`CreatureData` struct, ~lines 12-18)
- Modify: `Assets/Sim/Engine/Units/CreatureSystem.cs` (movement ~53-124; spawn ~129-159; add `TownGridRegistry` dependency)
- Modify: the `CreatureSystem` construction site (add the grid registry argument)

**Interfaces:**
- Consumes: `TownGridRegistry.Current` → `TownGridData` (`Walkable`, `CellX/CellY`, `WorldX/WorldZ`, `Width/Height`); `TownPathfinder.FindPath(..., blockGates)`; existing `NearestCivilian` helper (extend its range).
- Produces: `CreatureData.HungerLevel` (`float`, 0..1).

- [ ] **Step 1: Add hunger to `CreatureData`**

In `Assets/Sim/Registries/CreatureRegistry.cs`, add to the `CreatureData` struct:

```csharp
        public float HungerLevel;        // 0 sated .. 1 starving; drives the hunt
```

- [ ] **Step 2: Add the `TownGridRegistry` dependency to `CreatureSystem`**

In `Assets/Sim/Engine/Units/CreatureSystem.cs`, add `readonly TownGridRegistry _townGrid;`, a constructor parameter, and the assignment. Update the construction site: `grep -rn "new CreatureSystem(" Assets/ Headless/` and pass the world's town-grid registry.

- [ ] **Step 3: Add hunger constants + drift**

Near the other `const` declarations in `CreatureSystem`, add:

```csharp
        const float HungerDriftPerTick = 0.0000015f;   // ~1.3/day at 864k ticks → reliably hungry within a day
        const float HuntThreshold = 0.5f;              // above this, hunt the nearest civilian
        const float CivilianHuntRange = 100000f;       // effectively town-wide (vs the old proximity bite range)
```

At the top of the per-creature loop body (after `if (!_position.TryGet(id, out var pos)) continue;`), drift hunger and persist it on the creature row you already write back:

```csharp
        cr.HungerLevel = System.Math.Min(1f, cr.HungerLevel + HungerDriftPerTick * (float)gameSeconds / 0.1f);
```

(Use `gameSeconds` already computed at the top of `Update`; the `/0.1f` normalizes to per-0.1s-tick. Persist `cr` via the `CreatureSetIntent` already published on retarget/attack; if a tick neither retargets nor attacks, publish a `CreatureSetIntent { Id = id, Data = cr }` once at the end of the loop body so the drifted hunger settles — guard against double-publish by only publishing once per creature per tick.)

- [ ] **Step 4: Hunt-or-wander movement**

Replace the wander block (the `if (dist <= ArriveDistance) {...} else {...}` movement, ~lines 86-104) with hunger-aware logic. When hungry, target the nearest civilian and pathfind with the curfew overlay; follow the first waypoint. When the path fails (curfew sealed the gate) or when not hungry, move straight but collide with blocked cells:

```csharp
            var grid = _townGrid.Current;
            bool night = clock.Hour < 6 || clock.Hour >= 18;
            bool hunting = cr.HungerLevel >= HuntThreshold;

            float goalX, goalZ; bool haveGoal;
            if (hunting)
            {
                var victim = NearestCivilian(id, here_x, here_z, CivilianHuntRange);
                haveGoal = !victim.IsNone && _position.TryGet(victim, out var vp) && vp != null;
                goalX = haveGoal ? _position.Get(victim).X : cr.TargetX;
                goalZ = haveGoal ? _position.Get(victim).Z : cr.TargetZ;
            }
            else { goalX = cr.TargetX; goalZ = cr.TargetZ; haveGoal = true; }

            float nextX = here_x, nextZ = here_z; bool moved = false;
            if (hunting && haveGoal && grid != null
                && TownPathfinder.FindPath(grid, here_x, here_z, goalX, goalZ, _scratch, true, blockGates: night)
                && _scratch.Count > 0)
            {
                // Follow the path: head to the first waypoint we haven't reached.
                var wp = _scratch[0];
                float ddx = wp.X - here_x, ddz = wp.Z - here_z;
                float dd = (float)System.Math.Sqrt(ddx * ddx + ddz * ddz);
                if (dd > 1e-3f) { nextX = here_x + ddx / dd * step; nextZ = here_z + ddz / dd * step; moved = true; }
            }
            else
            {
                // No path (curfew) or just wandering: straight-line, but COLLIDE.
                float ddx = goalX - here_x, ddz = goalZ - here_z;
                float dd = (float)System.Math.Sqrt(ddx * ddx + ddz * ddz);
                if (dd <= ArriveDistance && !hunting)
                {
                    uint h = Hash(id.Value, tick);
                    double ang = (h & 0xFFFF) / 65535.0 * 2.0 * System.Math.PI;
                    double r = WanderRadius * (0.3 + 0.7 * (((h >> 16) & 0xFF) / 255.0));
                    cr.TargetX = here_x + (float)(System.Math.Cos(ang) * r);
                    cr.TargetZ = here_z + (float)(System.Math.Sin(ang) * r);
                }
                else if (dd > 1e-3f)
                {
                    float cand_x = here_x + ddx / dd * step, cand_z = here_z + ddz / dd * step;
                    // collide: only step if the destination cell is walkable
                    if (grid == null || grid.Walkable(grid.CellX(cand_x), grid.CellY(cand_z)))
                    { nextX = cand_x; nextZ = cand_z; moved = true; }
                    // else: blocked — stall at the wall (no move this tick)
                }
            }

            if (moved)
            {
                float yaw = (float)(System.Math.Atan2(nextX - here_x, nextZ - here_z) * 180.0 / System.Math.PI);
                Events.Publish(new PositionSetIntent { Id = id, X = nextX, Y = pos.Y, Z = nextZ, Yaw = yaw });
                here_x = nextX; here_z = nextZ;
            }
```

Add a reusable `readonly List<PathPoint> _scratch = new List<PathPoint>();` field to the system (cleared by `FindPath`). Keep the existing bite block (lines 106-122) below this — eating on arrival is unchanged. The bite already targets `NearestCivilian` within `AttackRange`, so a monster that reaches its prey eats it.

- [ ] **Step 5: Spawn outside the wall**

In `TrySpawn` (lines 129-159), replace the `centroid + SpawnRadius` placement with a walkable cell on the grid's outer edge (outside the inner wall ring). After computing the centroid `(cx,cz)` and bearing `ang`, march outward to the last walkable cell before leaving the grid:

```csharp
            var grid = _townGrid.Current;
            float px, pz;
            if (grid != null)
            {
                float dirx = (float)System.Math.Cos(ang), dirz = (float)System.Math.Sin(ang);
                float lastX = cx, lastZ = cz;
                for (float r = 0; r < grid.Width * TownGridData.CellSize; r += TownGridData.CellSize)
                {
                    float qx = cx + dirx * r, qz = cz + dirz * r;
                    int qcx = grid.CellX(qx), qcy = grid.CellY(qz);
                    if (!grid.InBounds(qcx, qcy)) break;
                    if (grid.Cost[qcy * grid.Width + qcx] > 0) { lastX = qx; lastZ = qz; }
                }
                px = lastX; pz = lastZ;   // farthest walkable cell along the bearing = at the outer wall band
            }
            else { px = cx + (float)(System.Math.Cos(ang) * SpawnRadius); pz = cz + (float)(System.Math.Sin(ang) * SpawnRadius); }
```

Set the spawned `CreatureData { TargetX = px, TargetZ = pz, NextAttackTick = 0, HungerLevel = 0 }`.

- [ ] **Step 6: Build**

Run: `dotnet build Headless/Sim.Host/Sim.Host.csproj`
Expected: build succeeds.

- [ ] **Step 7: Soak and confirm hunting + curfew behaviour**

Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Host -- --soak Daggerfall Gallotale 1`
Expected: the run completes; `pop` fluctuates as civilians are killed and creatures respawn (deaths happen). The full honesty breakdown (day vs night kills, blocked-at-wall) is quantified in Task 8 — here just confirm the sim runs a full day without exceptions and population churns.

- [ ] **Step 8: Verify determinism**

Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Host -- --engineworld Daggerfall Gallotale 20000`
Expected: `DETERMINISTIC — parallel == serial OK`. (All new randomness is via `Hash`; pathfinding is deterministic.) If MISMATCH, find the nondeterministic read (e.g. iterating an unordered dictionary for the victim) and sort/seed it before committing.

- [ ] **Step 9: Commit**

```bash
git add Assets/Sim/Registries/CreatureRegistry.cs Assets/Sim/Engine/Units/CreatureSystem.cs
# plus the CreatureSystem construction-site file changed in Step 2
git commit -m "Sim/: hungry monsters pathfind to civilians through gates, stall at the wall under curfew"
```

---

### Task 8: Honesty metrics in the soak

Quantify whether the chokepoint actually emerged: civilian deaths at-gate vs inside the walls, day vs night, guards posting/intercepting, and creatures stalled at the wall. Per the "score what everyone DOES" principle — the raw counts are the signal, not a pass/fail.

**Files:**
- Create: `Assets/Sim/Engine/Units/MetricsSystem.cs`
- Modify: the system-wiring site (add `MetricsSystem` to the engine's system list, LAST so it sees `DeathEvent`s published this tick)
- Modify: `Assets/Sim/Engine/EngineSoak.cs` (`Report` ~32-51)

**Interfaces:**
- Consumes: `DeathEvent` (`SimEvents.cs`: `{ EntityId Entity, Killer; DamageType FatalDamageType }`); `CreatureRegistry` (to confirm the killer is a creature); `PositionRegistry`; `TownGridRegistry.IsGateCell`; `CreatureRegistry.HungerLevel`; `EmploymentData` (guard posting/abandonment).
- Produces: `MetricsSystem` public read-only counters: `long KillsAtGate, KillsInside, KillsByDay, KillsByNight;` read by `EngineSoak`.

- [ ] **Step 1: Create `MetricsSystem`**

```csharp
namespace DaggerfallWorkshop.Sim.Engine
{
    /// Read-only diagnostic: tallies creature kills of civilians, classified by
    /// where (at a gate cell vs inside the walls) and when (day vs night). Publishes
    /// NOTHING — it must run AFTER HealthSystem (which emits DeathEvent) and never
    /// mutates settled state, so it doesn't affect the determinism fingerprint.
    public sealed class MetricsSystem : SimSystem
    {
        public long KillsAtGate, KillsInside, KillsByDay, KillsByNight;

        readonly WorldClockRegistry _clock;
        readonly CreatureRegistry _creatures;
        readonly PositionRegistry _position;
        readonly TownGridRegistry _townGrid;

        public MetricsSystem(EventBus events, WorldClockRegistry clock, CreatureRegistry creatures,
            PositionRegistry position, TownGridRegistry townGrid) : base(events)
        {
            _clock = clock; _creatures = creatures; _position = position; _townGrid = townGrid;
        }

        public override void Update(long tick)
        {
            var deaths = Events.GetEvents<DeathEvent>();
            if (deaths.Length == 0) return;
            var clock = _clock.Current;
            bool night = clock.Hour < 6 || clock.Hour >= 18;
            var grid = _townGrid.Current;
            for (int i = 0; i < deaths.Length; i++)
            {
                var d = deaths[i];
                if (!_creatures.Contains(d.Killer)) continue;   // only creature kills of civilians
                if (night) KillsByNight++; else KillsByDay++;
                bool atGate = false;
                if (grid != null && _position.TryGet(d.Entity, out var vp) && vp != null)
                    atGate = grid.IsGateCell(grid.CellX(vp.X), grid.CellY(vp.Z));
                if (atGate) KillsAtGate++; else KillsInside++;
            }
        }
    }
}
```

- [ ] **Step 2: Wire it into the engine, last**

Find where the parallel engine builds its ordered system list (`grep -rn "new HealthSystem(\|new CombatSystem(\|new MovementSystem(" Assets/ Headless/`). Add `new MetricsSystem(events, clock, creatures, position, townGrid)` to that list AFTER `HealthSystem` (so `DeathEvent`s of this tick are visible). Expose the instance so `EngineSoak` can read it — e.g. store it on `SimWorld` as `public MetricsSystem Metrics;` set during construction, mirroring how other systems/registries are exposed. If `SimWorld` doesn't expose systems, add a nullable `Metrics` field and assign it in the same builder.

- [ ] **Step 3: Print the metrics in the soak report**

In `Assets/Sim/Engine/EngineSoak.cs`, extend `Report` to also count live guard/creature state and print the cumulative kill breakdown. Add inside `Report`, before the final `Console.WriteLine`:

```csharp
            int posting = 0, attacking = 0, hungry = 0;
            foreach (var kv in w.Behavior.All)
            {
                if (kv.Value == null) continue;
                var a = kv.Value.Activity;
                if (a == ActivityKind.Patrol || a == ActivityKind.StandWatch) posting++;
                else if (a == ActivityKind.Attack) attacking++;
            }
            foreach (var kv in w.Creatures.All)
                if (kv.Value.HungerLevel >= 0.5f) hungry++;

            var m = w.Metrics;
            string kills = m == null ? "" :
                $" kills[gate={m.KillsAtGate} inside={m.KillsInside} day={m.KillsByDay} night={m.KillsByNight}]";
            Console.WriteLine($"           guards[posting={posting} attacking={attacking}] hungryMonsters={hungry}{kills}");
```

(If `w.Creatures` / `w.Metrics` accessors differ, adjust to the actual `SimWorld` property names.)

- [ ] **Step 4: Build**

Run: `dotnet build Headless/Sim.Host/Sim.Host.csproj`
Expected: build succeeds.

- [ ] **Step 5: Soak and read the honesty histogram**

Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Host -- --soak Daggerfall Gallotale 1`
Expected: each sample now prints a `guards[posting=… attacking=…] hungryMonsters=… kills[gate=… inside=… day=… night=…]` line. Sanity checks (these are observations, not asserts): `posting` > 0 across the day; `day` kills ≥ `night` kills (curfew suppresses night entry); some `gate` kills appear (interceptions/breaches happen at the chokepoint). Record the actual numbers in the task report — they're the deliverable.

- [ ] **Step 6: Verify determinism (metrics must not perturb the sim)**

Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Host -- --engineworld Daggerfall Gallotale 20000`
Expected: `DETERMINISTIC — parallel == serial OK` (MetricsSystem publishes nothing, so the fingerprint is unchanged).

- [ ] **Step 7: Commit**

```bash
git add Assets/Sim/Engine/Units/MetricsSystem.cs Assets/Sim/Engine/EngineSoak.cs
# plus the system-wiring + SimWorld file(s) changed in Step 2
git commit -m "Sim/: honesty metrics — guard posting + monster-kill breakdown (gate vs inside, day vs night)"
```

---

### Task 9: Night-curfew gate visual in town3d (cuttable)

Swap the gate model 446 (open) ↔ 447 (closed) in the 3D viewer on the live `world.night` flag the WebSocket already pushes. Pure client-side; no backend change. **Cuttable:** if it fights the instanced renderer, drop it — the sim-side curfew stands alone.

**Files:**
- Modify: `Headless/Sim.Web/wwwroot/town3d.html` (model loading ~708-737; instancing ~739-747; snapshot handler `onSnap` ~670-679; animate loop ~402-414)

**Interfaces:**
- Consumes: `data.placements[].modelId` (446 = open gate), `world.night` (already maintained from the `snap` frame).

- [ ] **Step 1: Ensure both gate models are fetched**

In `init()` where models are loaded (the `Promise.all(data.modelIds.map(...))` ~line 728), after building `protos`, ensure the closed-gate model is available even if the data only references 446:

```javascript
  if (protos.has(446) && !protos.has(447))
    protos.set(447, await loadModel(447, climate, season));
```

- [ ] **Step 2: Track gate instances during placement**

In the instancing loop (~739-747), collect gate placements so they can be toggled. Before the loop add `const gates = [];`. Inside the loop, after `town.add(inst); placed++;`, add:

```javascript
    if (p.modelId === 446 || p.modelId === 447) {
      const closed = protos.get(447).clone();
      closed.applyMatrix4(new THREE.Matrix4().fromArray(p.matrix));
      closed.visible = false;
      town.add(closed);
      inst.userData.isGateOpen = true;
      gates.push({ open: inst, closed });
    }
```

After the loop, hoist `gates` to a scope the animate loop can see (e.g. assign `window.__gates = gates;` or a module-level `let gatePairs = gates;` declared near `world`).

- [ ] **Step 3: Toggle on `world.night`**

In `animate()` (~402-414), after `controls.update();`, add:

```javascript
  if (gatePairs) for (const g of gatePairs) { g.open.visible = !world.night; g.closed.visible = world.night; }
```

(Declare `let gatePairs = null;` near the `world` constant ~line 417, and set `gatePairs = gates;` at the end of `init`'s placement.)

- [ ] **Step 4: Build the web project**

Run: `dotnet build Headless/Sim.Web/Sim.Web.csproj`
Expected: build succeeds (HTML is static; this just confirms the project still builds).

- [ ] **Step 5: Manual visual verification**

Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Web -- Daggerfall Gallotale --port 8080 --tps 60`
Open `http://localhost:8080/town3d.html`. Watch the status line's clock; confirm the gate geometry swaps to the closed model when the clock crosses into night (hour ≥ 18) and back to open at dawn (hour ≥ 6). If the swap doesn't appear or the renderer chokes, this task is cuttable — note it in the report and revert this file rather than blocking the milestone.

- [ ] **Step 6: Commit**

```bash
git add Headless/Sim.Web/wwwroot/town3d.html
git commit -m "Sim/: town3d swaps gate model 446<->447 on the night curfew flag"
```

---

## Self-Review

**Spec coverage:**
- Dynamic Object Zero seam (spec §2) → Task 6 (inject after `GatherAds`). ✓
- Shift by guard index (spec §3) → Task 4 (`NightShift = i%2==1`) + Task 6 (on-shift gate). ✓
- Proximity-derived Attack floored above duty (spec §3) → Task 6 (`GuardAttackFloor + prox*GuardAttackGain`, Δ=0). ✓
- Round-robin gate assignment (spec §3) → Task 4 (`i % GatePosts.Count`). ✓
- `Patrol`/`StandWatch` activities (spec §3) → Task 5. ✓
- GateMap from grid + WALL blocks (spec §3, corrected from `ComputeGates`) → Task 1–2. ✓
- Curfew: day open / night closed via overlay, no cost-grid mutation (spec §3) → Task 3 (`blockGates`) + Task 7 (monster passes `night`). ✓
- Monster blocked at wall at night, watch attacks (spec §3, §4) → Task 7 (collide/stall) + Task 6 (boosted Attack) + `CombatSystem` (existing). ✓
- Monster hunger + spawn-outside + pathfind + collide (spec §4) → Task 7. ✓
- Lean scope, solo monster (spec §6) → no pack code anywhere. ✓
- Honesty metrics (spec §6) → Task 8. ✓
- Curfew visual 446↔447 in-but-cuttable (spec §3) → Task 9. ✓
- Verify via histogram/endpoints, not Sim.Tests (spec §6) → every task's verification. ✓
- Emergent abandonment → free from existing Hunger prepotency + Δ=0 duty losing to a real hunger gap; no code, verified by `kills`/`posting` dropping when guards eat. ✓

**Type consistency:** `GateBlock` (Task 1) consumed by `IsGateCell` (Task 1), `ComputeGatePosts` (Task 2), `TownPathfinder` (Task 3), `MetricsSystem` (Task 8) — same name throughout. `EmploymentData.{GateIndex,NightShift,GateX,GateZ}` defined Task 4, read Task 6 — consistent. `CreatureData.HungerLevel` defined Task 7, read Task 8 — consistent. `ActivityKind.Patrol/StandWatch` defined Task 5, used Task 6/8 — consistent.

**Known soft spots flagged for the implementer (not placeholders — explicit instructions to adapt to real names):** the `OddSystem`/`CreatureSystem` construction sites and `SimWorld` accessor names (`w.Metrics`, `w.Creatures`, `Employment.All`) must be matched to the actual code via the `grep` commands given in each task; the exact line of `TownPathfinder`'s passability test must be located (the recon put it ~179). These are real wiring points, with the search command provided.
