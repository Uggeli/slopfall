# The Renderer — R1: carve `net/client.js` (design)

Part of the milestone in `docs/Milestones/TheRenderer.md` (stage R1). Gated by the R0.5 parity harness (`Headless/Sim.Web/parity` → `npm run parity`).

## Goal

Lift the network layer out of the `town3d.html` monolith into a standalone, mode-agnostic `net/client.js` ES module, with **zero visual change** (the parity gate must report PASS). This is the smallest seam and the first enforcement of the engine↔mode boundary: `net/` must reference no `OrbitControls`, observer camera, inspector, or HUD DOM.

## Boundary decision

The milestone lists "interp" under both `net/` and `agents/`. Resolution (user, 2026-06-22): **`net/` owns transport + decode + the snapshot double-buffer + interpolation *timing* (the `alpha` factor); `agents/` keeps the per-agent position lerp** (carved in R2). This is the smallest seam — the per-agent lerp stays inside `updateAgents`, reading `net`'s buffer + `alpha`.

## Module

`Headless/Sim.Web/wwwroot/net/client.js`, an ES module imported by town3d's existing inline `<script type="module">` (same importmap setup). **Browser globals (`WebSocket`, `location`) are referenced only inside `connect()`**, so the module is importable in Node for a unit test without a DOM.

## Interface (factory)

```js
export function createNet({ onSnapshot, onDetail, onBuilding }) {
  return {
    connect(),          // open ws://<host>/ws, wire handlers, auto-reconnect on close
    send(obj),          // JSON send if socket open
    setSpeed(scale),    // send {type:'speed',scale}
    get prevSnap(),     // Map id -> [x,z,yaw,...]  (previous snapshot)
    get curSnap(),      // Map id -> [x,z,yaw,...]  (current snapshot)
    get prevTime(),     // arrival time of prevSnap
    get curTime(),      // arrival time of curSnap
    alpha(now),         // interpolation factor clamped 0..1 for render time `now`
  };
}
```

`onSnapshot(msg)`, `onDetail(detail)`, `onBuilding(building)` are app-supplied callbacks invoked after net updates its internal state.

## What moves into `net/`

- `agentWs` + `connectAgents` (connect, `onopen`/`onerror`/`onmessage`/`onclose`, 1 s reconnect).
- The *buffer half* of `onSnap`: rotate `prevSnap←curSnap` and `prevTime←curTime`, decode `msg.entities` into the new `curSnap`, stamp `curTime`.
- The generic `send`, and `setSpeed`'s transport (`send({type:'speed',scale})`).
- The `alpha(now)` computation (currently inline in `updateAgents`): `curTime ? clamp01((now - curTime)/interval) : 0`, where `interval` is the existing snapshot-gap basis.

## What stays in town3d (this stage)

- The per-agent **lerp** in `updateAgents` — now reads `net.prevSnap/curSnap` + `net.alpha(now)`. (→ `agents/` in R2.)
- `onSnap`'s **app-side reactions**, moved into the `onSnapshot(msg)` callback town3d registers: `world.hour/minute/night/sun/weather`, `pushUtterances(msg.utterances)` (→ `ui/` in R3), the capture-mode tick check (`_capState`/`CAP_TICK` → `setSpeed(0)` + camera pose), and the status line.
- `setSpeed`'s HUD-button highlight: a town3d wrapper calls `net.setSpeed(scale)` then updates the `#speed` buttons. (→ `ui/` in R3.)

## Data flow

```
ws message → net.onmessage → JSON.parse
  'snap'     → net: rotate buffer, decode entities, stamp curTime → onSnapshot(msg)
  'detail'   → onDetail(msg.detail)
  'building' → onBuilding(msg.building)

render loop → updateAgents → reads net.curSnap / net.prevSnap / net.alpha(now) → lerp
senders (inspect / watch / pickBuilding / view / speed) → net.send(...) / net.setSpeed(...)
```

## Testing

1. **Parity gate (acceptance):** `cd Headless/Sim.Web/parity && npm run parity` must report PASS (≤0.1% pixels) — the town renders pixel-identically and all transport goes through `net/`.
2. **Unit test (`node:test`, zero new deps):** import `net/client.js` in Node and verify its pure logic without a browser/WebSocket — `alpha(now)` clamps to `[0,1]` and returns 0 before the first snapshot; a simulated snapshot ingestion rotates `prev←cur` and decodes `entities` into `curSnap`. (Requires factoring the snapshot-ingestion step so it is callable without opening a socket — e.g. an internal `ingest(msg, now)` the `onmessage` handler delegates to.)

## Guardrails

- **Pure move, no behaviour change.** Same rendered town, same crowd/atmosphere. The parity gate enforces it.
- **No client-side coordinate transforms** (server bakes everything; `docs/render_client_dataflow.md`).
- **`net/` is mode-agnostic:** no reference to `OrbitControls`, the observer camera, the inspector, or HUD DOM. If an extraction needs one, the cut is wrong — push it up into the app/mode.
- **No new rendering/transport features** — R1 is a move only.
