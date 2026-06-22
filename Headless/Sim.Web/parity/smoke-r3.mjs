// smoke-r3.mjs — interaction smoke for The Renderer R3 (ui/ carve). The parity capture
// exercises hud (setSpeed/clock/sliders/toggles run during boot) but NOT the inspector
// click path. This boots Sim.Web, loads town3d normally, waits for agents, clicks until one
// is picked, and asserts the inspector panel opens — plus zero console errors. Throws on
// failure (non-zero exit). Not a committed gate; a one-off R3 validation.
import { chromium } from 'playwright';
import { spawn } from 'node:child_process';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { SWIFTSHADER_ARGS } from './spike.mjs';

const ARENA2 = process.env.DAGGERFALL_ARENA2 || '/home/uggeli/df-data/arena2';
const _here = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = join(_here, '..', '..', '..');
const PORT = 8138;
const REGION = 'Daggerfall', LOCATION = 'Gothway Garden';

const server = spawn('dotnet',
  ['run', '--project', 'Headless/Sim.Web', '--', '--town', REGION, LOCATION,
   '--port', String(PORT), '--starthour', '12'],
  { cwd: REPO_ROOT, env: { ...process.env, DAGGERFALL_ARENA2: ARENA2 } });
const ready = new Promise((resolve, reject) => {
  const timer = setTimeout(() => reject(new Error('server not ready in 180s')), 180_000);
  server.stdout.on('data', b => { if (b.toString().includes(`http://localhost:${PORT}`)) { clearTimeout(timer); resolve(); } });
  server.on('exit', c => reject(new Error('server exited early, code ' + c)));
});
function shutdown() { try { server.kill('SIGINT'); } catch {} }
process.on('exit', shutdown);

try {
  await ready;
  const browser = await chromium.launch({ headless: true, args: SWIFTSHADER_ARGS });
  const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
  const errors = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('pageerror', e => errors.push(String(e)));

  await page.goto(`http://localhost:${PORT}/town3d.html`, { waitUntil: 'load' });
  // Wait until agents are streaming — the HUD stat line reads "live · N agents" (N>0).
  await page.waitForFunction(() => {
    const m = (document.getElementById('stat')?.textContent || '').match(/live · (\d+) agents/);
    return m && Number(m[1]) > 0;
  }, null, { timeout: 60_000 }).catch(() => { throw new Error('no agents streamed in 60s'); });

  // Click around the centre until the inspector panel opens (agents wander; a few tries).
  const box = { x: 1280 / 2, y: 720 / 2 };
  let opened = false;
  for (let i = 0; i < 40 && !opened; i++) {
    await page.mouse.click(box.x + (i % 8) * 12 - 48, box.y + ((i >> 3) % 6) * 12 - 36);
    await page.waitForTimeout(120);
    opened = await page.evaluate(() => document.getElementById('detail').style.display === 'block');
  }
  const detailHtml = await page.evaluate(() => document.getElementById('detail').innerHTML);
  await browser.close();

  if (errors.length) throw new Error('console errors:\n  ' + errors.join('\n  '));
  if (!opened) throw new Error('inspector panel never opened after 40 clicks (agent picking broken?)');
  console.log('SMOKE PASS — inspector opened, no console errors. detail:', detailHtml.slice(0, 80).replace(/\n/g, ' '));
} finally {
  shutdown();
}
