# The Renderer — R3: carve `ui/` (design)

Part of the milestone in `docs/Milestones/TheRenderer.md` (stage R3). Gated by the R0.5
parity harness (local baseline; see R2 design). Builds on R2 (`engine/` carved).

## Goal

Move the inspector panels and the HUD/speed/gfx widgets out of the `town3d.html` shell into
`wwwroot/ui/`, **mounted by the mode, fed by `net/`**, with zero visual change (parity PASS).
After R3 the shell holds only camera/`OrbitControls`, the fly-cam + framing, the capture-mode
state, `onSnapshot`, the `net` instance, the `animate()` loop, and `init()` — i.e. the future
`modes/observer` + `main` (split in R4). All DOM-widget logic lives in `ui/`.

**R3 done = inspect + HUD work unchanged (Gothway parity PASS) and no DOM-widget logic
(`renderDetail`, sliders, speed buttons, the comms feed) remains in the shell.**

Note: `page.screenshot()` captures the viewport *including* the HUD overlay (clock/title/stat
chrome), so a pure move must keep the rendered DOM byte-identical — same `innerHTML`, same
listeners, same text. The inspector `#detail` panel is hidden by default (no selection in
capture mode), so it doesn't affect the screenshot, but it's still a pure move.

## Modules

```
ui/
  comms.js      utterance feed: atomNames fetch, CHANNEL_GLYPH, atomName/utterText, pushUtterances.
                No deps → plain module, exports pushUtterances; the /asset/atoms fetch runs on import.
  inspector.js  mountInspector({net, camera}) → { renderDetail, renderBuilding, deselect, get selectedId }.
                Owns picking (raycaster/ptr + pointer/Escape handlers), selection state, and all
                render*/wire*/needBar panels. Imports agents+town (engine) for raycasting, clearSelRing.
  hud.js        mountHud({net, camera}) → { setSpeed, setFogSlider, positionTerrain, applyToggles,
                syncGfx, updateClock, stat }. Owns the gfx sliders, the debug toggles, the speed
                buttons, the clock/weather readout, and the status line. Imports gfx/world/grid/town/
                terrainGroup/renderer (engine).
```

## The mount seam (resolves the net↔ui cycle)

`net`'s callbacks need ui (`onDetail→renderDetail`, `onOpen→setSpeed`, `onStatus→stat`); ui needs
`net` (`send`). Order in the shell:

```js
const net = createNet({
  onSnapshot,                                   // shell fn: pushUtterances + world write + capture + stat
  onDetail:   d => inspector.renderDetail(d),
  onBuilding: b => inspector.renderBuilding(b),
  onStatus:   s => hud.stat(s),
  onOpen:     () => hud.setSpeed(CAPTURE ? -1 : 10),
});
const hud = mountHud({ net, camera });
const inspector = mountInspector({ net, camera });
```

The callbacks are arrows invoked only after `net.connect()` (called last, in `init()`), by which
time `hud`/`inspector` are assigned — no TDZ hazard. `mount*` wires its own DOM listeners and
returns its API. The mode (shell, → `modes/observer` in R4) is what calls `mount*` and passes
the camera it owns + the net it owns.

## What moves, by module

- **comms.js:** `atomNames` + the `/asset/atoms` fetch, `CHANNEL_GLYPH`, `commLines`/`COMM_MAX`,
  `atomName`, `utterText`, `pushUtterances`. Shell `onSnapshot` calls `pushUtterances` (imported).
- **inspector.js:** `raycaster`, `ptr`, `selectedId`/`selectedBuildingI`/`inspectTimer`/`downX/Y`,
  `agentTab`/`lastAgentDetail`/`oddCollapsed`, `NEED_LABELS`, the pointerdown/up + Escape handlers,
  `clearSelection`, `deselect`, `selectAgent`, `pickBuilding`, `renderDetail`, `renderAgentPanel`,
  `renderOdd`, `renderBuilding`, `wireAgent`, `wireBuilding`, `needBar`. `selectedId` is exposed via
  a getter so `animate` can pass it to `updateAgents`.
- **hud.js:** `syncGfx`, `setFogSlider`, the slider `input` listeners; `applyToggles`,
  `positionTerrain`, the toggle `change` listeners; `setSpeed` + the speed-button `click` listeners;
  `updateClock()` (the per-frame `ui('clock')` write, reading engine `world`); `stat` + the `ui`
  helper. The `resize` listener touches camera+renderer — it's mode/main, **stays in the shell**.

## Shell rewiring (call sites)

- `animate`: `ui('clock')…` → `hud.updateClock()`; `updateAgents(now, camera, net, selectedId)` →
  `… inspector.selectedId)`.
- `onSnapshot`: `pushUtterances` (import), `setSpeed(0)` → `hud.setSpeed(0)`, `stat(…)` → `hud.stat(…)`.
- `init`: every `stat(…)` → `hud.stat(…)`; `setFogSlider()` → `hud.setFogSlider()`;
  `positionTerrain()` → `hud.positionTerrain()`; `applyToggles()` → `hud.applyToggles()`.
- Remove the shell's `ui`/`stat` consts (now in hud) and the moved listeners/handlers.

## Testing

- **Acceptance (each sub-step):** `npm run parity` vs `baseline/local-r2-base.png` → PASS (≤0.1%).
  Carve in order **comms → inspector → hud**, parity after each.
- **Boundary check:** `grep -rnE "renderDetail|getElementById|#speed|querySelector" town3d.html`
  returns nothing (no DOM-widget logic left in the shell); `grep -r OrbitControls ui/` returns
  nothing (ui doesn't drive the camera — it's handed one).
- Manual smoke (optional): click an agent → panel; sliders/toggles/speed buttons respond.

## Guardrails

- **Pure move, no behaviour change.** Same DOM, same listeners, same panels. Parity enforces the
  canvas+chrome; the hidden inspector is a move-only.
- **No client-side coordinate transforms** (server bakes everything).
- **ui is fed, not authoritative:** `ui/` receives `net` + `camera`; it must not create the camera
  or `OrbitControls`. Picking *reads* the camera it's handed.
- **No new features** — R3 is a move only (no new panels, no restyle).
