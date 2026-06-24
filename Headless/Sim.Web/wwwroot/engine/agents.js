// engine/agents.js — the sim's people as animated sprite billboards (The Renderer, R2).
// Owns the agent billboard pool, the sprite-sheet cache, and the per-frame interp/animation
// in updateAgents. It reads the camera (to face billboards) and the net snapshot buffer —
// both handed in by the shell; it does not own them, and references no OrbitControls or HUD
// DOM. The selection ring is drawn here but the selected id is the inspector's (passed in).
import * as THREE from 'three';
import { scene } from './scene.js';
import { texLoader } from './assets.js';
import { setCellUV } from './world.js';

export const agents = new THREE.Group();
scene.add(agents);
let spritePools = null;   // { civilian:[], guard:[], monster:[] }
export function setSpritePools(p) { spritePools = p; }
const sheets = new Map();          // archive -> { tex, meta, mat, sheetW, sheetH } | 'loading'
const people = new Map();          // entityId -> { mesh, archive }
// 8-direction -> (record, flip): records 0-4 = facing S/SW/W/NW/N; NE/E/SE reuse
// records 3/2/1 mirrored (DFU MobilePersonBillboard). Idle = record 5.
const ORI_RECORD = [0, 1, 2, 3, 4, 3, 2, 1];
const ORI_FLIP   = [false, false, false, false, false, true, true, true];
const DIR_SIGN = 1;   // verified: maps walk direction to the correct facing sprite

// Render-kind (server WorldRunner): 0 civilian, 1 guard, 2 monster.
const RK_CIVILIAN = 0, RK_GUARD = 1, RK_MONSTER = 2;
// Animation FPS per state.
const FPS_WALK_PEOPLE = 4, FPS_WALK_COMBAT = 6, FPS_ATTACK = 10, FPS_IDLE = 4;
// ActivityKind.Attack ordinal (BehaviorRegistry.ActivityKind).
const ACT_ATTACK = 19;
// ActivityPhase ordinals (server ActivityPhase enum).
const PHASE_MOVING = 0, PHASE_DOING = 1, PHASE_QUEUED = 2;
// Muted amber tint applied to agents in the Queued phase so a queue reads as "waiting".
const QUEUED_TINT = 0xd4944a;

function poolFor(kind) {
  if (!spritePools) return null;
  if (kind === RK_MONSTER) return spritePools.monster;
  if (kind === RK_GUARD)   return spritePools.guard;
  return spritePools.civilian;
}

function hash32(n) { n = (n ^ 61) ^ (n >>> 16); n = (n + (n << 3)) | 0; n ^= n >>> 4; n = Math.imul(n, 0x27d4eb2d); n ^= n >>> 15; return n >>> 0; }

function getSheet(archive) {
  let s = sheets.get(archive);
  if (s) return s === 'loading' ? null : s;
  sheets.set(archive, 'loading');
  Promise.all([
    fetch(`/asset/spritemeta/${archive}`).then(r => r.json()),
    new Promise(res => texLoader.load(`/asset/spritesheet/${archive}`, res)),
  ]).then(([meta, tex]) => {
    tex.flipY = false; tex.magFilter = THREE.NearestFilter; tex.minFilter = THREE.NearestFilter; tex.generateMipmaps = false;
    const mat = new THREE.MeshBasicMaterial({ map: tex, alphaTest: 0.5, side: THREE.DoubleSide });
    sheets.set(archive, { tex, meta, mat, sheetW: meta.cols * meta.cellW, sheetH: meta.rows * meta.cellH });
  }).catch(() => sheets.delete(archive));
  return null;
}

// A flat ring marks the selected agent (updateAgents keeps it on their feet). The inspector
// owns the selected id; agents.js owns the ring and clears it on deselect.
const selRing = new THREE.Mesh(
  new THREE.RingGeometry(0.55, 0.78, 28),
  new THREE.MeshBasicMaterial({ color: 0x9fd0ff, side: THREE.DoubleSide, transparent: true, opacity: 0.85, depthWrite: false }));
selRing.rotation.x = -Math.PI / 2;   // lay flat on the ground
selRing.visible = false;
scene.add(selRing);
export function clearSelRing() { selRing.visible = false; }

// Per-frame: interpolate every agent prev->cur, billboard toward the camera, pick the
// animation record/frame, and keep the selection ring on the selected agent's feet.
// `cam` faces the billboards; `net` supplies the snapshot buffer + alpha; `selectedId` is
// the inspector's current selection (-1 if none).
export function updateAgents(t, cam, net, selectedId) {
  if (!spritePools) return;
  // Interpolate prev->cur over the ACTUAL snapshot gap (the web pump runs ~5 Hz,
  // not the 10 tps tick rate), so movement glides instead of teleport-then-freeze.
  const alpha = net.alpha(t);
  const seen = new Set();
  selRing.visible = false;   // re-shown below only if the selected agent is present

  for (const [id, c] of net.curSnap) {
    seen.add(id);
    const kind = c[4] | 0;
    const pool = poolFor(kind);
    if (!pool || pool.length === 0) continue;
    const archive = pool[hash32(id) % pool.length];
    const s = getSheet(archive);
    if (!s) continue;

    let p = people.get(id);
    if (!p || p.archive !== archive) {
      if (p) { if (p.tintMat) p.tintMat.dispose(); agents.remove(p.mesh); }
      const geo = new THREE.PlaneGeometry(1, 1);   // unit quad; sized per displayed record below
      const mesh = new THREE.Mesh(geo, s.mat);
      mesh.userData.id = id;   // for click-to-inspect raycasting
      agents.add(mesh);
      p = { mesh, archive, geo, tintMat: null };
      people.set(id, p);
    }

    // interpolate position prev->cur (incl. ground height for region towns)
    const pr = net.prevSnap.get(id) || c;
    const x = pr[0] + (c[0] - pr[0]) * alpha, z = pr[1] + (c[1] - pr[1]) * alpha;
    const gy = pr[3] + (c[3] - pr[3]) * alpha;

    if (id === selectedId) { selRing.position.set(x, gy + 0.05, z); selRing.visible = true; }

    // Direction index 0-7 (S,SW,W,NW,N,NE,E,SE) relative to the camera-locked
    // billboard. Moving: derive from velocity (verified). Else: derive from yaw.
    const mvx = c[0] - pr[0], mvz = c[1] - pr[1];
    const speed = Math.hypot(mvx, mvz);
    let fx, fz;
    if (speed >= 0.05) { const fl = 1 / speed; fx = mvx * fl; fz = mvz * fl; }
    else { const ry = c[2] * Math.PI / 180; fx = Math.sin(ry); fz = Math.cos(ry); }   // yaw is degrees = atan2(dx,dz)
    let dx = cam.position.x - x, dz = cam.position.z - z;
    const dl = Math.hypot(dx, dz) || 1; dx /= dl; dz /= dl;
    const dot = dx * fx + dz * fz, cross = dx * fz - dz * fx;
    let ori = Math.round(DIR_SIGN * Math.atan2(cross, dot) / (Math.PI / 4));
    ori = ((ori % 8) + 8) % 8;

    const combat = (kind === RK_GUARD || kind === RK_MONSTER);
    const attacking = combat && (c[5] | 0) === ACT_ATTACK;

    let base, fps, directional;
    if (attacking)           { base = 5;  fps = FPS_ATTACK;       directional = true; }   // attack rows 5-9
    else if (speed >= 0.05)  { base = 0;  fps = combat ? FPS_WALK_COMBAT : FPS_WALK_PEOPLE; directional = true; }  // walk rows 0-4
    else if (combat)         { base = 15; fps = FPS_IDLE;          directional = true; }   // combat idle rows 15-19
    else                     { base = 5;  fps = FPS_IDLE;          directional = false; }  // civilian idle row 5

    let record = directional ? base + ORI_RECORD[ori] : base;
    let flip = directional ? ORI_FLIP[ori] : false;
    if (record >= s.meta.rows) { record = ORI_RECORD[ori]; flip = ORI_FLIP[ori]; }   // archive lacks this block → fall back to walk

    const fc = s.meta.frames[record] || 1;
    const frame = Math.floor(t * fps) % fc;
    setCellUV(p.geo, s, record, frame, flip);

    // Size the billboard to THIS record's own world dimensions. Each animation record has
    // its own native pixel size and DF scale factor (a civilian's idle frame is taller and
    // differently scaled than its walk frames), so sizing every record from record 0 made
    // non-idle agents render shrunken. recWorldW/H are per-record metres (fall back to the
    // legacy single size for older metas). The unit quad is scaled, not rebuilt, per frame.
    const rw = (s.meta.recWorldW && s.meta.recWorldW[record]) || s.meta.worldW;
    const rh = (s.meta.recWorldH && s.meta.recWorldH[record]) || s.meta.worldH;
    p.mesh.scale.set(rw, rh, 1);
    p.mesh.position.set(x, gy + rh / 2, z);
    // Aim at the camera in the XZ plane only (same height as the sprite), so the
    // billboard stays vertical. Using a fixed Y here tilted it by the town's ground
    // pad height (gy), which differs per town — hence the axis flip when panning.
    p.mesh.lookAt(cam.position.x, p.mesh.position.y, cam.position.z);   // upright billboard

    // Phase tint: Queued agents get a muted-amber overlay so a queue reads as "waiting".
    // We clone the shared archive material only when needed (one clone per queued agent),
    // disposing it and restoring the shared mat when the agent leaves the Queued phase.
    const phase = c[6] | 0;
    if (phase === PHASE_QUEUED) {
      if (!p.tintMat) {
        p.tintMat = s.mat.clone();
        p.tintMat.color.setHex(QUEUED_TINT);
      }
      p.mesh.material = p.tintMat;
    } else {
      if (p.tintMat) { p.tintMat.dispose(); p.tintMat = null; }
      p.mesh.material = s.mat;
    }
  }

  for (const [id, p] of people) if (!seen.has(id)) { if (p.tintMat) p.tintMat.dispose(); agents.remove(p.mesh); p.geo.dispose(); people.delete(id); }
}
