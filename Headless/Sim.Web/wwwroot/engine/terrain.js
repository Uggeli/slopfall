// engine/terrain.js — ground meshes + region terrain streaming (The Renderer, R2).
// Owns the terrain group, the shared ground-atlas material, the region tile cache, and
// the per-pixel streamer (terrain tiles + nature scatter, and it drives world's structure
// streaming on the same ring). Mode-agnostic: it does not reference OrbitControls or HUD
// DOM. The streamer/target query take a *focal point* + camera + net from the shell (the
// mode owns those); terrain reads them, it doesn't own them.
import * as THREE from 'three';
import { scene, renderer } from './scene.js';
import { texLoader } from './assets.js';
import { town, regionStructures, addStructureTile, getFlatSheet, buildFlatInstances } from './world.js';

let terrainMat = null;          // shared ground-atlas material
export const terrainGroup = new THREE.Group();
scene.add(terrainGroup);

// Region mode: terrain is too big to ship in one payload, so we stream it per map pixel
// as the focal point pans. regionMeta carries the pixel bbox + tile size (819.2 m);
// regionTiles caches the meshes we've built/requested by "mx,my".
let regionMeta = null;
const regionTiles = new Map();   // "mx,my" -> mesh | 'loading'
let lastViewMx = -999, lastViewMy = -999;   // last focal pixel reported to the server
export const getRegionMeta = () => regionMeta;
export const setRegionMeta = (m) => { regionMeta = m; };

// Build a per-cell textured terrain mesh: heightfield (129x129) + a 128x128
// tilemap that picks a ground-tile per cell. The server bakes the atlas with all
// 4 tile orientations (32x7 grid, 64px tiles), so the client samples a pre-oriented
// tile with identity UVs — zero orientation logic on the client.
const ATLAS_COLS = 32, ATLAS_ROWS = 7, ATLAS_RECCOLS = 8;

function ensureTerrainMat(archive) {
  if (terrainMat) return terrainMat;
  terrainMat = new THREE.MeshLambertMaterial({ color: 0x8a8a8a });
  texLoader.load(`/asset/groundatlas/${archive}`, t => {
    t.flipY = false;   // atlas is top-down; UV v=0 at top
    // Crisp pixels up close (nearest mag), but mip + anisotropy when the tile
    // minifies — otherwise the 64px ground art aliases into diagonal moiré bands
    // at grazing angles. (Atlas mips bleed slightly across the 8x7 grid at far
    // distance; the half-texel UV inset keeps the base level clean.)
    t.magFilter = THREE.NearestFilter;
    t.minFilter = THREE.LinearMipmapLinearFilter;
    t.generateMipmaps = true;
    t.anisotropy = renderer.capabilities.getMaxAnisotropy();
    terrainMat.map = t; terrainMat.color.set(0xffffff); terrainMat.needsUpdate = true;
  });
  return terrainMat;
}

// Rebuild all tile meshes (centre + neighbours) into terrainGroup. The shell applies the
// 'show terrain' toggle (terrainGroup.visible) after calling this.
export function buildTerrain(tiles) {
  while (terrainGroup.children.length) { const m = terrainGroup.children.pop(); m.geometry.dispose(); }
  for (const d of tiles) {
    const m = buildTileMesh(d);
    m.position.set(d.originX, 0, d.originZ);
    terrainGroup.add(m);
  }
}

function buildTileMesh(d) {
  const dim = d.dim, step = d.worldSize / (dim - 1), td = d.tileDim;
  // Server emits canonical DFU-frame data (x→world X east, y→world Z north); the
  // client renders it raw — no flips. byte[] arrives base64 from System.Text.Json.
  const tilemap = Uint8Array.from(atob(d.tilemap), c => c.charCodeAt(0));
  const heights = d.heights;
  const H = (gx, gy) => heights[Math.min(dim - 1, Math.max(0, gx)) * dim + Math.min(dim - 1, Math.max(0, gy))];
  const wx = gx => gx * step;
  const wz = gy => gy * step;
  // Smooth normal per heightfield grid point (central differences).
  const N = (gx, gy) => {
    const nx = H(gx - 1, gy) - H(gx + 1, gy), nz = H(gx, gy - 1) - H(gx, gy + 1);
    const v = new THREE.Vector3(nx, 2 * step, nz); v.normalize(); return v;
  };

  const cells = (td) * (td), pos = new Float32Array(cells * 4 * 3),
        nor = new Float32Array(cells * 4 * 3), uv = new Float32Array(cells * 4 * 2);
  const idx = new Uint32Array(cells * 6);
  const epsU = 0.5 / (ATLAS_COLS * 64), epsV = 0.5 / (ATLAS_ROWS * 64);  // half-texel inset
  let vp = 0, up = 0, ip = 0, vbase = 0;

  for (let cx = 0; cx < td; cx++) {
    for (let cy = 0; cy < td; cy++) {
      const tile = tilemap[cx * td + cy];
      // The atlas is pre-oriented: orientation o = rot + 2*flip selects a column band.
      // Server bakes the rotation; client just looks up the cell with identity UVs.
      const rec = tile & 63, o = ((tile & 64) ? 1 : 0) + ((tile & 128) ? 2 : 0);
      const col = (rec % ATLAS_RECCOLS) + o * ATLAS_RECCOLS, row = (rec / ATLAS_RECCOLS) | 0;
      const u0 = col / ATLAS_COLS + epsU, u1 = (col + 1) / ATLAS_COLS - epsU;
      const v0 = row / ATLAS_ROWS + epsV, v1 = (row + 1) / ATLAS_ROWS - epsV;
      // Identity UV per quad corner [ (cx,cy),(cx+1,cy),(cx,cy+1),(cx+1,cy+1) ]:
      // tile u <- +terrain X (cu), tile v <- +terrain Z (cv). No client transforms.
      const luv = [[0, 0], [1, 0], [0, 1], [1, 1]];

      const corners = [[cx, cy], [cx + 1, cy], [cx, cy + 1], [cx + 1, cy + 1]];
      for (let k = 0; k < 4; k++) {
        const [gx, gy] = corners[k];
        pos[vp++] = wx(gx); pos[vp++] = H(gx, gy); pos[vp++] = wz(gy);
        const n = N(gx, gy); nor[vp - 3] = n.x; nor[vp - 2] = n.y; nor[vp - 1] = n.z;
        uv[up++] = u0 + luv[k][0] * (u1 - u0);
        uv[up++] = v0 + luv[k][1] * (v1 - v0);
      }
      // two triangles (CCW up-facing): 0,3,1  0,2,3
      idx[ip++] = vbase; idx[ip++] = vbase + 3; idx[ip++] = vbase + 1;
      idx[ip++] = vbase; idx[ip++] = vbase + 2; idx[ip++] = vbase + 3;
      vbase += 4;
    }
  }

  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  geo.setAttribute('normal', new THREE.BufferAttribute(nor, 3));
  geo.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
  geo.setIndex(new THREE.BufferAttribute(idx, 1));

  return new THREE.Mesh(geo, ensureTerrainMat(d.groundArchive));
}

// Which terrain mesh(es) sit under the focal point — for the shell's ground-follow.
// town: the 3x3 tiles; region: the single streamed tile (null if not loaded yet).
export function terrainTargetMeshes(focal) {
  if (!regionMeta) return terrainGroup.children;   // town: just the 3x3 tiles
  const ts = regionMeta.tileSize, t = focal;
  const mx = regionMeta.mx0 + Math.round(t.x / ts);
  const my = regionMeta.my1 - Math.round(t.z / ts);
  const m = regionTiles.get(mx + ',' + my);
  return (m && m !== 'loading') ? [m] : null;       // null → tile not streamed yet
}

// Stream the region's terrain around the focal point. We map the focal (x,z) back to a
// map pixel, request the ring of pixels within R of it (nearest-first, a few per tick so
// the single-threaded asset service keeps up), and evict tiles that drift well outside
// the ring. Fog hides the moving edge, so a modest R suffices. Server caches each pixel —
// re-entry is cheap. `focal` is the mode's focal point, `cam` the camera (for ring size),
// `net` the transport (to report the focal pixel for snapshot filtering).
let streamAccum = 0;
export function streamRegionTerrain(dt, focal, cam, net) {
  if (!regionMeta) return;
  streamAccum += dt;
  if (streamAccum < 0.3) return;   // throttle: ~3 Hz is plenty for panning
  streamAccum = 0;

  const ts = regionMeta.tileSize, t = focal;
  // Inverse of the server placement: worldX = (mx-mx0)*ts, worldZ = (my1-my)*ts.
  const cmx = regionMeta.mx0 + Math.round(t.x / ts);
  const cmy = regionMeta.my1 - Math.round(t.z / ts);
  const dist = cam.position.distanceTo(t);
  const R = Math.min(6, Math.max(2, Math.ceil(dist / ts) + 1));

  // Tell the server our focal pixel so it filters the agent snapshot to our ring
  // (R+2 matches the structure/terrain eviction hysteresis, so agents don't pop at
  // the visible edge). Only on change, to avoid spamming the socket.
  if (cmx !== lastViewMx || cmy !== lastViewMy) {
    lastViewMx = cmx; lastViewMy = cmy;
    net.send({ type: 'view', mx: cmx, my: cmy, r: R + 2 });
  }

  // Collect missing pixels in the ring, request the nearest few this tick.
  const want = [];
  for (let mx = cmx - R; mx <= cmx + R; mx++)
    for (let my = cmy - R; my <= cmy + R; my++) {
      if (mx < regionMeta.mx0 || mx > regionMeta.mx1 ||
          my < regionMeta.my0 || my > regionMeta.my1) continue;
      const key = mx + ',' + my;
      if (regionTiles.has(key)) continue;
      want.push({ mx, my, key, d: (mx - cmx) * (mx - cmx) + (my - cmy) * (my - cmy) });
    }
  want.sort((a, b) => a.d - b.d);
  for (const w of want.slice(0, 8)) {
    regionTiles.set(w.key, 'loading');
    fetch(`/asset/terraintile/${w.mx}/${w.my}`)
      .then(r => r.ok ? r.json() : null)
      .then(d => {
        if (!d) { regionTiles.delete(w.key); return; }
        const m = buildTileMesh(d);
        m.position.set(d.originX, 0, d.originZ);
        m.visible = terrainGroup.visible;   // honor the shell's 'show terrain' toggle
        terrainGroup.add(m);
        regionTiles.set(w.key, m);
        // Wilderness nature scatter: instanced billboards from the tile's climate
        // nature atlas, positioned in world space (tile origin + tile-local scatter).
        // Parented to the tile mesh so they unload with it.
        if (d.nature && d.nature.length && d.natureArchive) {
          getFlatSheet(d.natureArchive).then(({ meta, tex }) => {
            const items = d.nature.map(n => ({ record: n.record, x: d.originX + n.x, y: n.y, z: d.originZ + n.z }));
            m.add(buildFlatInstances(meta, tex, items));
          });
        }
      })
      .catch(() => regionTiles.delete(w.key));
  }

  // Evict tiles that have drifted beyond the ring (keep a 2-pixel hysteresis).
  for (const [key, m] of regionTiles) {
    if (m === 'loading') continue;
    const [mx, my] = key.split(',').map(Number);
    if (Math.abs(mx - cmx) > R + 2 || Math.abs(my - cmy) > R + 2) {
      // Dispose the tile mesh AND its child nature InstancedMesh (geometry +
      // per-instance attributes + its own ShaderMaterial). The atlas texture is
      // shared/cached in flatSheets and must NOT be disposed here.
      m.traverse(o => {
        if (o.isMesh || o.isInstancedMesh) { o.geometry.dispose(); if (o !== m) o.material.dispose(); }
      });
      terrainGroup.remove(m); m.geometry.dispose(); regionTiles.delete(key);
    }
  }

  // Structures (buildings + decorative flats) stream on the SAME ring as terrain.
  const wantS = [];
  for (let mx = cmx - R; mx <= cmx + R; mx++)
    for (let my = cmy - R; my <= cmy + R; my++) {
      if (mx < regionMeta.mx0 || mx > regionMeta.mx1 ||
          my < regionMeta.my0 || my > regionMeta.my1) continue;
      const key = mx + ',' + my;
      if (regionStructures.has(key)) continue;
      wantS.push({ mx, my, key, d: (mx - cmx) * (mx - cmx) + (my - cmy) * (my - cmy) });
    }
  wantS.sort((a, b) => a.d - b.d);
  for (const w of wantS.slice(0, 4)) {
    regionStructures.set(w.key, 'loading');
    fetch(`/asset/towntile/${w.mx}/${w.my}`)
      .then(r => r.ok ? r.json() : null)
      .then(d => addStructureTile(w.key, d))
      .catch(() => regionStructures.delete(w.key));
  }
  // Evict structure tiles beyond the ring (same 2-pixel hysteresis as terrain).
  for (const [key, g] of regionStructures) {
    if (g === 'loading' || g === 'empty') continue;
    const [mx, my] = key.split(',').map(Number);
    if (Math.abs(mx - cmx) > R + 2 || Math.abs(my - cmy) > R + 2) {
      // Building instances are proto.clone()s that SHARE the proto's geometry +
      // material (cached, reused by other tiles) — never dispose those. Only the
      // per-tile flat InstancedMeshes own their geometry + ShaderMaterial; dispose
      // those, but not the atlas texture (shared/cached in flatSheets).
      g.traverse(o => {
        if (o.isInstancedMesh) { o.geometry.dispose(); o.material.dispose(); }
      });
      town.remove(g); regionStructures.delete(key);
    }
  }
}
