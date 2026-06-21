# town3d Inspect Tooling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface the ODD decision tree for a selected agent, and add click-to-inspect for buildings (identity, residents, workers, settlement) in the town3d viewer.

**Architecture:** The sim already builds the `OddNode[]` decision tree and has registries for residency/employment/settlement/treasury. We (1) capture a *structured* snapshot of the tree for a *watched* agent in `OddSystem`, (2) expose it plus enriched building detail through the existing `/ws` channel in `Sim.Web/Program.cs`, and (3) render both in the existing `#detail` panel in `town3d.html` — one inspect target at a time, with Status/ODD tabs for agents and a swapped panel for buildings.

**Tech Stack:** C# (.NET 10), xUnit, System.Text.Json, vanilla JS + three.js (WebGL).

## Global Constraints

- Target framework is `net10.0`; tests use xUnit (`Headless/Sim.Tests`).
- No new NuGet/npm dependencies.
- Sim core types live in namespace `DaggerfallWorkshop.Sim` (data: `EntityId`, `BuildingRow`, `BuildingKind`, `ResidentRole`, `LineageData`) and `DaggerfallWorkshop.Sim.Engine` (systems/registries: `OddSystem`, `TreasuryRegistry`). `OddNode`/`OddTree` are in `DaggerfallWorkshop.Sim`.
- The WS receive loop reads live registries from a non-sim thread; every cross-thread read is wrapped in a 3-try `catch (InvalidOperationException) { Thread.Sleep(2); }` retry (existing pattern in `InspectEntity`).
- Run the viewer with: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Web -- <Region> <Location>` (env var per memory; town mode). Use a small walled town for light testing, e.g. `-- "Daggerfall" "Gothway Garden"` (default), or a 5×6 walled town like Gallotale.
- JSON payloads serialize **anonymous objects / properties**, not public struct fields (the project's `jsonOptions` does not set `IncludeFields`). When emitting snapshot nodes, project them to anonymous objects.

---

### Task 1: OddSystem structured snapshot + watch channel (sim core)

Capture the decision tree as a structured, serializable snapshot for agents the viewer is watching. Pure builder is unit-tested; the watch set and store are static (mirroring the existing `SnapshotEnabled`/`Snapshots`).

**Files:**
- Modify: `Assets/Sim/Engine/Units/OddSystem.cs` (add DTOs, statics, builder, capture call)
- Test: `Headless/Sim.Tests/OddSnapshotTests.cs` (create)

**Interfaces:**
- Produces:
  - `DaggerfallWorkshop.Sim.Engine.OddSnapNode` — `struct { int Parent; string Verb; double Direct; double Prop; double Total; bool Terminal; }`
  - `DaggerfallWorkshop.Sim.Engine.OddSnapshotData` — `sealed class { int Hour; int Minute; int Chosen; OddSnapNode[] Nodes; }` (`Chosen` = index into `Nodes` of the winning root child, `-1` if none; `Nodes[0]` is the root sentinel with `Verb=null`, `Parent=-1`)
  - `static ConcurrentDictionary<EntityId,byte> OddSystem.SnapshotWatch`
  - `static ConcurrentDictionary<EntityId,OddSnapshotData> OddSystem.StructuredSnapshots`
  - `static OddSnapshotData OddSystem.BuildSnapshot(OddNode[] buf, int n, IReadOnlyList<ActivityKind> verbs, int hour, int minute)`

- [ ] **Step 1: Write the failing test**

Create `Headless/Sim.Tests/OddSnapshotTests.cs`:

```csharp
using System.Collections.Generic;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.Tests
{
    /// BuildSnapshot must faithfully mirror the OddNode buffer: same parent links,
    /// per-node scores, and the same winner Traverse picks (highest-Total root child).
    public class OddSnapshotTests
    {
        // Chain: 0=Idle, 1=Labor, 2=Buy, 3=Farm. Labor→Buy→Farm; index 3 is the reward.
        // (Verb names are cosmetic labels for the test; only the indices drive scoring.)
        static readonly int[][] Chain = { new int[0], new[] { 2 }, new[] { 3 }, new int[0] };
        static readonly double[] Direct = { 0.05, 0.01, 0.01, 1.0 };
        static readonly List<ActivityKind> Verbs = new List<ActivityKind>
            { ActivityKind.Idle, ActivityKind.Labor, ActivityKind.Buy, ActivityKind.Farm };

        [Fact]
        public void BuildSnapshot_MarksChosenRoot_AndPreservesParentLinks()
        {
            var buf = new OddNode[64];
            int n = OddTree.Build(buf, 4, new[] { 0, 1 }, a => Chain[a], _ => true, a => Direct[a]);
            OddTree.Propagate(buf, n, 0.5);
            int winnerVerb = OddTree.Traverse(buf, n);   // verb index of the chosen root action

            var snap = OddSystem.BuildSnapshot(buf, n, Verbs, 8, 14);

            Assert.Equal(8, snap.Hour);
            Assert.Equal(14, snap.Minute);
            Assert.Equal(n, snap.Nodes.Length);
            Assert.Null(snap.Nodes[0].Verb);                 // root sentinel
            Assert.Equal(-1, snap.Nodes[0].Parent);
            // Chosen node is a root child whose verb matches Traverse's pick (Work).
            Assert.True(snap.Chosen > 0);
            Assert.Equal(0, snap.Nodes[snap.Chosen].Parent); // it's a root-level action
            Assert.Equal(Verbs[winnerVerb].ToString(), snap.Nodes[snap.Chosen].Verb);
            Assert.Equal("Labor", snap.Nodes[snap.Chosen].Verb);
            // Every non-root node's parent index is valid and its Total = Direct + Prop.
            for (int i = 1; i < n; i++)
            {
                Assert.InRange(snap.Nodes[i].Parent, 0, n - 1);
                Assert.Equal(snap.Nodes[i].Direct + snap.Nodes[i].Prop, snap.Nodes[i].Total, 6);
            }
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Headless/Sim.Tests/Sim.Tests.csproj --filter "FullyQualifiedName~OddSnapshotTests"`
Expected: FAIL to compile — `OddSystem.BuildSnapshot` / `OddSnapNode` / `OddSnapshotData` do not exist.

- [ ] **Step 3: Add the DTOs, statics, and builder to OddSystem**

In `Assets/Sim/Engine/Units/OddSystem.cs`, add these members inside the `OddSystem` class, immediately after the existing snapshot block (after the line `public static readonly System.Collections.Concurrent.ConcurrentDictionary<EntityId, string> Snapshots ... ;` near line 484):

```csharp
        // --- Structured decision snapshot for the live viewer (observability). ---
        // Watched ids are set by the web layer; only watched agents pay the capture cost.
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<EntityId, byte> SnapshotWatch
            = new System.Collections.Concurrent.ConcurrentDictionary<EntityId, byte>();
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<EntityId, OddSnapshotData> StructuredSnapshots
            = new System.Collections.Concurrent.ConcurrentDictionary<EntityId, OddSnapshotData>();

        /// Project the BFS OddNode buffer into a flat, serializable snapshot. Node 0 is
        /// the root sentinel (Verb=null). Chosen mirrors Traverse: the highest-Total
        /// root child (first one wins ties), as a node index; -1 if the root has none.
        public static OddSnapshotData BuildSnapshot(
            OddNode[] buf, int n, IReadOnlyList<ActivityKind> verbs, int hour, int minute)
        {
            var nodes = new OddSnapNode[n];
            for (int i = 0; i < n; i++)
            {
                var node = buf[i];
                nodes[i] = new OddSnapNode
                {
                    Parent = node.ParentIndex,
                    Verb = node.Action >= 0 ? verbs[node.Action].ToString() : null,
                    Direct = node.DirectScore,
                    Prop = node.PropagatedScore,
                    Total = node.Total,
                    Terminal = node.IsTerminal,
                };
            }
            int chosen = -1;
            if (n > 1 && buf[0].ChildStart <= buf[0].ChildEnd)
            {
                chosen = buf[0].ChildStart;
                for (int c = buf[0].ChildStart + 1; c <= buf[0].ChildEnd; c++)
                    if (buf[c].Total > buf[chosen].Total) chosen = c;
            }
            return new OddSnapshotData { Hour = hour, Minute = minute, Chosen = chosen, Nodes = nodes };
        }
```

Then add the two public types at the END of the `OddSystem.cs` file, inside `namespace DaggerfallWorkshop.Sim.Engine` but **outside** the `OddSystem` class (after the closing `}` of the class, before the namespace's closing `}`):

```csharp
    /// One node of a serialized ODD decision tree (see OddSystem.BuildSnapshot).
    public struct OddSnapNode
    {
        public int Parent;     // index into the snapshot's Nodes array; -1 only for node 0
        public string Verb;    // ActivityKind name; null for the root sentinel
        public double Direct;
        public double Prop;
        public double Total;
        public bool Terminal;
    }

    /// A captured ODD decision tree for one agent at one decision instant.
    public sealed class OddSnapshotData
    {
        public int Hour;
        public int Minute;
        public int Chosen;        // index into Nodes of the chosen root action; -1 = none
        public OddSnapNode[] Nodes;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Headless/Sim.Tests/Sim.Tests.csproj --filter "FullyQualifiedName~OddSnapshotTests"`
Expected: PASS (1 test).

- [ ] **Step 5: Wire the capture into Decide**

In `Assets/Sim/Engine/Units/OddSystem.cs`, in `Decide(...)`, find this existing block (around line 220):

```csharp
            int winner = OddTree.Traverse(buffer, n);
            if (SnapshotEnabled && (n > roots.Count + 1 || !Snapshots.ContainsKey(id)))
                Snapshots[id] = FormatTree(buffer, n, verbs);
```

Add the structured capture immediately after it:

```csharp
            if (SnapshotWatch.ContainsKey(id))
                StructuredSnapshots[id] = BuildSnapshot(
                    buffer, n, verbs, _worldClock.Current.Hour, _worldClock.Current.Minute);
```

- [ ] **Step 6: Verify the whole core still builds and the test still passes**

Run: `dotnet build Headless/Sim.Web/Sim.Web.csproj && dotnet test Headless/Sim.Tests/Sim.Tests.csproj --filter "FullyQualifiedName~OddSnapshotTests"`
Expected: build succeeds; the OddSnapshotTests test passes.

- [ ] **Step 7: Commit**

```bash
git add Assets/Sim/Engine/Units/OddSystem.cs Headless/Sim.Tests/OddSnapshotTests.cs
git commit -m "OddSystem: structured per-agent decision snapshot + watch channel"
```

---

### Task 2: Expose ODD snapshot + watch over the WebSocket (Sim.Web)

Add a `watch` message that toggles `SnapshotWatch` for the selected agent, clear it on disconnect, and attach the captured tree to the `detail` response.

**Files:**
- Modify: `Headless/Sim.Web/Program.cs` (WS receive loop ~395–421; disconnect ~423–426; `InspectEntity` ~513–550)

**Interfaces:**
- Consumes: `OddSystem.SnapshotWatch`, `OddSystem.StructuredSnapshots`, `OddSnapshotData` (Task 1).
- Produces: WS in `{type:"watch", id:<int>|null}`; `detail` response gains `odd: { ts:string, chosen:int, nodes:[{parent,verb,direct,prop,total,terminal}] } | null`.

- [ ] **Step 1: Ensure the Engine namespace is in scope**

At the top of `Headless/Sim.Web/Program.cs`, confirm there is a `using DaggerfallWorkshop.Sim.Engine;` among the usings. If absent, add it (the file already uses `EntityId` from `DaggerfallWorkshop.Sim`; add the `using` for `OddSystem`).

- [ ] **Step 2: Track the watched id per connection**

In the `/ws` handler, find the line declaring the camera ring (around line 335):

```csharp
    int viewMx = -1, viewMy = -1, viewR = 0;
```

Add directly below it:

```csharp
    // ODD snapshot: the single agent this connection is watching (-1 = none).
    int watchedId = -1;
```

- [ ] **Step 3: Add the `watch` message case**

In the `switch (t.GetString())` block, after the existing `case "speed" ...: ... break;` (around line 420), add:

```csharp
                case "watch":
                    // Stop watching whatever this connection watched before.
                    if (watchedId >= 0)
                    {
                        OddSystem.SnapshotWatch.TryRemove(new EntityId(watchedId), out _);
                        OddSystem.StructuredSnapshots.TryRemove(new EntityId(watchedId), out _);
                        watchedId = -1;
                    }
                    if (doc.RootElement.TryGetProperty("id", out var widProp)
                        && widProp.ValueKind == JsonValueKind.Number)
                    {
                        watchedId = widProp.GetInt32();
                        OddSystem.SnapshotWatch[new EntityId(watchedId)] = 1;
                    }
                    break;
```

- [ ] **Step 4: Clear the watch on disconnect**

Find the end of the receive loop / catch (around lines 422–426):

```csharp
    }
    catch (WebSocketException) { /* client went away mid-frame */ }

    try { await pump; } catch { /* pump dies with the socket */ }
```

Insert the cleanup between the `catch` and the `try { await pump; }`:

```csharp
    catch (WebSocketException) { /* client went away mid-frame */ }

    if (watchedId >= 0)
    {
        OddSystem.SnapshotWatch.TryRemove(new EntityId(watchedId), out _);
        OddSystem.StructuredSnapshots.TryRemove(new EntityId(watchedId), out _);
    }

    try { await pump; } catch { /* pump dies with the socket */ }
```

- [ ] **Step 5: Attach the snapshot to InspectEntity**

In `InspectEntity(EntityId id)`, inside the `try`, after the existing registry reads (after `world.Lineage.TryGet(id, out var lin);`, around line 524), add:

```csharp
            object odd = null;
            if (OddSystem.StructuredSnapshots.TryGetValue(id, out var snap) && snap.Nodes != null)
                odd = new
                {
                    ts = $"{snap.Hour:00}:{snap.Minute:00}",
                    chosen = snap.Chosen,
                    nodes = snap.Nodes.Select(nd => new
                    {
                        parent = nd.Parent, verb = nd.Verb, direct = nd.Direct,
                        prop = nd.Prop, total = nd.Total, terminal = nd.Terminal,
                    }),
                };
```

Then add `odd = odd,` to the returned anonymous object (after the `z = pos != null ? pos.Z : 0f,` line, before the closing `};`):

```csharp
                z = pos != null ? pos.Z : 0f,
                odd = odd,
            };
```

(`System.Linq` is already used in this file via `.Select` in the snap pump; no new using needed.)

- [ ] **Step 6: Build and smoke-run**

Run: `dotnet build Headless/Sim.Web/Sim.Web.csproj`
Expected: build succeeds, no errors.

Then smoke-run (Ctrl-C after it prints `spectating …`):
Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 timeout 20 dotnet run --project Headless/Sim.Web -- "Daggerfall" "Gothway Garden"`
Expected: prints `spectating Daggerfall / Gothway Garden (N civilians) at http://localhost:8080` and runs without throwing. (Behavioral check of the `odd` payload happens end-to-end in Task 4.)

- [ ] **Step 7: Commit**

```bash
git add Headless/Sim.Web/Program.cs
git commit -m "Sim.Web: watch channel + ODD snapshot in the detail response"
```

---

### Task 3: Building enrichment + nearest-building pick (Sim.Web)

Enrich `InspectBuilding` with name/residents/workers/settlement, and add a `pickBuilding` resolver that maps a clicked world point to the nearest building (applying GeoRemap in region mode).

**Files:**
- Modify: `Headless/Sim.Web/Program.cs` (`InspectBuilding` ~552–572; new `PickBuilding` helper; WS `pickBuilding` case)

**Interfaces:**
- Consumes: `world.Buildings`, `world.Residency`, `world.Employment`, `world.Settlements`, `world.Treasury`, `world.Lineage`, `world.Identity`, `world.Geography.GeoRemap`, the `wholeRegion` bool.
- Produces:
  - WS in `{type:"pickBuilding", x:<number>, z:<number>}` → `{type:"building", building:{...}}`.
  - Enriched `building` payload: `{ i, kind, quality, faction, x, z, name, production, residents:[{id,name,role}], workers:[{id,name}], settlement:{name,kind,residents,treasury} | null }`.

- [ ] **Step 1: Replace InspectBuilding with the enriched version**

In `Headless/Sim.Web/Program.cs`, replace the entire `InspectBuilding` method (lines ~552–572) with:

```csharp
object InspectBuilding(int i)
{
    for (int attempt = 0; attempt < 3; attempt++)
    {
        try
        {
            if (!world.Buildings.TryGet(i, out var b)) return new { i, missing = true };

            // Residents (and surnames for the building's display name) in one scan.
            var residents = new List<object>();
            string keeperSurname = null, anySurname = null;
            foreach (var kv in world.Residency.All)
            {
                if (kv.Value == null || kv.Value.BuildingIndex != i) continue;
                string rname = world.Identity.TryGet(kv.Key, out var rid) ? rid.Name : null;
                residents.Add(new { id = kv.Key.Value, name = rname, role = kv.Value.Role.ToString() });
                if (world.Lineage.TryGet(kv.Key, out var lin) && lin != null && !string.IsNullOrEmpty(lin.Surname))
                {
                    if (kv.Value.Role == ResidentRole.Keeper && keeperSurname == null) keeperSurname = lin.Surname;
                    if (anySurname == null) anySurname = lin.Surname;
                }
            }

            // Workers: employees whose employer's residency points at this building.
            var workers = new List<object>();
            foreach (var kv in world.Employment.All)
            {
                var emp = kv.Value;
                if (emp == null || emp.Employer.IsNone) continue;
                if (world.Residency.TryGet(emp.Employer, out var er) && er != null && er.BuildingIndex == i)
                    workers.Add(new { id = kv.Key.Value, name = world.Identity.TryGet(kv.Key, out var wid) ? wid.Name : null });
            }

            // Settlement membership (the loader tags each building to one settlement).
            object settlement = null;
            foreach (var s in world.Settlements.All)
                if (s.Buildings.Contains(i))
                {
                    settlement = new
                    {
                        name = s.Name,
                        kind = s.Kind.ToString(),
                        residents = s.Residents.Count,
                        treasury = world.Treasury.Get(s.Treasury),
                    };
                    break;
                }

            string kindLabel = b.Kind.ToString();
            bool isHome = (b.Kind >= BuildingKind.House1 && b.Kind <= BuildingKind.House6)
                          || b.Kind == BuildingKind.HouseForSale;
            string name = keeperSurname != null ? $"{keeperSurname}'s {kindLabel}"
                        : (isHome && anySurname != null) ? $"{anySurname} residence"
                        : kindLabel;
            string production = b.Kind switch
            {
                BuildingKind.Farm => "provisions",
                BuildingKind.Fishery => "fish",
                BuildingKind.Mine => "ore",
                BuildingKind.Pasture => "wool",
                BuildingKind.Weaver => "cloth",
                BuildingKind.ClothingStore => "attire",
                _ => null,
            };

            return new
            {
                i, kind = kindLabel, quality = b.Quality, faction = b.FactionId,
                x = b.X, z = b.Z, name, production, residents, workers, settlement,
            };
        }
        catch (InvalidOperationException) { Thread.Sleep(2); }
    }
    return new { i, busy = true };
}
```

- [ ] **Step 2: Add the PickBuilding resolver**

Directly below `InspectBuilding`, add:

```csharp
// Resolve a clicked world point to the nearest building, then return its detail.
// Region mode places building meshes (and agents) in geo-remapped world space, so
// remap each building's town-local origin before comparing; town mode compares raw.
object PickBuilding(double x, double z)
{
    for (int attempt = 0; attempt < 3; attempt++)
    {
        try
        {
            int bestI = -1; double best = double.MaxValue;
            foreach (var kv in world.Buildings.All)
            {
                double bx = kv.Value.X, bz = kv.Value.Z;
                if (wholeRegion)
                {
                    var (gx, _, gz) = world.Geography.GeoRemap(kv.Value.X, kv.Value.Z);
                    bx = gx; bz = gz;
                }
                double dx = bx - x, dz = bz - z, d2 = dx * dx + dz * dz;
                if (d2 < best) { best = d2; bestI = kv.Key; }
            }
            if (bestI < 0) return new { missing = true };
            return InspectBuilding(bestI);
        }
        catch (InvalidOperationException) { Thread.Sleep(2); }
    }
    return new { busy = true };
}
```

- [ ] **Step 3: Add the `pickBuilding` WS case**

In the `switch (t.GetString())` block, after the `case "inspectBuilding" ...: ... break;` (around line 405), add:

```csharp
                case "pickBuilding"
                    when doc.RootElement.TryGetProperty("x", out var pbx)
                      && doc.RootElement.TryGetProperty("z", out var pbz):
                    await Send(JsonSerializer.SerializeToUtf8Bytes(
                        new { type = "building", building = PickBuilding(pbx.GetDouble(), pbz.GetDouble()) }, jsonOptions));
                    break;
```

- [ ] **Step 4: Build and smoke-run**

Run: `dotnet build Headless/Sim.Web/Sim.Web.csproj`
Expected: build succeeds.

Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 timeout 20 dotnet run --project Headless/Sim.Web -- "Daggerfall" "Gothway Garden"`
Expected: starts and prints the `spectating …` line without throwing. (Behavioral check in Task 4.)

- [ ] **Step 5: Commit**

```bash
git add Headless/Sim.Web/Program.cs
git commit -m "Sim.Web: enrich building inspect + nearest-building pick resolver"
```

---

### Task 4: town3d client — tabs, ODD tree, building panel, raycast (end-to-end)

Add the building raycast, the watch send on selection, the Status/ODD tab UI with the collapsible tree, and the building panel with jump-select. This task's verification is the full end-to-end check.

**Files:**
- Modify: `Headless/Sim.Web/wwwroot/town3d.html` (CSS ~19–29; selection/render JS ~748–808; WS onmessage ~903–908)

**Interfaces:**
- Consumes: `detail` payload with optional `odd` (Task 2); enriched `building` payload (Task 3); WS out `{type:"inspect"|"watch"|"pickBuilding"}`.
- Produces: none (leaf UI).

- [ ] **Step 1: Add panel/tab/tree CSS**

In `Headless/Sim.Web/wwwroot/town3d.html`, find the `#detail` rule (line 19):

```css
  #detail { position: absolute; top: 10px; right: 10px; width: 220px; z-index: 10; display: none;
            background: rgba(20,24,32,.9); border: 1px solid #2c3444; border-radius: 6px; padding: 10px 12px; }
```

Replace it with (wider, scrollable for the tree):

```css
  #detail { position: absolute; top: 10px; right: 10px; width: 260px; max-height: 82vh; overflow: auto; z-index: 10; display: none;
            background: rgba(20,24,32,.9); border: 1px solid #2c3444; border-radius: 6px; padding: 10px 12px; }
```

Then add these rules immediately after the existing `#detail .dlink { ... }` line (line 29):

```css
  #detail .tabs { display: flex; gap: 4px; margin: 4px 0 6px; }
  #detail .tab { padding: 1px 8px; border: 1px solid #2c3444; border-radius: 4px; color: #8893a7; cursor: pointer; }
  #detail .tab.active { color: #0e1117; background: #9fd0ff; border-color: #9fd0ff; }
  #detail .odd { font-size: 12px; }
  #detail .tnode { white-space: nowrap; display: flex; justify-content: space-between; gap: 8px; padding: 1px 0; }
  #detail .tnode.chosen { color: #ffd479; }
  #detail .tnode .sc { color: #6b768a; }
  #detail .tcaret { cursor: pointer; color: #9fd0ff; }
  #detail .role { color: #6b768a; }
```

- [ ] **Step 2: Replace the selection state + pointerup handler**

Find the selection state and pointerup handler (lines ~750–763):

```javascript
let selectedId = -1, inspectTimer = null, downX = 0, downY = 0;

// Distinguish a click from an orbit-drag: only pick if the pointer barely moved.
renderer.domElement.addEventListener('pointerdown', e => { downX = e.clientX; downY = e.clientY; });
renderer.domElement.addEventListener('pointerup', e => {
  if (e.button !== 0 || Math.hypot(e.clientX - downX, e.clientY - downY) > 5) return;
  ptr.x = (e.clientX / innerWidth) * 2 - 1;
  ptr.y = -(e.clientY / innerHeight) * 2 + 1;
  raycaster.setFromCamera(ptr, camera);
  const hit = raycaster.intersectObjects(agents.children, false)[0];
  if (hit && hit.object.userData.id != null) selectAgent(hit.object.userData.id);
  else deselect();
});
addEventListener('keydown', e => { if (e.code === 'Escape') deselect(); });
```

Replace it with:

```javascript
let selectedId = -1, selectedBuildingI = -1, inspectTimer = null, downX = 0, downY = 0;
let agentTab = 'status', lastAgentDetail = null, oddCollapsed = new Set();

// Distinguish a click from an orbit-drag: only pick if the pointer barely moved.
renderer.domElement.addEventListener('pointerdown', e => { downX = e.clientX; downY = e.clientY; });
renderer.domElement.addEventListener('pointerup', e => {
  if (e.button !== 0 || Math.hypot(e.clientX - downX, e.clientY - downY) > 5) return;
  ptr.x = (e.clientX / innerWidth) * 2 - 1;
  ptr.y = -(e.clientY / innerHeight) * 2 + 1;
  raycaster.setFromCamera(ptr, camera);
  const hitA = raycaster.intersectObjects(agents.children, false)[0];
  if (hitA && hitA.object.userData.id != null) { selectAgent(hitA.object.userData.id); return; }
  // No agent under the cursor: try a building. Recurse — region tiles are nested groups.
  const hitB = raycaster.intersectObjects(town.children, true)[0];
  if (hitB) { pickBuilding(hitB.point.x, hitB.point.z); return; }
  deselect();
});
addEventListener('keydown', e => { if (e.code === 'Escape') deselect(); });
```

- [ ] **Step 3: Replace selectAgent/deselect with selection + building functions**

Find `selectAgent` and `deselect` (lines ~765–777):

```javascript
function selectAgent(id) {
  selectedId = id;
  ui('detail').style.display = 'block';
  ui('detail').innerHTML = `<div class="dh">#${id}…</div>`;
  const ask = () => { if (agentWs && agentWs.readyState === 1) agentWs.send(JSON.stringify({ type: 'inspect', id })); };
  ask();
  clearInterval(inspectTimer);
  inspectTimer = setInterval(ask, 1000);   // keep the panel live while selected
}
function deselect() {
  selectedId = -1; clearInterval(inspectTimer); inspectTimer = null;
  ui('detail').style.display = 'none'; selRing.visible = false;
}
```

Replace with:

```javascript
function send(o) { if (agentWs && agentWs.readyState === 1) agentWs.send(JSON.stringify(o)); }

// Drop whatever is currently selected (agent or building) and stop watching.
function clearSelection() {
  if (selectedId >= 0) send({ type: 'watch', id: null });
  selectedId = -1; selectedBuildingI = -1; lastAgentDetail = null; oddCollapsed.clear();
  clearInterval(inspectTimer); inspectTimer = null;
  selRing.visible = false;
}
function deselect() { clearSelection(); ui('detail').style.display = 'none'; }

function selectAgent(id) {
  if (selectedId === id) return;
  clearSelection();
  selectedId = id; agentTab = 'status';
  ui('detail').style.display = 'block';
  ui('detail').innerHTML = `<div class="dh">#${id}…</div>`;
  send({ type: 'watch', id });                       // start ODD snapshotting this agent
  const ask = () => send({ type: 'inspect', id });
  ask();
  inspectTimer = setInterval(ask, 1000);             // keep the panel live while selected
}

function pickBuilding(x, z) {
  clearSelection();
  selectedBuildingI = -2;                            // pending (any non -1 marks "building mode")
  ui('detail').style.display = 'block';
  ui('detail').innerHTML = `<div class="dh">building…</div>`;
  send({ type: 'pickBuilding', x, z });
}
```

- [ ] **Step 4: Replace renderDetail/wireClose with the tabbed agent + building renderers**

Find `renderDetail` and `wireClose` (lines ~781–802):

```javascript
function renderDetail(d) {
  if (!d || d.id !== selectedId) return;   // stale (deselected or reselected since the ask)
  if (d.busy) return;                      // transient registry contention — keep last render
  if (d.missing) { ui('detail').innerHTML = `<div class="dh"><span class="dx">×</span>#${d.id}</div><div class="dim">no longer here</div>`; wireClose(); return; }
  let h = `<div class="dh"><span class="dx">×</span>#${d.id} ${d.name ?? ''}</div>`
    + `<div class="dim">${d.kind} · ${d.race} · lvl ${d.level}</div>`
    + `<div class="dim">activity: ${d.activity} (${d.phase})</div>`
    + `<div class="dim">coin: ${(d.coin ?? 0).toFixed(2)}</div>`
    + (d.spouse >= 0 ? `<div class="dim">spouse: <span class="dlink" data-id="${d.spouse}">#${d.spouse}</span></div>` : '');
  if (Array.isArray(d.needs)) {
    h += `<div class="ndh">needs</div>`;
    for (let i = 0; i < NEED_LABELS.length; i++) h += needBar(NEED_LABELS[i], d.needs[i]);
  }
  ui('detail').innerHTML = h;
  wireClose();
}
function wireClose() {
  const x = ui('detail').querySelector('.dx');
  if (x) x.onclick = deselect;
  const sp = ui('detail').querySelector('.dlink');   // jump to the spouse
  if (sp) sp.onclick = () => selectAgent(Number(sp.dataset.id));
}
```

Replace with:

```javascript
function renderDetail(d) {
  if (!d || d.id !== selectedId) return;   // stale (deselected or reselected since the ask)
  if (d.busy) return;                      // transient registry contention — keep last render
  lastAgentDetail = d;
  renderAgentPanel();
}

function renderAgentPanel() {
  const d = lastAgentDetail; if (!d) return;
  if (d.missing) {
    ui('detail').innerHTML = `<div class="dh"><span class="dx">×</span>#${d.id}</div><div class="dim">no longer here</div>`;
    wireAgent(); return;
  }
  let h = `<div class="dh"><span class="dx">×</span>#${d.id} ${d.name ?? ''}</div>`
    + `<div class="tabs"><span class="tab ${agentTab === 'status' ? 'active' : ''}" data-tab="status">status</span>`
    + `<span class="tab ${agentTab === 'odd' ? 'active' : ''}" data-tab="odd">ODD</span></div>`;
  if (agentTab === 'status') {
    h += `<div class="dim">${d.kind} · ${d.race} · lvl ${d.level}</div>`
      + `<div class="dim">activity: ${d.activity} (${d.phase})</div>`
      + `<div class="dim">coin: ${(d.coin ?? 0).toFixed(2)}</div>`
      + (d.spouse >= 0 ? `<div class="dim">spouse: <span class="dlink" data-id="${d.spouse}">#${d.spouse}</span></div>` : '');
    if (Array.isArray(d.needs)) {
      h += `<div class="ndh">needs</div>`;
      for (let i = 0; i < NEED_LABELS.length; i++) h += needBar(NEED_LABELS[i], d.needs[i]);
    }
  } else {
    h += renderOdd(d.odd);
  }
  ui('detail').innerHTML = h;
  wireAgent();
}

// Reconstruct the tree from the flat nodes[] via parent links. Node 0 is the root
// sentinel; its children are the root-level actions. Children sort by Total desc.
function renderOdd(odd) {
  if (!odd || !Array.isArray(odd.nodes) || odd.nodes.length < 2)
    return `<div class="dim">no decision captured yet…</div>`;
  const kids = {};
  odd.nodes.forEach((n, i) => { if (i === 0) return; (kids[n.parent] ||= []).push(i); });
  let out = `<div class="dim">decided ${odd.ts ?? '—'}</div><div class="odd">`;
  const walk = (idx, depth) => {
    const cs = (kids[idx] || []).slice().sort((a, b) => odd.nodes[b].total - odd.nodes[a].total);
    for (const ci of cs) {
      const n = odd.nodes[ci];
      const hasKids = !!kids[ci];
      const collapsed = oddCollapsed.has(ci);
      const caret = hasKids ? `<span class="tcaret" data-idx="${ci}">${collapsed ? '▸' : '▾'}</span> ` : '• ';
      const mark = ci === odd.chosen ? ' ◄' : '';
      out += `<div class="tnode${ci === odd.chosen ? ' chosen' : ''}" style="padding-left:${depth * 12}px">`
        + `<span>${caret}${n.verb}${mark}</span>`
        + `<span class="sc">${n.direct.toFixed(2)} / ${n.prop.toFixed(2)} / <b>${n.total.toFixed(2)}</b></span>`
        + `</div>`;
      if (hasKids && !collapsed) walk(ci, depth + 1);
    }
  };
  walk(0, 0);
  return out + `</div>`;
}

function renderBuilding(b) {
  if (selectedBuildingI === -1) return;   // deselected since the request
  if (b.busy) return;
  selectedBuildingI = b.i ?? -2;
  if (b.missing) {
    ui('detail').innerHTML = `<div class="dh"><span class="dx">×</span>building</div><div class="dim">none here</div>`;
    wireBuilding(); return;
  }
  let h = `<div class="dh"><span class="dx">×</span>${b.name ?? b.kind}</div>`
    + `<div class="dim">${b.kind}${b.production ? ' · ' + b.production : ''} · q${b.quality}</div>`;
  if (b.settlement)
    h += `<div class="dim">${b.settlement.name} (${b.settlement.kind}) · pop ${b.settlement.residents} · treasury ${Math.round(b.settlement.treasury)}</div>`;
  if (Array.isArray(b.residents) && b.residents.length) {
    h += `<div class="ndh">residents</div>`;
    for (const r of b.residents)
      h += `<div class="dim">• <span class="dlink" data-id="${r.id}">${r.name ?? '#' + r.id}</span> <span class="role">${r.role}</span></div>`;
  }
  if (Array.isArray(b.workers) && b.workers.length) {
    h += `<div class="ndh">workers</div>`;
    for (const w of b.workers)
      h += `<div class="dim">• <span class="dlink" data-id="${w.id}">${w.name ?? '#' + w.id}</span></div>`;
  }
  ui('detail').innerHTML = h;
  wireBuilding();
}

function wireAgent() {
  const p = ui('detail');
  const x = p.querySelector('.dx'); if (x) x.onclick = deselect;
  p.querySelectorAll('.tab').forEach(t => t.onclick = () => { agentTab = t.dataset.tab; renderAgentPanel(); });
  p.querySelectorAll('.dlink').forEach(el => el.onclick = () => selectAgent(Number(el.dataset.id)));
  p.querySelectorAll('.tcaret').forEach(el => el.onclick = () => {
    const i = Number(el.dataset.idx);
    if (oddCollapsed.has(i)) oddCollapsed.delete(i); else oddCollapsed.add(i);
    renderAgentPanel();
  });
}
function wireBuilding() {
  const p = ui('detail');
  const x = p.querySelector('.dx'); if (x) x.onclick = deselect;
  p.querySelectorAll('.dlink').forEach(el => el.onclick = () => selectAgent(Number(el.dataset.id)));
}
```

- [ ] **Step 5: Route the `building` message**

Find the WS `onmessage` handler (lines ~903–907):

```javascript
  agentWs.onmessage = (ev) => {
    const msg = JSON.parse(ev.data);
    if (msg.type === 'snap') onSnap(msg);        // ignore the 'world' frame; we have /asset/town
    else if (msg.type === 'detail') renderDetail(msg.detail);
  };
```

Replace with:

```javascript
  agentWs.onmessage = (ev) => {
    const msg = JSON.parse(ev.data);
    if (msg.type === 'snap') onSnap(msg);        // ignore the 'world' frame; we have /asset/town
    else if (msg.type === 'detail') renderDetail(msg.detail);
    else if (msg.type === 'building') renderBuilding(msg.building);
  };
```

- [ ] **Step 6: End-to-end verification (run + observe in a browser)**

Start the viewer:
Run: `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Web -- "Daggerfall" "Gothway Garden"`

Then open `http://localhost:8080/town3d.html` and confirm:
1. **Agent ODD:** click a person → panel shows `#id name` with `status`/`ODD` tabs. The `status` tab shows needs (as before). Click `ODD` → after ~1–2 s a tree appears with `verb` rows, `dir / prop / total` columns, the chosen root marked `◄` in amber, and `▾/▸` carets that collapse/expand. (If it says "no decision captured yet…", wait for the agent to re-decide — activity expiry or dawn/dusk.)
2. **Building:** click a building → panel swaps to the building's name, kind/production/quality, a settlement line (name · pop · treasury), and `residents`/`workers` lists where present.
3. **Jump-select:** click a resident/worker (or a spouse) name → panel swaps back to that agent in agent mode.
4. **One-at-a-time:** selecting a building clears the agent (and vice-versa); `×` / Esc closes the panel.
5. **No console errors** in devtools.

Stop the host (Ctrl-C).

- [ ] **Step 7: Commit**

```bash
git add Headless/Sim.Web/wwwroot/town3d.html
git commit -m "town3d: ODD decision tree + building/settlement inspect panel"
```

---

## Notes / risks

- **Region-mode building pick** assumes `world.Geography.GeoRemap` is the same transform that places streamed building meshes (it is the one used for agents in the snap pump). Town mode is exact. If region-mode picks land on the wrong building, verify the towntile placement transform against `GeoRemap` — this is the one spot to re-check.
- **Multiple WS clients:** `SnapshotWatch`/`StructuredSnapshots` are global. With two viewers watching different agents both work; if two watch the *same* agent and one deselects, the other's snapshot stops updating until its next `inspect`/`watch`. Acceptable for a dev spectator tool; noted, not handled.
- **Building name** is synthesized from resident surnames (`<Surname>'s <Kind>` / `<Surname> residence`) rather than Daggerfall's `BuildingNames.GetName` (which lives in the Unity game layer and isn't wired into the headless host). Authentic DF names are a future enhancement.
