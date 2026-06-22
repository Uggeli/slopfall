import { chromium } from 'playwright';
import { PNG } from 'pngjs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { dirname, join } from 'node:path';

export const SWIFTSHADER_ARGS = [
  '--enable-unsafe-swiftshader', '--use-angle=swiftshader',
  '--use-gl=angle', '--ignore-gpu-blocklist', '--disable-gpu-sandbox',
];

// A render is "blank" if every pixel has identical RGB values (alpha intentionally excluded).
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
