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
  let resolved = false;
  const timer = setTimeout(() => reject(new Error('server did not become ready in 180s')), 180_000);
  const onData = (b) => {
    const s = b.toString(); process.stdout.write(s);
    if (s.includes(`http://localhost:${PORT}`)) { clearTimeout(timer); resolved = true; resolve(); }
  };
  server.stdout.on('data', onData);
  server.stderr.on('data', (b) => process.stderr.write(b.toString()));
  server.on('exit', (c) => { if (!resolved) reject(new Error('server exited early, code ' + c)); });
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
