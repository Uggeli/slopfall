# town3d Inspect Tooling — Design

Date: 2026-06-21
Status: Approved (pending spec review)

## Goal

Make the simulation debuggable from the town3d viewer by surfacing two things
that the sim already knows but the UI never shows:

1. **ODD decision reasoning** — the full decision tree the planner evaluated for
   a selected agent (candidate verbs, the Enables-DAG expansion, and the
   direct / propagated / total scores per node, with the chosen path marked).
2. **Building / settlement detail** — click a building to see its identity, who
   lives there, who works there, its economic role, and its settlement context.

Read-only. No new sim behavior, no editing.

## Current state (what already exists)

- **Agent inspect works.** Clicking an agent raycasts the billboard, sends
  `{type:"inspect", id}` over `/ws`, server `InspectEntity()`
  (`Headless/Sim.Web/Program.cs:513`) returns name / kind / needs / coin /
  spouse, and the client `#detail` panel renders it via `renderDetail()`
  (`town3d.html:781`). The poll repeats every 1 s (`town3d.html:765`).
- **ODD snapshots exist but are not surfaced.** `OddSystem` can capture the
  decision tree as *text* (`SnapshotEnabled` + `Snapshots[id]` +
  `FormatTree()`, `OddSystem.cs:482`). Nothing pipes it to the client, and it
  is text, not structured. The live buffer is `OddNode[]`
  (`OddTree.cs:8`: `{ParentIndex, Action, ChildStart/End, DirectScore,
  PropagatedScore, IsTerminal}`) built in `OddSystem.Decide`.
- **Building inspect: server half only.** `{type:"inspectBuilding", i}` →
  `InspectBuilding()` (`Program.cs:552`) returns `i / kind / quality /
  faction / x / z`. There is **no** building-click in `town3d.html`, and the
  payload has no name / residents / workers / settlement.
- **Linkage registries exist** (`SimWorld.cs:41`): `ResidencyRegistry`,
  `EmploymentRegistry`, `SettlementRegistry` (with a `Residents` roster +
  `Treasury`). All are `EntityId → data` maps exposing `.All`, so
  building → residents/workers is a cheap reverse-scan for one building.

## Design

### 1. ODD decision tree (server)

- **Watch channel.** Add a watched-id mechanism to `OddSystem`
  (`SnapshotWatch`), set from the WS thread using the same cross-thread path
  the existing `inspect` / `speed` messages already use to reach the sim. Only
  watched agents are snapshotted, so cost is zero when nothing is selected and
  one-agent when something is.
- **Structured snapshot.** When a *watched* agent re-decides, serialize its
  `OddNode[]` into a DTO instead of text:
  - `nodes[]` of `{ parent:int, verb:string, direct:float, prop:float,
    total:float, terminal:bool }` (verb resolved from the verb index to a
    human name),
  - `chosenRootIndex:int` (the root action Traverse selected),
  - `ts` (decision game-time, hour:minute).
  - Stored in `Dictionary<EntityId, OddSnapshot>` on `OddSystem`, read by
    Sim.Web. Overwritten on each re-decide so it stays current ("live").

### 2. Building enrichment (server)

Extend `InspectBuilding()` to also return:

- **Identity:** resolved building name (from `NameSeed`), kind, quality,
  faction.
- **Residents:** reverse-scan `ResidencyRegistry.All` filtered to this building
  index → `[{ id, name, role }]` (role from `ResidentRole`).
- **Workers:** reverse-scan `EmploymentRegistry.All` filtered to this building
  → `[{ id, name }]`, plus a static production label derived from
  `BuildingKind` (Farm → provisions, Weaver → cloth, Fishery → fish, …).
- **Settlement:** from `SettlementRegistry` — settlement name, resident count,
  treasury.

The reverse-scans are O(N over residents/employees) and run only on click, so
no per-tick cost.

### 3. WebSocket protocol

- `{type:"watch", id}` / `{type:"watch", id:null}` — client sends on agent
  select / deselect to start/stop ODD snapshotting.
- The existing 1 s `inspect` → `detail` response gains an optional
  `odd: { nodes, chosen, ts }` block, present only when the agent is watched
  and a snapshot has been captured for it.
- The `building` response gains the enriched fields from §2.

### 4. town3d client

- **Building raycast (the missing half).** On `pointerup`, if no agent billboard
  is hit, raycast against the baked building instances; map the hit instance →
  building index and send `{type:"inspectBuilding", i}`.
- **Unified `#detail` panel — one inspect target at a time.** Selecting an
  agent or a building clears the other (constraint: never inspect two things at
  once).
  - **Agent mode:** a tab bar `[Status] [ODD]` inside the panel.
    - *Status* = existing needs/info view (`renderDetail`).
    - *ODD* = collapsible tree reconstructed client-side by linking each node's
      `parent` index; chosen path highlighted; score columns shown as
      `dir / prop / ★total`. Collapsible via `▼ / ▸` carets.
  - **Building mode:** the panel swaps to show identity / residents / workers +
    production / settlement context. Resident and worker names are **clickable
    → jump-select** that agent (swaps panel to agent mode, sends `inspect` +
    `watch`).

### 5. Component boundaries

- `OddSystem`: owns the watch set and structured snapshot store. Knows nothing
  about WS/JSON.
- `Sim.Web/Program.cs`: translates WS messages ↔ registry/`OddSystem` reads;
  owns JSON shape.
- `town3d.html`: owns rendering, selection state, the panel/tabs, and the
  tree-from-flat-array reconstruction.

## Out of scope

- No new sim behavior or decision changes.
- No settlement-wide map / overview screen.
- No editing or interaction beyond read-only inspection.
- The three open backlog bugs (coins, fishing spots, wander movement) — tracked
  separately.

## Risks / notes

- **Cross-thread access.** The watch set and snapshot store are written by the
  sim thread and read by the WS thread. Follow whatever synchronization the
  existing `inspect` / `speed` path already relies on; do not invent a new
  concurrency model. Confirm during implementation.
- **Verb-name resolution** must match the verb-index ordering used when the tree
  was built, or scores will attach to the wrong labels.
