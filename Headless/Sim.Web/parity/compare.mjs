import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { PNG } from 'pngjs';
import pixelmatch from 'pixelmatch';

const [, , basePath, curPath] = process.argv;
const TOLERANCE = 0.001;   // allow up to 0.1% of pixels to differ (sub-pixel AA noise)

if (!basePath || !curPath) { console.error('usage: compare.mjs <baseline> <current>'); process.exit(2); }

const here = dirname(fileURLToPath(import.meta.url));

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
writeFileSync(join(here, 'diff.png'), PNG.sync.write(diff));
if (ratio > TOLERANCE) {
  console.error(`FAIL: ${bad}/${total} px differ (${(ratio * 100).toFixed(3)}% > ${(TOLERANCE * 100).toFixed(1)}%). See ${join(here, 'diff.png')}`);
  process.exit(1);
}
console.log(`PASS: ${bad}/${total} px differ (${(ratio * 100).toFixed(3)}% <= ${(TOLERANCE * 100).toFixed(1)}%)`);
