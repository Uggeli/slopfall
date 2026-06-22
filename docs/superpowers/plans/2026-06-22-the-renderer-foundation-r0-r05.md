# The Renderer — Foundation (R0 + R0.5) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish the two foundations the rest of *The Renderer* milestone (R1–R5) depends on: a green build with the full test suite running (R0), and a proven, deterministic screenshot **parity gate** for the web client (R0.5).

**Architecture:** R0 is a pure deletion of the orphaned `Sim.Net` binary-TCP path that holds the only build break. R0.5 adds a tiny Node/Playwright harness that boots `Sim.Web`, loads `town3d.html` in a deterministic **capture mode** (fast-forward to a fixed tick, pause, fixed camera), screenshots via headless-Chromium SwiftShader software-WebGL, and diffs against a committed baseline. Capture mode is the one place we touch `town3d.html` outside a pure move; it stays in as the regression gate for R1–R4.

**Tech Stack:** .NET 10 (`dotnet build`/`test`), Node 22 + Playwright 1.61 (Chromium 1228, already cached at `~/.cache/ms-playwright`), `pngjs` + `pixelmatch` for image diffing, Three.js (existing, in `town3d.html`).

## Global Constraints

- **ARENA2 data path (this machine):** `DAGGERFALL_ARENA2=/home/sakkivi/omat/daggerfall-gamedata/arena2` — required env var for every `dotnet run --project Headless/Sim.Web` invocation.
- **Canonical scene:** region `"Daggerfall"`, location `"Gothway Garden"` (also the Program.cs default), `--starthour 12`. This is the milestone's reference town.
- **Determinism is structural, not seeded:** there is no `--seed`. Identical frames come from identical (region, location, starthour, tick). Capture at a fixed tick N.
- **Server bakes everything; client renders raw.** No client-side coordinate flips/rotations/transposes may be introduced (`docs/render_client_dataflow.md`). Capture mode only sets camera/speed/settle — never transforms geometry.
- **No new rendering features.** This plan adds test infrastructure only. No water/interiors/post-processing/seasons/animation; no "improve while refactoring."
- **Playwright browsers are already installed** — never run `playwright install` (it would re-download). Install npm packages with `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1`.
- **Headless WebGL needs SwiftShader flags:** launch Chromium with `--enable-unsafe-swiftshader --use-angle=swiftshader --use-gl=angle --ignore-gpu-blocklist --disable-gpu-sandbox`.

---

### Task 1: R0 — Green build by deletion

Delete the orphaned `Sim.Net` project (binary-TCP path) and its two dead tests; the build break lives entirely in `Sim.Net/Protocol.cs` (uses the removed `RenderSnapshot`). Nothing else references Sim.Net's types — `Sim.Web` mentions it only in comments and defines neither `WorldStatic` nor `RenderSnapshot`.

**Files:**
- Delete: `Headless/Sim.Net/` (whole project: `Protocol.cs`, `Sim.Net.csproj`, `bin/`, `obj/`)
- Delete: `Headless/Sim.Tests/SnapshotTests.cs`, `Headless/Sim.Tests/ProtocolTests.cs`
- Modify: `Headless/Sim.Tests/Sim.Tests.csproj` (remove line 20: `<ProjectReference Include="..\Sim.Net\Sim.Net.csproj" />`)
- Modify: `Headless/Sim.slnx` (remove `<Project Path="Sim.Net/Sim.Net.csproj" />`)

**Interfaces:**
- Consumes: nothing.
- Produces: a solution that builds green and a fully-running test suite. No code symbols.

- [ ] **Step 1: Confirm the red baseline**

Run: `cd /home/sakkivi/omat/daggerfall-unity && dotnet build Headless/Sim.slnx -clp:ErrorsOnly 2>&1 | grep -iE "error|Build FAILED"`
Expected: FAIL — `Sim.Net/Protocol.cs(114,52)` and `(142,23)`: `CS0246 ... 'RenderSnapshot' could not be found`, then `Build FAILED.`

- [ ] **Step 2: Delete Sim.Net and the two dead tests**

```bash
cd /home/sakkivi/omat/daggerfall-unity
git rm -r Headless/Sim.Net
git rm Headless/Sim.Tests/SnapshotTests.cs Headless/Sim.Tests/ProtocolTests.cs
```

- [ ] **Step 3: Remove the dangling references**

In `Headless/Sim.Tests/Sim.Tests.csproj`, delete this line (line 20):
```xml
    <ProjectReference Include="..\Sim.Net\Sim.Net.csproj" />
```

In `Headless/Sim.slnx`, delete this line:
```xml
  <Project Path="Sim.Net/Sim.Net.csproj" />
```

(Leave the `Sim.Net` comments in `Sim.Web/Sim.Web.csproj` and `Sim.SpatialTests/Sim.SpatialTests.csproj` as-is — they are historical notes, not references. `Sim.SpatialTests` not being in `Sim.slnx` is a pre-existing condition, out of scope here.)

- [ ] **Step 4: Verify the build is green**

Run: `cd /home/sakkivi/omat/daggerfall-unity && dotnet build Headless/Sim.slnx -clp:ErrorsOnly 2>&1 | tail -5`
Expected: `Build succeeded.` with `0 Error(s)`.

- [ ] **Step 5: Verify the full suite now runs**

Run: `cd /home/sakkivi/omat/daggerfall-unity && dotnet test Headless/Sim.slnx 2>&1 | grep -iE "Passed!|Failed!|Passed:|Failed:|total"`
Expected: the suite discovers and runs hundreds of tests across `Sim.Tests` + `Sim.MemoryTests` (previously only 1 of 218 ran). All tests pass. Record the actual passed/total count in the commit body.

- [ ] **Step 6: Commit**

```bash
cd /home/sakkivi/omat/daggerfall-unity
git add -A
git commit -m "$(cat <<'EOF'
feat(infra): R0 — delete orphaned Sim.Net, restore green build + full suite

The binary-TCP Sim.Net path held the only build break (Protocol.cs used the
removed RenderSnapshot). Delete the project, its two dead tests, the
Sim.Tests->Sim.Net reference, and the slnx entry. Solution builds green; the
full test suite runs again (was 1 of 218). TODOS line 15 cleared.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

Also remove the now-stale "Build is broken" item from `TODOS.md` (the bullet under `## Build & infra` referencing `Sim.Net/Protocol.cs` / `RenderSnapshot`) in this same commit.

---

### Task 2: R0.5a — WebGL feasibility spike (the gate that may abort R0.5)

Prove headless Chromium can render WebGL on this box **before** building anything on top of it. If this fails, stop R0.5, leave a note in `docs/Milestones/TheRenderer.md` that automated parity is infeasible here, and fall back to manual per-stage eyeballing for R1–R4.

**Files:**
- Create: `Headless/Sim.Web/parity/package.json`
- Create: `Headless/Sim.Web/parity/webgl-spike.html` (self-contained WebGL triangle, no network)
- Create: `Headless/Sim.Web/parity/spike.mjs`
- Modify: `.gitignore` (add `node_modules/`)

**Interfaces:**
- Consumes: nothing.
- Produces: confidence that `chromium.launch(SWIFTSHADER_ARGS)` renders WebGL. The `SWIFTSHADER_ARGS` array and the `isBlank(png)` helper are reused by Task 3/4 (copied into `capture.mjs`).

- [ ] **Step 1: Create the parity package manifest**

Create `Headless/Sim.Web/parity/package.json`:
```json
{
  "name": "town3d-parity",
  "private": true,
  "type": "module",
  "description": "Screenshot parity gate for the town3d web client (The Renderer milestone)",
  "scripts": {
    "spike": "node spike.mjs",
    "baseline": "node capture.mjs --out baseline/gothway.png",
    "parity": "node capture.mjs --out current.png && node compare.mjs baseline/gothway.png current.png"
  },
  "devDependencies": {
    "playwright": "1.61.0",
    "pngjs": "^7.0.0",
    "pixelmatch": "^6.0.0"
  }
}
```

- [ ] **Step 2: Ignore node_modules**

Append to `/home/sakkivi/omat/daggerfall-unity/.gitignore`:
```
node_modules/
Headless/Sim.Web/parity/current.png
Headless/Sim.Web/parity/diff.png
```

- [ ] **Step 3: Install deps without re-downloading browsers**

Run:
```bash
cd /home/sakkivi/omat/daggerfall-unity/Headless/Sim.Web/parity
PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1 npm install
```
Expected: installs `playwright`, `pngjs`, `pixelmatch` into `node_modules`; does NOT download a browser (the cached Chromium 1228 is reused).

- [ ] **Step 4: Create the minimal WebGL page**

Create `Headless/Sim.Web/parity/webgl-spike.html`:
```html
<!doctype html><html><head><meta charset="utf-8"></head>
<body style="margin:0">
<canvas id="c" width="320" height="240"></canvas>
<script>
const gl = document.getElementById('c').getContext('webgl');
if (!gl) { document.title = 'NO_WEBGL'; } else {
  gl.clearColor(0, 0, 0, 1); gl.clear(gl.COLOR_BUFFER_BIT);
  const vs = gl.createShader(gl.VERTEX_SHADER);
  gl.shaderSource(vs, 'attribute vec2 p; void main(){ gl_Position = vec4(p,0.0,1.0); }'); gl.compileShader(vs);
  const fs = gl.createShader(gl.FRAGMENT_SHADER);
  gl.shaderSource(fs, 'void main(){ gl_FragColor = vec4(1.0,0.3,0.1,1.0); }'); gl.compileShader(fs);
  const pr = gl.createProgram(); gl.attachShader(pr, vs); gl.attachShader(pr, fs); gl.linkProgram(pr); gl.useProgram(pr);
  const buf = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, buf);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-0.8,-0.8, 0.8,-0.8, 0.0,0.8]), gl.STATIC_DRAW);
  const loc = gl.getAttribLocation(pr, 'p'); gl.enableVertexAttribArray(loc);
  gl.vertexAttribPointer(loc, 2, gl.FLOAT, false, 0, 0);
  gl.drawArrays(gl.TRIANGLES, 0, 3);
  document.title = 'WEBGL_OK';
}
</script></body></html>
```

- [ ] **Step 5: Create the spike runner**

Create `Headless/Sim.Web/parity/spike.mjs`:
```js
import { chromium } from 'playwright';
import { PNG } from 'pngjs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { dirname, join } from 'node:path';

export const SWIFTSHADER_ARGS = [
  '--enable-unsafe-swiftshader', '--use-angle=swiftshader',
  '--use-gl=angle', '--ignore-gpu-blocklist', '--disable-gpu-sandbox',
];

// A render is "blank" if every pixel is identical (nothing was drawn).
export function isBlank(buf) {
  const png = PNG.sync.read(buf);
  const d = png.data;
  for (let i = 4; i < d.length; i += 4) {
    if (d[i] !== d[0] || d[i + 1] !== d[1] || d[i + 2] !== d[2]) return false;
  }
  return true;
}

const here = dirname(fileURLToPath(import.meta.url));
const browser = await chromium.launch({ headless: true, args: SWIFTSHADER_ARGS });
const page = await browser.newPage({ viewport: { width: 320, height: 240 } });
await page.goto(pathToFileURL(join(here, 'webgl-spike.html')).href);
const title = await page.title();
const shot = await page.locator('#c').screenshot();
await browser.close();

if (title !== 'WEBGL_OK') { console.error('FAIL: no WebGL context (title=' + title + ')'); process.exit(1); }
if (isBlank(shot)) { console.error('FAIL: WebGL context exists but rendered a blank canvas'); process.exit(1); }
console.log('PASS: headless WebGL renders (non-blank triangle).');
```

- [ ] **Step 6: Run the spike**

Run: `cd /home/sakkivi/omat/daggerfall-unity/Headless/Sim.Web/parity && npm run spike`
Expected: `PASS: headless WebGL renders (non-blank triangle).`
**If it FAILS:** do not proceed with Tasks 3–4. Edit `docs/Milestones/TheRenderer.md` R0.5 note to record that headless WebGL is infeasible on this machine and parity degrades to manual eyeballing; commit that note; stop here.

- [ ] **Step 7: Commit**

```bash
cd /home/sakkivi/omat/daggerfall-unity
git add Headless/Sim.Web/parity/package.json Headless/Sim.Web/parity/package-lock.json Headless/Sim.Web/parity/webgl-spike.html Headless/Sim.Web/parity/spike.mjs .gitignore
git commit -m "$(cat <<'EOF'
feat(parity): R0.5a — headless WebGL spike (SwiftShader) for the parity gate

Standalone WebGL triangle rendered via Playwright + cached Chromium 1228 with
SwiftShader software-GL flags; asserts a non-blank canvas. Proves the parity
harness is viable on this box before building it.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: R0.5b — town3d capture mode + capture harness

Add a deterministic `?capture=` mode to `town3d.html` and a Node harness that boots `Sim.Web`, drives capture mode, and writes a PNG of Gothway Garden. This is the only `town3d.html` change that is not a pure move; it is test infrastructure and stays in.

**Capture-mode contract (`?capture=1`):**
- Query params: `tick` (target tick to capture at; default `50`), `cam` (`"x,y,z,tx,ty,tz"`; default an overhead pose), `hour` is controlled server-side via `--starthour`.
- Behaviour: on socket open, fast-forward (`setSpeed(-1)`, max rate) instead of the normal `setSpeed(10)`. On the first frame whose `tick >= target`, call `setSpeed(0)` (pause), move `camera` + `controls.target` to the fixed pose, then count down `SETTLE_FRAMES` render frames (lets interpolation converge to the static paused frame). When the countdown hits zero, set `window.__captureReady = true`.
- The harness additionally waits for network idle, so async asset loads (models via the LoadingManager, textures, raw `fetch` tiles) finish before the screenshot.

**Files:**
- Modify: `Headless/Sim.Web/wwwroot/town3d.html` (add capture-mode block; touch `onopen` and the snapshot `onmessage` handler)
- Create: `Headless/Sim.Web/parity/capture.mjs`
- Create: `Headless/Sim.Web/parity/baseline/` (directory for the committed baseline PNG)

**Interfaces:**
- Consumes: `SWIFTSHADER_ARGS`, `isBlank` from `spike.mjs`.
- Produces: `window.__captureReady` boolean flag (the harness's readiness signal); `capture.mjs` writing a PNG to a `--out <path>` argument.

- [ ] **Step 1: Add the capture-mode header in town3d.html**

In `Headless/Sim.Web/wwwroot/town3d.html`, immediately after the camera + controls are created (search for `const controls = new OrbitControls(camera, renderer.domElement);`, ~line 99), insert:
```js
// --- Capture mode (test infra: deterministic screenshot for the parity gate) ---
const _capQ = new URLSearchParams(location.search);
const CAPTURE = _capQ.has('capture');
const CAP_TICK = Number(_capQ.get('tick') ?? 50);
const CAP_CAM = (_capQ.get('cam') ?? '600,900,600,600,0,600').split(',').map(Number);
let _capState = CAPTURE ? 'ff' : 'off';   // ff -> waiting for CAP_TICK; settle -> counting down; done
let _capSettle = 90;                       // render frames to let interpolation converge after pause
window.__captureReady = false;
```

- [ ] **Step 2: Fast-forward instead of normal speed on open (capture mode)**

Find the socket `onopen` handler (search for `agentWs.onopen = () =>` / `setSpeed(10);`, ~line 1057). Replace its `setSpeed(10)` call so capture mode fast-forwards:
```js
  agentWs.onopen = () => { stat('agents: stream open, waiting…'); setSpeed(CAPTURE ? -1 : 10); };
```

- [ ] **Step 3: Pause + frame the camera when the target tick arrives**

Find the snapshot handler where `world.hour` is assigned from the incoming message (search for `world.hour = msg.hour;`, ~line 1048). Immediately after that line, insert:
```js
  if (_capState === 'ff' && msg.tick >= CAP_TICK) {
    _capState = 'settle';
    setSpeed(0);
    camera.position.set(CAP_CAM[0], CAP_CAM[1], CAP_CAM[2]);
    controls.target.set(CAP_CAM[3], CAP_CAM[4], CAP_CAM[5]);
    controls.update();
  }
```
(`msg.tick` is the per-frame tick the server sends — confirm the property name in the same handler; the server sets `tick = snap.Tick` in `Program.cs`. If the client stores it under a different name in this handler, use that name.)

- [ ] **Step 4: Drive the settle countdown in the render loop**

Find the render loop `function animate()` (search for `function animate() {`, ~line 524). Inside it, just before `renderer.render(scene, camera);`, insert:
```js
  if (_capState === 'settle') {
    controls.update();
    if (--_capSettle <= 0) { _capState = 'done'; window.__captureReady = true; }
  }
```

- [ ] **Step 5: Create the capture harness**

Create `Headless/Sim.Web/parity/capture.mjs`:
```js
import { chromium } from 'playwright';
import { spawn } from 'node:child_process';
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import { SWIFTSHADER_ARGS, isBlank } from './spike.mjs';

const ARENA2 = '/home/sakkivi/omat/daggerfall-gamedata/arena2';
const PORT = 8137;
const REGION = 'Daggerfall', LOCATION = 'Gothway Garden';
const STARTHOUR = 12, CAP_TICK = 50;
const CAM = '600,900,600,600,0,600';
const VIEWPORT = { width: 1280, height: 720 };

const outIdx = process.argv.indexOf('--out');
const outPath = outIdx >= 0 ? process.argv[outIdx + 1] : 'current.png';

// 1. Boot Sim.Web; resolve when it prints its ready line.
const server = spawn('dotnet',
  ['run', '--project', 'Headless/Sim.Web', '--', REGION, LOCATION,
   '--port', String(PORT), '--starthour', String(STARTHOUR)],
  { cwd: '/home/sakkivi/omat/daggerfall-unity', env: { ...process.env, DAGGERFALL_ARENA2: ARENA2 } });

const ready = new Promise((resolve, reject) => {
  const timer = setTimeout(() => reject(new Error('server did not become ready in 180s')), 180_000);
  const onData = (b) => {
    const s = b.toString(); process.stdout.write(s);
    if (s.includes(`http://localhost:${PORT}`)) { clearTimeout(timer); resolve(); }
  };
  server.stdout.on('data', onData);
  server.stderr.on('data', (b) => process.stderr.write(b.toString()));
  server.on('exit', (c) => reject(new Error('server exited early, code ' + c)));
});

function shutdown() { try { server.kill('SIGINT'); } catch {} }
process.on('exit', shutdown);

try {
  await ready;
  const browser = await chromium.launch({ headless: true, args: SWIFTSHADER_ARGS });
  const page = await browser.newPage({ viewport: VIEWPORT });
  const url = `http://localhost:${PORT}/town3d.html?capture=1&tick=${CAP_TICK}&cam=${CAM}`;
  await page.goto(url, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__captureReady === true, null, { timeout: 120_000 });
  await page.waitForLoadState('networkidle');
  const shot = await page.screenshot();
  await browser.close();
  if (isBlank(shot)) throw new Error('captured a blank frame (no scene rendered)');
  mkdirSync(dirname(outPath), { recursive: true });
  writeFileSync(outPath, shot);
  console.log('captured', outPath, `(${shot.length} bytes)`);
} finally {
  shutdown();
}
```

- [ ] **Step 6: Capture a frame and eyeball it once**

Run: `cd /home/sakkivi/omat/daggerfall-unity/Headless/Sim.Web/parity && node capture.mjs --out /tmp/cap-check.png`
Expected: server boots, `captured /tmp/cap-check.png (... bytes)`, no "blank frame" error.
Open `/tmp/cap-check.png` (or `Read` it) and confirm it shows Gothway Garden — terrain, buildings, NPC billboards, daylit sky — not an empty horizon. This is the one manual confirmation that capture mode frames a real scene.

- [ ] **Step 7: Commit**

```bash
cd /home/sakkivi/omat/daggerfall-unity
git add Headless/Sim.Web/wwwroot/town3d.html Headless/Sim.Web/parity/capture.mjs
git commit -m "$(cat <<'EOF'
feat(parity): R0.5b — town3d capture mode + Sim.Web capture harness

?capture=1 fast-forwards to a fixed tick, pauses, frames a fixed camera, and
sets window.__captureReady after interpolation settles. capture.mjs boots
Sim.Web (Gothway Garden, --starthour 12), waits for ready, screenshots via
headless SwiftShader Chromium. The one non-pure-move town3d touch; stays in
as the parity gate.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: R0.5c — baseline + `npm run parity` diff gate (proven to bite)

Capture the committed baseline from the known-good current `town3d.html`, add the diff command, and prove the gate actually catches a visual change.

**Files:**
- Create: `Headless/Sim.Web/parity/compare.mjs`
- Create: `Headless/Sim.Web/parity/baseline/gothway.png` (committed baseline image)
- Modify: `docs/Milestones/TheRenderer.md` (mark R0.5 done; record the exact `npm run parity` command R1–R4 must re-run)

**Interfaces:**
- Consumes: `capture.mjs` (`npm run baseline`, `npm run parity` from Task 2's package.json).
- Produces: `compare.mjs <baseline> <current>` exiting non-zero when mismatch exceeds tolerance; the `npm run parity` command that R1–R4 use as their per-stage test step.

- [ ] **Step 1: Create the comparator**

Create `Headless/Sim.Web/parity/compare.mjs`:
```js
import { readFileSync, writeFileSync } from 'node:fs';
import { PNG } from 'pngjs';
import pixelmatch from 'pixelmatch';

const [, , basePath, curPath] = process.argv;
const TOLERANCE = 0.001;   // allow up to 0.1% of pixels to differ (sub-pixel AA noise)

const base = PNG.sync.read(readFileSync(basePath));
const cur = PNG.sync.read(readFileSync(curPath));
if (base.width !== cur.width || base.height !== cur.height) {
  console.error(`FAIL: size mismatch ${base.width}x${base.height} vs ${cur.width}x${cur.height}`);
  process.exit(1);
}
const diff = new PNG({ width: base.width, height: base.height });
const bad = pixelmatch(base.data, cur.data, diff.data, base.width, base.height, { threshold: 0.1 });
const total = base.width * base.height;
const ratio = bad / total;
writeFileSync('diff.png', PNG.sync.write(diff));
if (ratio > TOLERANCE) {
  console.error(`FAIL: ${bad}/${total} px differ (${(ratio * 100).toFixed(3)}% > ${(TOLERANCE * 100).toFixed(1)}%). See diff.png`);
  process.exit(1);
}
console.log(`PASS: ${bad}/${total} px differ (${(ratio * 100).toFixed(3)}% <= ${(TOLERANCE * 100).toFixed(1)}%)`);
```

- [ ] **Step 2: Verify capture is reproducible run-to-run**

Run: `cd /home/sakkivi/omat/daggerfall-unity/Headless/Sim.Web/parity && node capture.mjs --out a.png && node capture.mjs --out b.png && node compare.mjs a.png b.png`
Expected: `PASS: ... <= 0.1%`. If it FAILS, capture is non-deterministic — increase `_capSettle` in town3d.html (Task 3 Step 1) and/or raise `CAP_TICK`, and re-test before continuing. Clean up: `rm a.png b.png diff.png`.

- [ ] **Step 3: Write the committed baseline**

Run: `cd /home/sakkivi/omat/daggerfall-unity/Headless/Sim.Web/parity && npm run baseline`
Expected: writes `baseline/gothway.png`.

- [ ] **Step 4: Confirm parity is green against the baseline**

Run: `cd /home/sakkivi/omat/daggerfall-unity/Headless/Sim.Web/parity && npm run parity`
Expected: `PASS: ... <= 0.1%`.

- [ ] **Step 5: Prove the gate bites (and revert)**

Temporarily change a visible constant in `town3d.html` — e.g. the sky/clear color or fog — then confirm parity FAILS, then revert:
```bash
cd /home/sakkivi/omat/daggerfall-unity
# Make a visible change (pick the scene background / fog color line):
sed -i 's/scene.background = /scene.background = \/*PARITYTEST*\/ /' Headless/Sim.Web/wwwroot/town3d.html  # placeholder marker only; instead edit the actual color value
```
Better: open `town3d.html`, find where `scene.background` (or fog color) is set, change the color value, save. Then:
Run: `cd /home/sakkivi/omat/daggerfall-unity/Headless/Sim.Web/parity && npm run parity`
Expected: `FAIL: ... px differ (... > 0.1%). See diff.png` — the gate detects the visual change.
Then revert the change: `cd /home/sakkivi/omat/daggerfall-unity && git checkout Headless/Sim.Web/wwwroot/town3d.html` and re-run `npm run parity` → expect `PASS`. (This step writes no commit; it only proves the gate works.)

- [ ] **Step 6: Record the gate in the milestone doc**

In `docs/Milestones/TheRenderer.md`, update the R0.5 stage: mark it done and state the exact command R1–R4 must re-run after each carve:
```
Per-stage parity check (run from Headless/Sim.Web/parity): `npm run parity`
(boots Sim.Web at Gothway Garden + captures tick 50 + diffs vs baseline/gothway.png).
A pure move must report PASS (<=0.1% pixels differ).
```

- [ ] **Step 7: Commit**

```bash
cd /home/sakkivi/omat/daggerfall-unity
git add Headless/Sim.Web/parity/compare.mjs Headless/Sim.Web/parity/baseline/gothway.png docs/Milestones/TheRenderer.md
git commit -m "$(cat <<'EOF'
feat(parity): R0.5c — baseline + npm run parity diff gate

pixelmatch comparator (0.1% tolerance) + committed Gothway Garden baseline.
Verified reproducible run-to-run and proven to fail on a deliberate color
change. `npm run parity` is now the mechanical per-stage gate for R1-R4.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## What comes after this plan

R1–R5 are **not** in this plan by design: each carve's test step is `npm run parity` (built here), and the exact module seams are best read at the line level when carving, not pre-committed now. After R0.5 is green, write the R1 plan (`Carve net/`) as the next brainstorming→writing-plans cycle. If the Task 2 spike failed (no headless WebGL), R1–R4 instead use manual per-stage eyeballing and the parity harness is dropped.

## Self-Review

- **Spec coverage:** R0 (milestone stage R0) → Task 1. R0.5 spike → Task 2. R0.5 capture mode + harness → Task 3. R0.5 baseline + gate → Task 4. The R0.5 spec's "de-risk first," "capture mode is the one non-pure-move touch," "baseline from known-good current town3d," and "re-run after each of R1–R4" are all covered. R1–R5 are explicitly deferred to their own plans (matches the milestone's stage-by-stage structure). ✓
- **Placeholder scan:** Task 4 Step 5's first `sed` is explicitly labelled a non-working marker with the real instruction ("Better: open ... change the color value") following it — the engineer edits the actual color line. No other TBD/TODO/"handle edge cases" placeholders. Every code step has complete code. ✓
- **Type/name consistency:** `SWIFTSHADER_ARGS` and `isBlank` are defined in `spike.mjs` (Task 2) and imported by `capture.mjs` (Task 3). `window.__captureReady` is set in town3d (Task 3) and awaited in `capture.mjs` (Task 3). `--out` arg produced by `capture.mjs` and used by package.json scripts (Task 2). `baseline/gothway.png` path consistent across Tasks 2/4. `_capState`/`_capSettle`/`CAP_TICK`/`CAP_CAM` consistent within town3d edits. ✓
- **Known soft spot:** capture mode reads the per-frame tick as `msg.tick`; Task 3 Step 3 flags confirming the exact client-side property name in that handler (server sends `tick`). Determinism is verified empirically in Task 4 Step 2 before the baseline is committed, with a concrete remedy (raise `_capSettle`/`CAP_TICK`) if it's flaky.
