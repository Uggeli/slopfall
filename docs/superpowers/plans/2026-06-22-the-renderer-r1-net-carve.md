# The Renderer — R1: carve `net/client.js` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lift the network layer out of the `town3d.html` monolith into a standalone, mode-agnostic `net/client.js` ES module, with zero visual change (parity gate PASS).

**Architecture:** `net/` owns transport + Frame decode + the snapshot double-buffer + interpolation *timing* (`alpha`). The per-agent position lerp stays in `updateAgents` (carved to `agents/` in R2), now reading `net.curSnap/prevSnap` + `net.alpha(now)`. `onSnap`'s app-side reactions (world clock, utterances, capture-tick, status) move into an `onSnapshot(msg)` callback town3d registers. Two tasks: (1) build + unit-test the module in isolation; (2) integrate town3d and delete the moved code, gated by `npm run parity`.

**Tech Stack:** Browser ES modules (importmap, no bundler), Node 22 `node:test` (zero new deps), the R0.5 Playwright parity harness.

## Global Constraints

- **Pure move, zero visual change.** Acceptance is `cd Headless/Sim.Web/parity && npm run parity` → PASS (≤0.1% pixels). Same town, crowd, atmosphere.
- **`net/` is mode-agnostic:** it must reference NO `OrbitControls`, observer camera, inspector, or HUD/`#stat` DOM. App reactions come back via callbacks only.
- **Browser globals only inside `connect()`:** `WebSocket` and `location` may be referenced only inside `net`'s `connect()`, so the module imports cleanly in Node for the unit test.
- **No client-side coordinate transforms** (server bakes everything; `docs/render_client_dataflow.md`). This refactor moves code; it must not add any flip/rotate/transpose.
- **No new features.** Transport behaviour (reconnect after 1 s, the same wire messages) is preserved exactly.
- **Wire entity decode (preserve verbatim):** `e → [e[1], e[2], e[5], e[7]||0, e[6]||0, e[3]||0]` i.e. output `[x, z, yaw, groundY, kind, activity]` from wire indices `id=e[0], x=e[1], z=e[2], activity=e[3], yaw=e[5], kind=e[6], groundY=e[7]`.
- **Clock:** snapshot arrival time is `performance.now()/1000` (seconds); `alpha`'s render time `t` is the same seconds clock `updateAgents` already receives.

---

### Task 1: `net/client.js` module + unit test

Build the module in isolation with a `node:test` unit test for its pure logic (`ingest`, `alpha`, buffer rotation). Does not touch `town3d.html` yet, so the parity baseline is unaffected.

**Files:**
- Create: `Headless/Sim.Web/wwwroot/net/client.js`
- Create: `Headless/Sim.Web/parity/net-client.test.mjs`
- Modify: `Headless/Sim.Web/parity/package.json` (add a `"test"` script)

**Interfaces:**
- Consumes: nothing.
- Produces: `createNet({ onSnapshot, onDetail, onBuilding, onStatus, onOpen }) → { connect(), send(obj), ingest(msg, now), alpha(t), get prevSnap(), get curSnap(), get prevTime(), get curTime() }`.
  - `ingest(msg, now)`: rotates `prev←cur`, decodes `msg.entities` into `curSnap`, stamps `curTime = now`.
  - `alpha(t)`: `(curTime - prevTime) || 0.2` as the gap; returns `curTime ? min(1,(t-curTime)/gap) : 0`.
  - `send(obj)`: JSON-sends if socket open. `connect()`: opens `ws://${location.host}/ws`, wires handlers, reconnects after 1 s.

- [ ] **Step 1: Write the failing unit test**

Create `Headless/Sim.Web/parity/net-client.test.mjs`:
```js
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createNet } from '../wwwroot/net/client.js';

test('alpha is 0 before any snapshot', () => {
  const net = createNet({});
  assert.equal(net.alpha(123), 0);
});

test('ingest decodes entities and rotates prev<-cur', () => {
  const net = createNet({});
  // wire: [id, x, z, activity, (unused), yaw, kind, groundY]
  net.ingest({ entities: [[7, 10, 20, 2, 99, 90, 1, 5]] }, 100);
  assert.equal(net.curSnap.size, 1);
  assert.deepEqual(net.curSnap.get(7), [10, 20, 90, 5, 1, 2]); // [x,z,yaw,groundY,kind,activity]
  assert.equal(net.prevSnap.size, 0);
  net.ingest({ entities: [[7, 11, 21, 2, 99, 90, 1, 5]] }, 100.2);
  assert.equal(net.prevSnap.get(7)[0], 10); // previous cur became prev
  assert.equal(net.curSnap.get(7)[0], 11);
});

test('alpha uses the snapshot gap and clamps to [0,1]', () => {
  const net = createNet({});
  net.ingest({ entities: [] }, 100);
  net.ingest({ entities: [] }, 100.2); // gap = 0.2
  assert.equal(net.alpha(100.2), 0);   // at curTime
  assert.equal(net.alpha(100.3), 0.5); // halfway through the gap
  assert.equal(net.alpha(101), 1);     // clamped
});

test('onSnapshot/onDetail/onBuilding are invoked by ingest path only via connect; ingest stays pure', () => {
  let called = 0;
  const net = createNet({ onSnapshot: () => { called++; } });
  net.ingest({ entities: [] }, 1); // ingest itself must NOT fire onSnapshot
  assert.equal(called, 0);
});
```

- [ ] **Step 2: Add the test script and run it to verify it fails**

In `Headless/Sim.Web/parity/package.json`, add to `"scripts"`:
```json
    "test": "node --test"
```
Run: `cd /home/sakkivi/omat/daggerfall-unity/Headless/Sim.Web/parity && npm test`
Expected: FAIL — `Cannot find module '../wwwroot/net/client.js'` (the module does not exist yet).

- [ ] **Step 3: Implement the module**

Create `Headless/Sim.Web/wwwroot/net/client.js`:
```js
// net/client.js — mode-agnostic transport for the town3d client (The Renderer, R1).
// Owns: WebSocket connect/reconnect, Frame decode, the snapshot double-buffer, and
// interpolation timing (alpha). It must NOT reference OrbitControls, the observer
// camera, the inspector, or any HUD DOM — app-side reactions come back via callbacks.
// Browser globals (WebSocket, location) are used ONLY inside connect(), so this module
// imports cleanly in Node for unit tests.

const SNAP_INTERVAL = 0.2; // fallback gap (s); the web pump publishes ~5 Hz

export function createNet({ onSnapshot, onDetail, onBuilding, onStatus, onOpen } = {}) {
  let ws = null;
  let prevSnap = new Map(), curSnap = new Map(); // id -> [x, z, yaw, groundY, kind, activity]
  let prevTime = 0, curTime = 0;

  // Decode one 'snap' frame into the double-buffer. Pure (no socket/DOM); `now` is the
  // render clock in seconds. Exposed for tests; the live path calls it from onmessage.
  function ingest(msg, now) {
    prevSnap = curSnap; prevTime = curTime;
    // id -> [x, z, yaw, groundY, kind, activity]; groundY (idx 7) is the town's
    // terrain-pad height in region mode (0 in town mode); kind (idx 6) is the
    // render-kind; activity (idx 3) is the ActivityKind ordinal.
    curSnap = new Map(msg.entities.map(e => [e[0], [e[1], e[2], e[5], e[7] || 0, e[6] || 0, e[3] || 0]]));
    curTime = now;
  }

  // Interpolation factor 0..1 for render time `t` (seconds). 0 before the first snapshot.
  function alpha(t) {
    const interval = (curTime - prevTime) || SNAP_INTERVAL;
    return curTime ? Math.min(1, (t - curTime) / interval) : 0;
  }

  function send(obj) { if (ws && ws.readyState === 1) ws.send(JSON.stringify(obj)); }

  function connect() {
    onStatus && onStatus('agents: connecting…');
    ws = new WebSocket(`ws://${location.host}/ws`);
    ws.onopen = () => { onStatus && onStatus('agents: stream open, waiting…'); onOpen && onOpen(); };
    ws.onerror = () => onStatus && onStatus('agents: WebSocket error');
    ws.onmessage = (ev) => {
      const msg = JSON.parse(ev.data);
      if (msg.type === 'snap') { ingest(msg, performance.now() / 1000); onSnapshot && onSnapshot(msg); }
      else if (msg.type === 'detail') onDetail && onDetail(msg.detail);
      else if (msg.type === 'building') onBuilding && onBuilding(msg.building);
    };
    ws.onclose = () => { onStatus && onStatus('agents: stream closed, retrying…'); setTimeout(connect, 1000); };
  }

  return {
    connect, send, ingest, alpha,
    get prevSnap() { return prevSnap; },
    get curSnap() { return curSnap; },
    get prevTime() { return prevTime; },
    get curTime() { return curTime; },
  };
}
```

- [ ] **Step 4: Run the unit test to verify it passes**

Run: `cd /home/sakkivi/omat/daggerfall-unity/Headless/Sim.Web/parity && npm test`
Expected: PASS — all 4 tests pass, output pristine (no warnings).

- [ ] **Step 5: Commit**

```bash
cd /home/sakkivi/omat/daggerfall-unity
git add Headless/Sim.Web/wwwroot/net/client.js Headless/Sim.Web/parity/net-client.test.mjs Headless/Sim.Web/parity/package.json
git commit -m "$(cat <<'EOF'
feat(client): R1 — net/client.js module (transport + buffer + interp timing)

Mode-agnostic net layer for town3d: WebSocket connect/reconnect, Frame decode,
snapshot double-buffer, and alpha() interpolation timing. Browser globals only
inside connect() so it unit-tests in Node (node:test, zero new deps). Not yet
wired into town3d (next task). No OrbitControls/inspector/DOM references.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Integrate `town3d.html` → `net/`

Replace town3d's inline net code with the `net` instance; delete the moved code. Pure move — `npm run parity` must stay PASS.

**Files:**
- Modify: `Headless/Sim.Web/wwwroot/town3d.html`

**Interfaces:**
- Consumes: `createNet(...)` from Task 1 (`net.connect()`, `net.send(obj)`, `net.alpha(t)`, `net.curSnap`, `net.prevSnap`).
- Produces: a town3d that renders identically with all transport through `net/`.

- [ ] **Step 1: Import the module**

In `Headless/Sim.Web/wwwroot/town3d.html`, after the existing import line `import { OrbitControls } from 'three/addons/controls/OrbitControls.js';` (~line 85), add:
```js
import { createNet } from './net/client.js';
```

- [ ] **Step 2: Delete the moved buffer declarations**

Delete these three lines (currently ~639–641):
```js
let prevSnap = new Map(), curSnap = new Map();   // id -> [x,z,yaw]
let prevTime = 0, curTime = 0;
const SNAP_INTERVAL = 0.2;         // fallback gap; web pump publishes ~5 Hz
```
(Keep the `const DIR_SIGN = 1;` line and everything else — only those three move into `net/`.)

- [ ] **Step 3: Replace the onSnap / connectAgents / setSpeed block**

Replace the whole block from `function onSnap(msg) {` through the end of `function setSpeed(scale) { ... }` (currently ~lines 1052–1092, i.e. `onSnap`, `let agentWs = null;`, `connectAgents`, and `setSpeed`) with:
```js
// App-side reactions to a decoded snapshot (net owns the buffer + decode).
function onSnapshot(msg) {
  if (msg.utterances) pushUtterances(msg.utterances);
  world.hour = msg.hour; world.minute = msg.minute; world.night = msg.night;
  world.sun = msg.sun; world.weather = msg.weather;
  // Capture grabs the first published frame at-or-past CAP_TICK (timing-dependent tick, deterministic per machine).
  if (_capState === 'ff' && msg.tick >= CAP_TICK) {
    _capState = 'settle';
    setSpeed(0);
    camera.position.set(CAP_CAM[0], CAP_CAM[1], CAP_CAM[2]);
    controls.target.set(CAP_CAM[3], CAP_CAM[4], CAP_CAM[5]);
    controls.update();
  }
  stat(`live · ${net.curSnap.size} agents · ${String(msg.hour).padStart(2,'0')}:${String(msg.minute).padStart(2,'0')} · ${msg.weather}`);
}

const net = createNet({
  onSnapshot,
  onDetail: (d) => renderDetail(d),
  onBuilding: (b) => renderBuilding(b),
  onStatus: (s) => stat(s),
  onOpen: () => setSpeed(CAPTURE ? -1 : 10),
});

// Speed control wrapper: net transport + HUD button highlight (the latter → ui/ in R3).
function setSpeed(scale) {
  net.send({ type: 'speed', scale });
  document.querySelectorAll('#speed button').forEach(b =>
    b.style.color = Number(b.dataset.s) === scale ? '#9fd0ff' : '#8893a7');
}
```

- [ ] **Step 4: Rewire `updateAgents` to read from `net`**

In `function updateAgents(t)` (~line 942), replace the two interval/alpha lines:
```js
  const interval = (curTime - prevTime) || SNAP_INTERVAL;
  const alpha = curTime ? Math.min(1, (t - curTime) / interval) : 0;
```
with:
```js
  const alpha = net.alpha(t);
```
Then in the same function update the buffer reads: `for (const [id, c] of curSnap)` → `for (const [id, c] of net.curSnap)` (~line 948), and `const pr = prevSnap.get(id) || c;` → `const pr = net.prevSnap.get(id) || c;` (~line 969).

- [ ] **Step 5: Rewire the remaining `net` references and senders**

- ~line 543: `if (spritePools && curSnap.size) updateAgents(now);` → `if (spritePools && net.curSnap.size) updateAgents(now);`
- ~lines 262–263 (the inline view send):
  ```js
      if (agentWs && agentWs.readyState === 1)
        agentWs.send(JSON.stringify({ type: 'view', mx: cmx, my: cmy, r: R + 2 }));
  ```
  → `net.send({ type: 'view', mx: cmx, my: cmy, r: R + 2 });`
- Delete the `function send(o) { ... }` line (~799), and update its 4 callers to use `net.send`:
  - ~803: `if (selectedId >= 0) send({ type: 'watch', id: null });` → `net.send({ type: 'watch', id: null })`
  - ~816: `send({ type: 'watch', id });` → `net.send({ type: 'watch', id });`
  - ~817: `const ask = () => send({ type: 'inspect', id });` → `const ask = () => net.send({ type: 'inspect', id });`
  - ~827: `send({ type: 'pickBuilding', x, z });` → `net.send({ type: 'pickBuilding', x, z });`
- The init call `connectAgents();` (~line 1180) → `net.connect();`

- [ ] **Step 6: Verify — unit test still green and the town is pixel-identical**

Run:
```bash
cd /home/sakkivi/omat/daggerfall-unity/Headless/Sim.Web/parity
npm test          # net/client.js unit test still passes
npm run parity    # the carved town renders identically
```
Expected: `npm test` PASS; `npm run parity` `PASS: ... <= 0.1%`. If parity FAILs, the move changed behaviour — inspect `parity/diff.png`, fix, re-run (do not loosen the tolerance).

- [ ] **Step 7: Commit**

```bash
cd /home/sakkivi/omat/daggerfall-unity
git add Headless/Sim.Web/wwwroot/town3d.html
git commit -m "$(cat <<'EOF'
feat(client): R1 — route town3d transport through net/client.js

town3d now consumes net/: connect/reconnect, send, snapshot buffer, and
alpha() interp timing all come from net. Snapshot app-reactions (world clock,
utterances, capture tick, status) flow back via the onSnapshot callback; the
per-agent lerp stays in updateAgents (→ agents/ in R2). Pure move — npm run
parity PASS, net unit test green.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review

- **Spec coverage:** Module + factory interface → Task 1. `net/` owns transport/decode/buffer/alpha → Task 1 (`createNet`). Per-agent lerp stays in `updateAgents` reading `net.*` → Task 2 Step 4. `onSnapshot` callback carries world clock/utterances/capture/status → Task 2 Step 3. Browser globals only in `connect()` + Node unit test → Task 1. Parity gate + unit test → Task 2 Step 6. Guardrails (mode-agnostic, no transforms) → Global Constraints + module has no OrbitControls/DOM. ✓
- **Spec refinement noted:** the spec listed `net.setSpeed`; the plan keeps `setSpeed` as town3d's wrapper (it owns the HUD-button highlight) calling `net.send({type:'speed',scale})`, so `net`'s surface is just `send`. Functionally identical; smaller `net` surface.
- **Placeholder scan:** every code step has complete code; the exact decode mapping and clock are in Global Constraints; line numbers are "~" anchors with quoted source text to find. No TBD/TODO. ✓
- **Type/name consistency:** `createNet`, `connect`, `send`, `ingest`, `alpha`, `curSnap`, `prevSnap`, `curTime`, `prevTime` used identically across Task 1 (definition) and Task 2 (consumption). Callbacks `onSnapshot/onDetail/onBuilding/onStatus/onOpen` match between the module's destructure and town3d's `createNet({...})`. ✓
- **Known anchor risk:** Task 2 uses approximate line numbers; each edit quotes the exact current source line to locate it unambiguously. The implementer must confirm `renderDetail`/`renderBuilding`/`pushUtterances`/`world`/`stat` are in scope at the `createNet(...)` site (they are module-level; the callbacks defer their use to runtime). Verified empirically by Task 2 Step 6 (parity).
