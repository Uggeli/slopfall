// engine/world.js — buildings + nature/decorative flats (The Renderer, R2).
// Owns the `town` group (all building instances), the region structure-tile cache and its
// streamer-fed builder, the flat-billboard atlas cache + instancing, and the shared
// sprite-cell UV helper. Mode-agnostic: no camera, OrbitControls, inspector, or HUD DOM.
import * as THREE from 'three';
import { scene } from './scene.js';
import { texLoader, loadModel } from './assets.js';

export const town = new THREE.Group();    // all building instances
scene.add(town);

// Region mode streams building/flat geometry per map pixel on the same ring as terrain.
// regionStructures caches a THREE.Group per pixel ('loading' | 'empty' | Group).
export const regionStructures = new Map();   // "mx,my" -> THREE.Group | 'loading' | 'empty'
let townClimate = 2, townSeason = 0;  // region texture climate/season (from /asset/town)
export function setTownClimateSeason(climate, season) { townClimate = climate; townSeason = season; }

// Town gates: paired open/closed door models (446/447) the engine toggles by day/night.
// init() collects the pairs while placing buildings; the render loop calls updateGates.
let gatePairs = null;
export function setGatePairs(g) { gatePairs = g; }
export function updateGates(night) {
  if (gatePairs) for (const g of gatePairs) { g.open.visible = !night; g.closed.visible = night; }
}

// Instantiate one pixel's render geometry: clone each unique building model (loaded
// once, browser-cached) and apply its world matrix; instance the decorative flats per
// archive. The group is parented to `town` so the mirror/wireframe toggles apply.
export async function addStructureTile(key, d) {
  if (!d || (!(d.placements && d.placements.length) && !(d.flats && d.flats.length))) {
    regionStructures.set(key, 'empty'); return;   // cache the empty pixel
  }
  const g = new THREE.Group();
  // Each placement carries its settlement's climate (mixed-climate regions span
  // desert/mountain/etc., and a large town can spill into a neighbour's pixel). So
  // key protos by (modelId, climate) — the model URL already varies by climate, so
  // the browser caches each variant separately.
  const protos = {};
  const need = new Map();
  for (const p of d.placements) {
    const c = p.climateBase ?? townClimate;
    need.set(p.modelId + '|' + c, { id: p.modelId, climate: c });
  }
  await Promise.all([...need.values()].map(({ id, climate }) =>
    loadModel(id, climate, townSeason).then(m => { protos[id + '|' + climate] = m; })));
  for (const p of d.placements) {
    const proto = protos[p.modelId + '|' + (p.climateBase ?? townClimate)];
    if (!proto) continue;
    const inst = proto.clone();
    inst.applyMatrix4(new THREE.Matrix4().fromArray(p.matrix));
    g.add(inst);
  }
  // Flats carry absolute world coords + worldW/H already (same as the town path),
  // so pass them straight through — no tile offset, unlike terrain nature scatter.
  const byArchive = {};
  for (const f of (d.flats || [])) (byArchive[f.archive] ||= []).push(f);
  for (const [archive, items] of Object.entries(byArchive)) {
    const { meta, tex } = await getFlatSheet(Number(archive));
    g.add(buildFlatInstances(meta, tex, items));
  }
  // A tile may have been evicted while its models were loading; honor that.
  if (regionStructures.get(key) !== 'loading') return;
  town.add(g);
  regionStructures.set(key, g);
}

// ---- World flats (nature + decorative billboards) ----
const flatSheets = new Map();
export function getFlatSheet(archive) {
  if (flatSheets.has(archive)) return flatSheets.get(archive);
  const p = Promise.all([
    fetch(`/asset/flatmeta/${archive}`).then(r => r.json()),
    new Promise(res => texLoader.load(`/asset/flatsheet/${archive}`, res)),
  ]).then(([meta, tex]) => {
    // flipY stays true (three.js default): the offset/repeat UV math below
    // (offset.y = 1-(v+h)/sheetH) is the flipY=true convention, unlike the agent
    // path which sets per-vertex UVs directly. Forcing flipY=false flips flats upside-down.
    tex.magFilter = THREE.NearestFilter; tex.minFilter = THREE.NearestFilter; tex.generateMipmaps = false;
    return { meta, tex };
  });
  flatSheets.set(archive, p);
  return p;
}

// Camera-facing (Y-axis, upright) billboard shader for instanced flats. The quad
// orients to the camera in the vertex shader (no per-frame CPU work); per-instance
// attributes carry the atlas cell (aOffset/aRepeat) and world size (aSize). The base
// of the billboard sits at the instance position (anchored, not centred).
function makeFlatMaterial(tex) {
  return new THREE.ShaderMaterial({
    uniforms: { map: { value: tex } },
    vertexShader: `
      attribute vec2 aOffset;
      attribute vec2 aRepeat;
      attribute vec2 aSize;
      varying vec2 vUv;
      void main() {
        vec3 P = vec3(instanceMatrix[3][0], instanceMatrix[3][1], instanceMatrix[3][2]);
        vec3 d = normalize(vec3(cameraPosition.x - P.x, 0.0, cameraPosition.z - P.z));
        vec3 right = vec3(d.z, 0.0, -d.x);              // perpendicular to view in XZ
        vec3 world = P
          + right * (position.x * aSize.x)              // position.x in [-0.5,0.5]
          + vec3(0.0, 1.0, 0.0) * ((position.y + 0.5) * aSize.y);  // base anchored at P.y
        gl_Position = projectionMatrix * viewMatrix * vec4(world, 1.0);
        vUv = aOffset + uv * aRepeat;
      }
    `,
    fragmentShader: `
      uniform sampler2D map;
      varying vec2 vUv;
      void main() {
        vec4 c = texture2D(map, vUv);
        if (c.a < 0.5) discard;
        gl_FragColor = c;
      }
    `,
    side: THREE.DoubleSide,
  });
}

// Build one InstancedMesh for all flats of an archive. `list` items are
// {record, x, y, z} (+ optional worldW/H override; else taken from the atlas cell).
export function buildFlatInstances(meta, tex, list) {
  // Filter to flats whose record exists in this atlas.
  const items = list.filter(f => meta.cells[f.record]);
  const n = items.length;
  const geo = new THREE.PlaneGeometry(1, 1);   // unit quad; sized + oriented in the shader
  const off = new Float32Array(n * 2), rep = new Float32Array(n * 2), siz = new Float32Array(n * 2);
  const mesh = new THREE.InstancedMesh(geo, makeFlatMaterial(tex), n);
  const m = new THREE.Matrix4();
  for (let i = 0; i < n; i++) {
    const f = items[i], c = meta.cells[f.record];
    off[i * 2] = c.u / meta.sheetW;        off[i * 2 + 1] = 1 - (c.v + c.h) / meta.sheetH;
    rep[i * 2] = c.w / meta.sheetW;        rep[i * 2 + 1] = c.h / meta.sheetH;
    siz[i * 2] = f.worldW ?? c.worldW;     siz[i * 2 + 1] = f.worldH ?? c.worldH;
    mesh.setMatrixAt(i, m.makeTranslation(f.x, f.y, f.z));
  }
  geo.setAttribute('aOffset', new THREE.InstancedBufferAttribute(off, 2));
  geo.setAttribute('aRepeat', new THREE.InstancedBufferAttribute(rep, 2));
  geo.setAttribute('aSize', new THREE.InstancedBufferAttribute(siz, 2));
  mesh.instanceMatrix.needsUpdate = true;
  mesh.frustumCulled = false;   // billboards span the scene; skip whole-mesh culling
  return mesh;
}

// Set a plane's UVs to a sheet cell (record row, frame col); optional horizontal flip.
// Shared with agents (sprite animation).
export function setCellUV(geo, s, record, frame, flip) {
  const m = s.meta;
  const uL = (frame * m.cellW) / s.sheetW, uR = ((frame + 1) * m.cellW) / s.sheetW;
  const vT = (record * m.cellH) / s.sheetH, vB = ((record + 1) * m.cellH) / s.sheetH;  // flipY=false: v=0 top
  const a = flip ? uR : uL, b = flip ? uL : uR;
  const uv = geo.attributes.uv;
  uv.setXY(0, a, vT); uv.setXY(1, b, vT); uv.setXY(2, a, vB); uv.setXY(3, b, vB);
  uv.needsUpdate = true;
}
