// One-off verification for the agent-size bug: non-idle agents rendered smaller than idle.
// Loads town3d against an already-running Sim.Web, waits for agents, then reads the live
// `agents` THREE.Group (same cached module the page imports) and samples each billboard's
// per-record world size (mesh.scale). Fix-correct => heights cluster tightly (~2.0 m for
// civilians) regardless of activity, with width variance proving mixed records are shown.
import { chromium } from 'playwright';
import { SWIFTSHADER_ARGS } from './spike.mjs';

const PORT = process.argv[2] || '8137';
const browser = await chromium.launch({ headless: true, args: SWIFTSHADER_ARGS });
const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });
const errors = [];
page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
page.on('pageerror', e => errors.push(String(e)));

await page.goto(`http://localhost:${PORT}/town3d.html`, { waitUntil: 'load' });
await page.waitForFunction(() => {
  const m = (document.getElementById('stat')?.textContent || '').match(/live · (\d+) agents/);
  return m && Number(m[1]) > 0;
}, null, { timeout: 60_000 });

// Sample a few times over a couple seconds so agents change activity (walk<->idle).
const samples = [];
for (let i = 0; i < 5; i++) {
  await page.waitForTimeout(500);
  const s = await page.evaluate(async () => {
    const mod = await import('./engine/agents.js');
    return mod.agents.children
      .filter(m => m.geometry && m.geometry.type === 'PlaneGeometry')   // sprite billboards, not door models
      .map(m => {
        const src = m.material?.map?.image?.currentSrc || m.material?.map?.image?.src || '';
        const am = src.match(/spritesheet\/(\d+)/);
        return { archive: am ? +am[1] : -1, w: +m.scale.x.toFixed(3), h: +m.scale.y.toFixed(3) };
      });
  });
  samples.push(...s);
}
await browser.close();

if (!samples.length) { console.error('FAIL: no agent billboards found'); process.exit(1); }
const min = a => Math.min(...a), max = a => Math.max(...a), avg = a => a.reduce((x, y) => x + y, 0) / a.length;
// The bug specifically affected CIVILIANS (archives 381-456): walk records rendered ~1.36 m
// vs idle ~2.0 m. Monsters (255-274) have genuinely short walk frames (a rat is ~0.7 m) — not
// a bug. So group by archive class and check civilian heights are uniform regardless of record.
const isCiv = a => a >= 381 && a <= 456;
const civ = samples.filter(s => isCiv(s.archive));
const nonciv = samples.filter(s => !isCiv(s.archive));
const civH = civ.map(s => s.h), civW = civ.map(s => s.w);

console.log(`samples: ${samples.length} billboard observations  (civilian=${civ.length}, non-civilian=${nonciv.length})`);
console.log(`CIVILIAN height m: min=${min(civH).toFixed(3)} max=${max(civH).toFixed(3)} avg=${avg(civH).toFixed(3)}`);
console.log(`CIVILIAN width  m: min=${min(civW).toFixed(3)} max=${max(civW).toFixed(3)}  (width varies by record => walk+idle both present)`);
// Per-archive height spread: a civilian shown in ANY record must stay ~full height.
const byArch = {};
for (const s of civ) (byArch[s.archive] ||= []).push(s.h);
for (const [a, hh] of Object.entries(byArch))
  console.log(`  archive ${a}: n=${hh.length} height ${min(hh).toFixed(3)}..${max(hh).toFixed(3)} m`);
if (nonciv.length) {
  const ncH = nonciv.map(s => s.h);
  console.log(`non-civilian height m: min=${min(ncH).toFixed(3)} max=${max(ncH).toFixed(3)} (monsters/guards — sizes legitimately vary by species/record)`);
}
console.log(`console errors: ${errors.length}`);
if (errors.length) console.log('  ' + errors.slice(0, 5).join('\n  '));

// Old bug: civilian walk rendered ~1.36 m. Pass if NO civilian falls below 1.7 m AND
// civilian widths vary (proving walk + idle records are both being displayed), no errors.
const civShrunk = civH.filter(h => h < 1.7).length;
const civWidthVaries = civ.length && (max(civW) - min(civW)) > 0.1;
if (civ.length >= 5 && civShrunk === 0 && civWidthVaries && errors.length === 0) {
  console.log('\nPASS — every civilian renders ~full height in all records; widths vary (walk+idle shown); no errors.');
} else {
  console.log('\nFAIL — ' + [
    civ.length < 5 ? 'too few civilians sampled' : null,
    civShrunk > 0 ? `${civShrunk} shrunken civilian billboards (bug still present)` : null,
    !civWidthVaries ? 'no civilian width variance (inconclusive — maybe only idle seen)' : null,
    errors.length ? 'console errors' : null,
  ].filter(Boolean).join('; '));
  process.exit(1);
}
