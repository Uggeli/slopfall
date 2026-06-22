# The Renderer — R2: carve `engine/` (design)

Part of the milestone in `docs/Milestones/TheRenderer.md` (stage R2). Gated by the R0.5
parity harness (`Headless/Sim.Web/parity` → `npm run parity`). Builds on R1 (`net/` carved).

## Goal

Extract the six engine submodules out of the `town3d.html` monolith into ES modules under
`wwwroot/engine/`, **one at a time, each a pure move with zero visual change** (parity PASS
after every extraction). After R2, `town3d.html` is a thin consumer of `engine/` + `net/`:
it still owns the camera, `OrbitControls`, the fly-cam/framing, the inspector/HUD DOM, and
the `animate()` render loop — those are mode/ui concerns carved in R3/R4. The R2 deliverable
is just the six engine files and the boundary they establish.

**R2 done = Gothway Garden renders pixel-identically AND nothing under `engine/` references
`OrbitControls`, the inspector, or the HUD DOM** (`ui('clock')`, `ui('fog')`, …).

## The two boundary rules this carve must respect

1. **No `OrbitControls`, no camera *framing/movement* in `engine/`.** The fly-cam
   (`moveCamera`), ground-follow (`followGround`, `terrainTargetMeshes`), and framing
   (`frame`, `frameRegion`) all drive `controls`/`camera` — they are *observer-mode* and
   **stay in the shell** (→ `modes/observer` in R4). Engine modules may *read* the camera
   (e.g. `agents` billboards toward `camera.position`) — the camera is a shared object the
   mode owns and the engine is handed; reading it is fine, owning/`OrbitControls`-ing it is not.
2. **No HUD/inspector DOM in `engine/`.** `engine/` must not call `ui('clock')`, `ui('fog')`,
   the inspector panels, etc. Where an engine function currently writes the HUD
   (`updateAtmosphere` → `ui('clock')`, the gfx sliders), that DOM write **stays in the
   shell** for R2 and the engine exposes the underlying state instead. (HUD → `ui/` in R3.)

## State-threading strategy (the key decision)

**Shared-singleton modules — `engine/scene.js` is the root.** The monolith already shares
state as module-level `const`/`let`; the smallest-diff pure move is to keep that shape but
move the bindings into the module that owns them and `import` them where needed. No
giant "context object" threaded through every call (that would touch every line and isn't a
pure move).

- **THREE core singletons** (created once, never reassigned) live in `engine/scene.js` and
  are imported by the other engine modules: `renderer`, `scene`, `ambient`, `sun`, `grid`,
  plus the shared `THREE` namespace re-export is unnecessary (each module imports `three`).
- **Reassigned `let` state cannot be imported-and-reassigned across modules** (ES live
  bindings are read-only to importers). So any cross-module mutable state becomes a **property
  on an exported object** (mutating `obj.x` is fine across modules). Concretely:
  - `engine/scene.js` exports `gfx = { bright:1, fogOn:true, fogFar:1600 }` and
    `world = { hour:12, minute:0, night:false, sun:1, weather:'Sunny' }`. `world` is the
    per-frame render input the shell's `onSnapshot` writes and `atmosphere` reads — it IS the
    "Frame" the engine draws. `gfx` is the graphics-settings state the HUD writes (R3) and
    `scene`/`atmosphere` read.
  - Module-private reassigned `let`s that no other module touches stay `let` inside their
    owning module (e.g. terrain's `terrainTiles`, `terrainMat`, `regionMeta`, `townClimate`,
    `townSeason`, `lastViewMx/My`, `groundAccum`, `groundY`).
  - `const` Maps/Sets (`regionTiles`, `regionStructures`, `sheets`, `people`, `flatSheets`)
    move with their owning module and are mutated in place — no reassignment problem.

## Module layout & cut lines

```
engine/
  scene.js       renderer, scene, ambient, sun, grid; exported state objects `world` + `gfx`;
                 renderer-resize listener. NO camera, NO OrbitControls, NO render loop.
  assets.js      manager, loader (GLTFLoader), texLoader, applyNearest, loadModel.
                 The /asset/model + texture-filter client. (sprite/flat sheets live with their
                 consumers: agents/world — see below.)
  terrain.js     ATLAS_* consts, ensureTerrainMat, buildTerrain, buildTileMesh, positionTerrain,
                 streamRegionTerrain, terrainGroup, regionMeta/regionTiles/regionStructures*,
                 townClimate/townSeason, lastViewMx/My, the /asset/terrain+towntile+regionmeta
                 stream. (*regionStructures building groups are built here via world.addStructureTile.)
  world.js       town group, addStructureTile, flats group, getFlatSheet, makeFlatMaterial,
                 buildFlatInstances, setCellUV (shared with agents — exported), flat consts.
                 Buildings + nature flats.
  agents.js      agents group, sprite pools, sheets cache, people map, poolFor, getSheet, hash32,
                 ORI_*/FPS_*/RK_*/ACT_* consts, updateAgents(t, camera, net), selRing.
                 Reads camera (passed) + net buffer; per-agent lerp.
  atmosphere.js  SKY_* colors, weatherMods, updateAtmosphere(dt) → writes scene.background/fog,
                 sun, ambient from `world` + `gfx`. Does NOT write ui('clock') (shell does).
```

### Stays in `town3d.html` (the shell) after R2

- Camera + `OrbitControls` + `controls` config.
- `moveCamera`, `followGround`, `terrainTargetMeshes`, `frame`, `frameRegion` (observer cam).
- `animate()` render loop (the thin consumer — calls engine module fns; → engine `render()` in R4).
- Capture-mode state (`_capState`, `CAP_*`) — test infra wired to camera/controls.
- The inspector + HUD: `selectAgent`/`pickBuilding`/`renderDetail`/panels, `applyToggles`
  (reads `ui('mirx'…)`), `syncGfx`/`setFogSlider` (read sliders, write `gfx`), `stat`, comms log,
  the `ui('clock')` write that was in `updateAtmosphere`.
- `init()` orchestration (fetch town/region, place buildings, kick streams) — calls into engine.
- `net` instance + `onSnapshot` (writes `engine.world`).

## Cross-module dependency notes (the tricky edges)

- **`setCellUV`** is used by both `world` (flats) and `agents` (sprites). It's a pure UV helper
  → lives in `world.js`, exported, imported by `agents.js`. (Or a tiny `engine/uv.js`; keeping
  it in `world` avoids a 7th file — decide at implementation, parity is identical either way.)
- **`addStructureTile`** (region building streaming) is called from `terrain`'s
  `streamRegionTerrain`. It needs `loadModel` (assets) + the building cache. Put it in `world.js`,
  imported by `terrain.js`. Watch the import cycle terrain↔world: terrain calls
  `world.addStructureTile`; world doesn't call terrain. One-directional → no cycle.
- **`atmosphere.updateAtmosphere`** reads `engine.world` + `engine.gfx`, writes scene/sun/ambient.
  The `ui('clock')` line moves OUT to the shell (called right after `updateAtmosphere` in
  `animate`, or in `onSnapshot`). That single move is the only behavioural seam — verified by parity.
- **`agents.updateAgents(t)`** reads `camera.position` (billboarding) + `net` buffer. Pass
  `camera` and `net` in as args (engine reads, doesn't own). `selRing` (selection highlight) is
  drawn by agents but *positioned* from `selectedId` (inspector state) — pass `selectedId` in, or
  keep `selRing` visibility in the shell. Resolve at implementation; simplest: agents exports
  `setSelected(id)` the inspector calls.
- **gfx sliders:** `syncGfx` currently reads `ui('fog'/'bright'/'fov'/'res'/'fogOn')` and applies
  to `gfx`, `camera.fov`, `renderer`. It stays in the shell (HUD-coupled). It writes `engine.gfx`
  and calls `engine.scene` setters for renderer pixel-ratio. `engine/scene` exposes whatever
  apply-helpers are needed (e.g. `applyRenderScale(pct)`), but the slider *reading* is shell/HUD.

## Extraction order (each independently parity-gated)

Bottom-up by dependency, so each module imports only already-extracted ones:

1. **scene.js** — core singletons + `world`/`gfx` state. Shell imports them; delete the inline
   decls. (Highest blast radius: every other reference now reads from the module.)
2. **assets.js** — `loader`/`texLoader`/`loadModel`/`applyNearest`. Depends on nothing but `three`.
3. **atmosphere.js** — depends on scene (`world`,`gfx`,`scene`,`sun`,`ambient`). Move the
   `ui('clock')` write to the shell in the same step.
4. **terrain.js** — depends on scene; will call `world.addStructureTile` (forward-declared until
   step 5, or extract world first — see note). 
5. **world.js** — buildings + flats; provides `addStructureTile`, `setCellUV` to terrain/agents.
6. **agents.js** — depends on scene + world (`setCellUV`) + assets; reads camera + net.

**Order refinement:** because `terrain.streamRegionTerrain` calls `addStructureTile`, extract
**world before terrain** (swap 4↔5) so terrain imports a real `world.addStructureTile`. Final
order: scene → assets → atmosphere → world → terrain → agents. Each ends with `npm run parity` PASS.

## Testing

- **Acceptance (every step):** `cd Headless/Sim.Web/parity && npm run parity` → PASS (≤0.1%).
  Gate locally against `baseline/local-r2-base.png` (the committed `gothway.png` baseline was
  captured on a different machine — cross-machine tick drift FAILs it spuriously; run-to-run on
  this machine is ~0.01%). Re-run after each of the six extractions.
- **Boundary check (R2 done):** `grep -rE 'OrbitControls|ui\(' wwwroot/engine/` returns nothing
  (no controls, no HUD/inspector DOM). `grep -r 'controls' wwwroot/engine/` returns nothing.
- No unit tests here (these are THREE/DOM-bound rendering modules); the screenshot gate is the test.

## Guardrails

- **Pure move per submodule.** Same town, crowd, atmosphere after each. No "improve while
  refactoring" — no new water/interiors/post-fx/animation.
- **No client-side coordinate transforms** (server bakes everything; `docs/render_client_dataflow.md`).
- **Engine is mode-agnostic:** no `OrbitControls`, no camera framing/movement, no inspector/HUD DOM.
  If an extraction needs one, the cut is wrong — leave that function in the shell.
- **One client alive throughout** — town3d keeps rendering after every step (parity proves it).
