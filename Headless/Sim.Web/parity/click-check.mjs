// click-check.mjs — connect to an ALREADY-RUNNING server (no spawn), wait for agents,
// click until the inspector opens, report panel + console errors. Reliable validation that
// the observer-mounted inspector still picks. Usage: node click-check.mjs <port>
import { chromium } from 'playwright';
import { SWIFTSHADER_ARGS } from './spike.mjs';
const PORT = process.argv[2] || '8090';
const browser = await chromium.launch({ headless: true, args: SWIFTSHADER_ARGS });
const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
const errors = [];
page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
page.on('pageerror', e => errors.push(String(e)));
await page.goto(`http://localhost:${PORT}/town3d.html`, { waitUntil: 'load' });
await page.waitForFunction(() => {
  const m = (document.getElementById('stat')?.textContent || '').match(/live · (\d+) agents/);
  return m && Number(m[1]) > 0;
}, null, { timeout: 60_000 }).catch(() => { throw new Error('no agents in 60s'); });
let opened = false;
for (let i = 0; i < 50 && !opened; i++) {
  await page.mouse.click(640 + (i % 10) * 11 - 55, 360 + ((i / 10) | 0) * 11 - 33);
  await page.waitForTimeout(110);
  opened = await page.evaluate(() => document.getElementById('detail').style.display === 'block');
}
const detail = await page.evaluate(() => document.getElementById('detail').innerHTML.slice(0, 90).replace(/\n/g, ' '));
await browser.close();
if (errors.length) { console.log('FAIL — console errors:\n  ' + errors.join('\n  ')); process.exit(1); }
if (!opened) { console.log('FAIL — inspector never opened'); process.exit(1); }
console.log('CLICK PASS — inspector opened:', detail);
