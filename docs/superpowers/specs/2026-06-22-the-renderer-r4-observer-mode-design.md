# The Renderer — R4: focal-point contract + `modes/observer` (design)

Part of the milestone in `docs/Milestones/TheRenderer.md` (stage R4). Gated by the R0.5
parity harness (local baseline). Builds on R1–R3. **This is the deliverable that makes a
first-person `modes/player` free** — it establishes the one boundary the milestone exists for.

## Goal

Introduce an explicit engine **render API** that streams + draws the world around a *focal
point* it can't tell apart from an avatar's, and reframe the remaining `town3d.html` shell as
`modes/observer` (orbit/fly cam → focal point = `controls.target`; mounts the inspector + HUD).
After R4, `town3d.html` is thin `main`: create net + engine + observer, load the town, start.

**R4 done = observer is a ~thin mode over engine+net+ui; `engine.render` takes `{camera, focal}`
and nothing else view-specific; Gothway parity PASS.**

## The focal-point contract (the invariant)

```js
const engine = createEngine({ net });
engine.render({ camera, focal, dt, now, selectedId });
```

- `camera` — the THREE camera the **mode** owns (billboarding + the final draw).
- `focal` — a `Vector3` the world streams around. Observer: `controls.target`. Player (future):
  the avatar's position. **The engine cannot distinguish the two** — that's the whole point.
- `dt`, `now` — frame timing (seconds).
- `selectedId` — the inspector's current selection (for the agent's foot-ring); `-1 if none`.

`engine.render` does exactly today's per-frame engine work, in the canvas-affecting order it
runs today: `updateGates(world.night)` → `updateAtmosphere(dt)` →
`streamRegionTerrain(dt, focal, camera, net)` → `updateAgents(now, camera, net, selectedId)` →
`renderer.render(scene, camera)`. No camera *movement*, no `OrbitControls`, no input — those
are the mode's.

## Modules

```
engine/index.js   the engine facade: createEngine({net}) → { render(ctx) }. Plus updateGates
                  + gate state move to engine/world.js (setGatePairs / updateGates(night)).
modes/observer.js createObserver({ net, engine }) → { camera, get focal, frameTown, frameRegion,
                  maybeCapture, start, hud, inspector, get capturing }.
                  Owns the camera + OrbitControls + fly-cam (moveCamera/followGround/frame*),
                  the capture-mode state, the rAF loop, and MOUNTS the inspector + HUD.
town3d.html       thin `main`: onSnapshot, createNet, createEngine, createObserver, init(), start.
```

## Carve order (each parity-gated)

**Step 1 — extract `engine.render`.**
- `engine/world.js`: add `let gatePairs`, `setGatePairs(g)`, `updateGates(night)` (the per-frame
  gate-visibility toggle).
- `engine/index.js` (new): `createEngine({net})` whose `render({camera,focal,dt,now,selectedId})`
  runs the five-step engine block above.
- `town3d.html`: in `animate`, replace the inline gate-toggle + `updateAtmosphere` +
  `streamRegionTerrain` + `updateAgents` + `renderer.render` with `engine.render({…})`; replace
  `gatePairs = gates` (init) with `setGatePairs(gates)`. Camera/controls/fly-cam/capture/loop
  + hud/inspector mounts stay in the shell for now. Clock (`hud.updateClock()`) stays in the loop;
  it's DOM-only so order vs the engine block is irrelevant. Parity PASS.

**Step 2 — extract `modes/observer`.**
- `modes/observer.js` (new): `createObserver({net, engine})` moves camera + `OrbitControls` +
  `moveCamera`/`followGround`/`frame`/`frameRegion` + `keys`/resize + capture state + the rAF
  loop, and **mounts** `hud`/`inspector` (the milestone's "mode mounts the inspector + HUD").
  - `get focal()` → `controls.target`; `camera` getter.
  - `update(dt)` runs `moveCamera`+`followGround`+`controls.update()`; the loop then runs the
    capture-settle, `engine.render({camera, focal, dt, now, selectedId: inspector.selectedId})`,
    and `hud.updateClock()` — preserving today's canvas order (capture-settle's idempotent
    `controls.update()` before the draw; clock is DOM-only).
  - `maybeCapture(tick)` — the onSnapshot CAP_TICK branch (set CAP_CAM pose + `hud.setSpeed(0)`).
  - `frameTown(min,max)` / `frameRegion()` for init; `start()` kicks the rAF loop.
- `town3d.html` becomes thin main: `onSnapshot` calls `pushUtterances` + writes engine `world` +
  `observer.maybeCapture(msg.tick)` + `stat(...)`; net callbacks delegate to
  `observer.inspector`/`observer.hud`; init frames via `observer.frameTown/frameRegion`; then
  `observer.start()`. Parity PASS + inspector smoke (re-run `smoke-r3.mjs`).

## Boundary checks (R4 done)

- `grep -rnE "OrbitControls|controls\.|moveCamera|followGround" engine/` → nothing (engine never
  moves a camera).
- `engine.render`'s signature references only `camera`/`focal` for the view — no observer/inspector
  types. A player mode could call it with `focal = avatarPos` unchanged.
- `town3d.html` (main) contains no `OrbitControls`, no per-frame engine draw calls — only mode +
  net + ui wiring + init.

## Guardrails

- **Pure move, no behaviour change.** Same Gothway render, crowd, atmosphere, inspect, HUD,
  capture. Parity + the inspector smoke enforce it. Preserve the canvas-affecting per-frame order.
- **No client-side coordinate transforms** (server bakes everything).
- **The boundary is the product:** `engine/` stays free of `OrbitControls`/camera-movement/DOM;
  `modes/observer` owns the camera and hands the engine a focal point. Get this right and
  `modes/player` is free on the rendering side.
- **No new features** (no player mode, no free-roam physics — R4 only unlocks them).
