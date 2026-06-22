// ui/hud.js — the heads-up display: gfx sliders, debug toggles, speed buttons, the
// clock/weather readout, and the status line (The Renderer, R3). Mounted by the mode with
// the camera it owns + the net it owns. Writes engine `gfx`/applies to renderer+camera, and
// reflects engine `world` in the clock. It does not create the camera or OrbitControls.
import { renderer, grid, world, gfx } from '../engine/scene.js';
import { town } from '../engine/world.js';
import { terrainGroup } from '../engine/terrain.js';

const ui = id => document.getElementById(id);
export const stat = s => ui('stat').textContent = s;

export function mountHud({ net, camera }) {
  // ---- Graphics sliders: fog distance/toggle, brightness, FOV, render scale ----
  // These feed live into the engine gfx/camera/renderer state. gfx.fogFar and brightness
  // are read each frame by updateAtmosphere(); FOV and resolution apply on change.
  function syncGfx() {
    gfx.fogFar = +ui('fog').value;
    gfx.bright = +ui('bright').value / 100;
    gfx.fogOn = ui('fogOn').checked;
    camera.fov = +ui('fov').value; camera.updateProjectionMatrix();
    renderer.setPixelRatio(devicePixelRatio * (+ui('res').value / 100));
    renderer.setSize(innerWidth, innerHeight);
    ui('fogVal').textContent = gfx.fogFar >= 1000 ? (gfx.fogFar / 1000).toFixed(1) + 'km' : gfx.fogFar + 'm';
    ui('brightVal').textContent = Math.round(gfx.bright * 100) + '%';
    ui('fovVal').textContent = camera.fov + '°';
    ui('resVal').textContent = ui('res').value + '%';
  }
  // Reflect the active fogFar in the slider (init picks town 1600 vs region 3200).
  function setFogSlider() { ui('fog').value = gfx.fogFar; syncGfx(); }
  for (const id of ['fog', 'bright', 'fov', 'res', 'fogOn']) ui(id).addEventListener('input', syncGfx);

  // ---- Debug view toggles: mirror X/Z, wireframe, grid, terrain visibility ----
  function applyToggles() {
    town.scale.set(ui('mirx').checked ? -1 : 1, 1, ui('mirz').checked ? -1 : 1);
    town.traverse(o => {
      if (!o.isMesh) return;
      const mats = Array.isArray(o.material) ? o.material : [o.material];
      for (const m of mats) { m.wireframe = ui('wire').checked; m.needsUpdate = true; }
    });
    grid.visible = ui('grid').checked;
  }
  function positionTerrain() { terrainGroup.visible = ui('terr').checked; }
  for (const id of ['mirx', 'mirz', 'wire', 'grid']) ui(id).addEventListener('change', applyToggles);
  ui('terr').addEventListener('change', positionTerrain);

  // ---- Speed control = engine tick rate (server handles {type:"speed",scale}). scale is
  // ticks/real-second: 0 pauses, 10 is the normal rate, -1 (MAX) runs flat out. ----
  function setSpeed(scale) {
    net.send({ type: 'speed', scale });
    document.querySelectorAll('#speed button').forEach(b =>
      b.style.color = Number(b.dataset.s) === scale ? '#9fd0ff' : '#8893a7');
  }
  document.querySelectorAll('#speed button').forEach(btn => {
    btn.addEventListener('click', () => setSpeed(Number(btn.dataset.s)));
  });

  // Per-frame clock/weather readout, reflecting engine `world` (engine writes no DOM).
  function updateClock() {
    ui('clock').textContent = `${String(world.hour).padStart(2, '0')}:${String(world.minute).padStart(2, '0')} · ${world.weather}`;
  }

  return { setSpeed, setFogSlider, positionTerrain, applyToggles, syncGfx, updateClock, stat };
}
