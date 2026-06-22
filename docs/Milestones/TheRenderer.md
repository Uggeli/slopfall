# MSxx — The Renderer (town3d → the modular client)        [client] + [infra]

Goal: promote `town3d.html` from a 1118-line monolith into THE client — a modular
renderer where an engine layer draws a `Frame` knowing nothing about *how* you look at
the world, and the observer overview is just one *mode* sitting on top. Delete every
competing client in the same pass. The payoff is structural, not a feature: once the
engine↔mode boundary exists, a first-person/player mode inherits the continuous world,
atmosphere, NPC crowd, assets, and transport **for free** — so the work that mode needs
shrinks to its control loop, not its renderer.

This is the collection point for the build break and all `[client]` presentation/input
work that's currently smeared across `town3d.html`. It is independent of the porting
backlog and can run whenever. It is NOT the final Unity excision (that's the separate
`[infra]` "remove vendored DFU scaffolding", gated on the porting backlog).

## Depends on (gate before digging in)

Nothing hard. The substrate already exists:
- The CQRS core rewrite landed; `Sim.Web` already publishes its own immutable
  `WorldRunner.Frame` to the browser and builds green. The renderer reads a `Frame` —
  that contract is in place.
- `docs/render_client_dataflow.md` is the coordinate/orientation contract this refactor
  must preserve verbatim (see Guardrails). It is current and correct.

The one red thing (`Sim.Net` / the `RenderSnapshot` build break, TODOS.md line 15) is
resolved *inside* this milestone by deletion (R0), not by re-adding the type.

## The design hinge (the invariant this milestone exists to establish)

**The engine↔mode boundary.** `engine/` draws a `Frame` and streams the world around a
*focal point*. It must not know whether that focal point is an orbit-camera target
(observer) or an avatar position (player), and must not reference any UI/inspector DOM.
A mode supplies: a camera rig, a focal point, input handling, and which UI overlays to
mount.

Get this right → first-person is free on the rendering side (feed it the avatar's
position; everything streams/renders identically). Get it wrong → split the file by
function without the boundary and the modes can't share, defeating the point. **The line
to defend: nothing in `engine/` references `OrbitControls`, the observer camera, or the
inspector.**

## Module layout (functions already cluster this way in town3d.html)

```
engine/        ← mode-agnostic core: "given a Frame + a focal point + a camera, draw the world"
  scene        renderer, scene, lights, render loop, gfx settings (fog/brightness/FOV/render-scale)
  assets       model/texture/atlas/sprite-sheet/flat-sheet caches — the /asset/* client
  terrain      ensureTerrainMat, buildTerrain/buildTileMesh, positionTerrain, streamRegionTerrain
  world        addStructureTile + loadModel (buildings) + buildFlatInstances/getFlatSheet (nature flats)
  agents       billboard pool (poolFor/getSheet/hash32), setCellUV, updateAgents (interp from Frame)
  atmosphere   updateAtmosphere, weatherMods (day/night sky, sun arc, weather tint, fog)
net/
  client       WebSocket connect, Frame decode, onSnap, snapshot buffer + interp, send (speed/inspect)
ui/
  inspector    selectAgent/pickBuilding, renderDetail/AgentPanel/Odd/Building, needBar, wire*
  hud          clock/weather readout, speed buttons, gfx sliders (syncGfx/setFogSlider)
modes/
  observer     orbit + WASD fly-cam (moveCamera/followGround/frame*), click-to-inspect  ← today's town3d
  player       (NOT built here — see "Enabled, not built here")
main           pick a mode; wire engine + net + ui
```

## Stages

- [infra] **R0 — Build green by deletion.** Delete `Sim.Net` (binary-TCP, the orphaned
  remote path). Deleting it exposed that `Sim.Tests` has been stale and non-building since
  the CQRS rewrite (`f385dd848` deleted the old serial core; the Sim.Net dep was masking
  ~13 further errors) — so delete the whole stale `Sim.Tests` project too (sources stay in
  git history for a future CQRS test-port). Remove both from `Sim.slnx`. Done = whole
  solution builds green; the live suites pass (`Sim.MemoryTests`, `Sim.SpatialTests`).
  TODOS.md line 15 cleared. This is the safety net for the client carves below — a green
  server-side build to refactor against (the client itself is gated by R0.5's parity gate,
  not the deleted Sim.Tests).
- [infra] **R0.5 — Parity harness (the mechanical "looks identical" gate).** DONE. The whole
  milestone's acceptance is per-stage visual parity, but there is no JS test harness and
  no browser here. Build one: a Playwright + bundled-Chromium (SwiftShader software-WebGL)
  setup that boots `Sim.Web` at a fixed seed, loads town3d in a deterministic **capture
  mode** (fixed camera pose, fixed time-of-day, captures the first published frame
  at-or-past `CAP_TICK` then pauses — the exact captured tick is timing-dependent,
  deterministic on a fixed machine but the baseline is single-machine-scoped; a different
  machine could land on a different tick → false FAIL; server-side "run to tick N then
  pause" is the clean portability upgrade if ever needed), screenshots, and diffs against
  a committed baseline within a small tolerance (sub-pixel AA noise).
  - **De-risk first:** the opening task is a spike. Playwright 1.61 + its Chromium are
    already cached (`~/.cache/ms-playwright/chromium-1228`), so no install — just render a
    WebGL page headlessly and confirm a non-blank PNG of the expected scene. If SwiftShader
    can't render Three.js on this box, fall back to manual eyeballing and drop the rest of
    R0.5 — but learn that in minutes, not after building the harness.
  - **Capture mode** is the one place we touch town3d outside a pure move: a `?capture=`
    URL mode (or query params) that sets the camera + pauses the clock at a fixed tick.
    This is test infrastructure, not a rendering change, and it stays in as the regression
    gate. It also adds the repo's first `package.json` + a Playwright dev-dependency.
  - **Baseline** is captured here, from the known-good *current* town3d, before any
    carving. Done = `npm run parity` green against baseline on unmodified town3d.
  - **Per-stage parity check (run from `Headless/Sim.Web/parity`):** `npm run parity`
    (boots Sim.Web at Gothway Garden + captures tick 50 + diffs vs `baseline/gothway.png`).
    A pure move must report PASS (<=0.1% pixels differ). Re-run after each of R1–R4.
  - Verified reproducible run-to-run (7/921600 px = 0.001%); gate proven to bite on a
    sky-colour change (735001/921600 px = 79.753% FAIL → revert → 3/921600 px = 0.000% PASS).
- [client] **R1 — Carve `net/`.** Lift the WebSocket / `Frame` decode / `onSnap` /
  interpolation / `send` out of `town3d.html` into `net/client.js`; town3d imports it.
  Smallest seam, zero visual change. Done = the town renders identically, all transport
  goes through `net/`.
- [client] **R2 — Carve `engine/`.** DONE. Extracted the six engine submodules in dependency
  order (scene → assets → atmosphere → world → terrain → agents), each a pure move parity-gated
  to PASS. `town3d.html` dropped 1167 → 594 lines; `engine/` is 656 lines across the six files.
  The engine↔mode boundary holds: streaming/billboarding take a *focal point* + camera + net
  from the shell (the mode owns those); `engine/` references no `OrbitControls`, camera framing,
  or HUD/inspector DOM in code (the camera-framing fly-cam, the inspector/HUD, and the `animate()`
  render loop stay in the shell → `modes/observer` in R4). Design:
  `docs/superpowers/specs/2026-06-22-the-renderer-r2-engine-carve-design.md`.
  Note: the committed parity baseline (`baseline/gothway.png`) was captured on a different machine
  and FAILs here on tick drift; R2 was gated against a local `baseline/local-r2-base.png` from the
  unmodified town3d (run-to-run noise floor ~0.01%). A server-side "run to tick N then pause" is
  still the clean cross-machine fix (see R0.5).
- [client] **R3 — Carve `ui/`.** DONE. Three modules: `ui/comms.js` (utterance feed, no deps),
  `ui/inspector.js` (`mountInspector({net,camera})` — picking + selection + all detail panels),
  `ui/hud.js` (`mountHud({net,camera})` — gfx sliders, debug toggles, speed buttons, clock/weather
  readout, status line). The mode mounts them and feeds them `net` + the camera it owns; net's
  `onDetail/onBuilding/onStatus/onOpen` callbacks delegate to ui. `town3d.html` dropped 594 → 346
  lines (all DOM-widget logic now in `ui/`, 278 lines). Each carve parity-gated (comms/inspector/
  hud all PASS; final 4/921600 px = 0.000%); inspector click path validated by an interaction smoke
  (`parity/smoke-r3.mjs`: agents stream → click → panel opens, zero console errors). Design:
  `docs/superpowers/specs/2026-06-22-the-renderer-r3-ui-carve-design.md`. The shell is now just the
  fly-cam + capture + animate loop + onSnapshot + init — i.e. the future `modes/observer` + `main`.
- [client] **R4 — The boundary: focal-point contract + `modes/observer`.** Introduce the
  explicit `engine` API (`render(frame)`, streams around a focal point; a camera the mode
  owns) and reframe today's behaviour as `modes/observer` (orbit/fly cam → focal point =
  camera target; mounts the inspector + HUD). This is the deliverable that makes
  first-person free. Done = observer is a ~thin mode over engine+net+ui, and the engine
  takes a focal point it can't tell apart from an avatar's.
- [client] **R5 — Sole-client cutover.** Delete `wwwroot/index.html` (2D canvas
  spectator) and `wwwroot/view3d.html` (single-model debug viewer); make the town client
  the default page (update `Sim.Web/Program.cs` static-file/default-doc routing). Done =
  exactly one web client exists; no parallel viewer left to rot.

## Enabled, not built here

- **`modes/player` (first-person / player-as-agent).** A new mode that reuses all of
  `engine/` + `net/`: it supplies an eye-height camera rig and feeds the avatar position
  as the focal point. The *rendering* is free after R4. What it still needs is its own
  work, tracked elsewhere: the **player-as-agent control loop** — input frames → server
  verbs → sim moves the avatar → authoritative position returns in the next `Frame` —
  plus a join/spawn flow. That's the `[client]` + multiplayer-mechanics backlog
  (TODOS.md: "player verbs + input frames", agent possession & handoff, join/spawn), and
  it sits on the "player = entity with `ControlledByInput`" / MMO-by-accident direction.
  This milestone makes that work small; it does not do it.

## Guardrails (acceptance constraints, not polish)

- **The boundary is the product.** Nothing in `engine/` may reference `OrbitControls`,
  the observer camera, or the inspector/HUD DOM. If an extraction needs to, the cut is
  wrong — push that concern up into the mode. This is the one rule the milestone exists
  to enforce; everything else is mechanics.
- **Behaviour parity per stage.** Every extraction is a pure move. The rendered town must
  look identical after each stage (same Gothway Garden, same crowd, same atmosphere). No
  "improve while refactoring" — new rendering features (water, interiors, post-processing,
  seasons, flat animation) are separate TODO items and stay out. **Enforced mechanically:**
  the R0.5 parity harness (screenshot-diff vs. baseline) is re-run after each of R1–R4; a
  pure move must land within tolerance. If the R0.5 spike shows headless WebGL is
  infeasible here, this degrades to manual per-stage eyeballing — but parity is still the
  gate either way.
- **Preserve the server-bakes-everything rule.** Per `docs/render_client_dataflow.md`:
  the server bakes all geometry/orientation into DFU's native world frame and the client
  renders raw — no client-side flips/rotations/transposes. The refactor must not
  reintroduce any client-side coordinate transform. Known orientation issues (if any
  remain) are left exactly as-is; this is structural work only.
- **One client at the end.** No window where zero viewers work, and no two viewers left
  alive afterward. Client-page deletions (R5) come *after* the 3D client is the proven
  sole renderer.
- **Deletion is final for the TCP path.** Removing `Sim.Net` forecloses the
  engine-agnostic binary-TCP transport (the old Godot/native-client path). The browser
  talks to `Sim.Web` directly and never needed it. Revisit only if a non-browser client
  is ever actually wanted — at which point it's a fresh transport on `net/`, not a revival.

## Deferred within this milestone
- The player-as-agent control loop, collision/free-roam physics, and first-person
  *gameplay* (this milestone only unlocks them — see "Enabled, not built here").
- All new rendering features (water shader, building interiors, post-processing, season
  visual swap, flat/wind animation) — separate `[absent]`/`[stub]` TODO items.
- A native/Godot client and any non-browser transport (foreclosed by R0; out of scope).
- The final Unity excision — deleting the vendored DFU tree (`Assets/Scripts/`,
  `Assets/Game/`) — is a different `[infra]` milestone gated on the porting backlog
  (TODOS.md line 276), not this one.
