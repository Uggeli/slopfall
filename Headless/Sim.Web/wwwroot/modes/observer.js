// modes/observer.js — the overview/spectator mode (The Renderer, R4).
// Owns the camera + OrbitControls + WASD fly-cam + framing + capture-mode test infra + the
// render loop, and mounts the inspector + HUD. Each frame it hands the engine a *focal point*
// (controls.target) the engine cannot tell apart from an avatar's — so a first-person mode is
// free on the rendering side: it would supply the same engine an eye-height camera + the
// avatar position as the focal point, and reuse everything below unchanged.
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { renderer } from '../engine/scene.js';
import { terrainTargetMeshes, getRegionMeta } from '../engine/terrain.js';
import { mountHud } from '../ui/hud.js';
import { mountInspector } from '../ui/inspector.js';

export function createObserver({ net, engine }) {
  const camera = new THREE.PerspectiveCamera(60, innerWidth / innerHeight, 0.1, 10000);
  const controls = new OrbitControls(camera, renderer.domElement);
  controls.minDistance = 5;                    // don't let dolly collapse the pivot to ~0 (frozen zoom)
  controls.maxPolarAngle = Math.PI * 0.495;    // keep the camera above the ground plane

  // --- Capture mode (test infra: deterministic screenshot for the parity gate) ---
  const _capQ = new URLSearchParams(location.search);
  const CAPTURE = _capQ.has('capture');
  const CAP_TICK = Number(_capQ.get('tick') ?? 50);
  const CAP_CAM = (_capQ.get('cam') ?? '600,900,600,600,0,600').split(',').map(Number);
  let _capState = CAPTURE ? 'ff' : 'off';   // ff -> waiting for CAP_TICK; settle -> counting down; done
  let _capSettle = 90;                       // 90 frames is enough for interpolation alpha→1 once sim is paused
  if (CAPTURE) window.__captureReady = false;

  // The mode mounts the inspector + HUD, handing them the camera it owns + the net.
  const hud = mountHud({ net, camera });
  const inspector = mountInspector({ net, camera });

  // WASD fly-cam (on top of OrbitControls): move camera + target together over the
  // ground plane. W/S forward-back, A/D strafe, E/Space up, Q down, Shift sprints.
  // Speed scales with zoom (distance to target) so it feels right at any altitude.
  const keys = new Set();
  addEventListener('keydown', e => {
    if (e.target.tagName === 'INPUT') return;
    if (e.code === 'Space') e.preventDefault();
    keys.add(e.code);
  });
  addEventListener('keyup', e => keys.delete(e.code));
  addEventListener('blur', () => keys.clear());   // don't get stuck if focus leaves
  const _fwd = new THREE.Vector3(), _right = new THREE.Vector3(),
        _up = new THREE.Vector3(0, 1, 0), _move = new THREE.Vector3();
  function moveCamera(dt) {
    if (!keys.size) return;
    const fast = keys.has('ShiftLeft') || keys.has('ShiftRight');
    const speed = Math.max(40, camera.position.distanceTo(controls.target)) * dt * (fast ? 3 : 1);

    // Horizontal WASD: pan camera + pivot together across the ground.
    _move.set(0, 0, 0);
    camera.getWorldDirection(_fwd); _fwd.y = 0;
    if (_fwd.lengthSq() < 1e-6) _fwd.set(0, 0, -1);   // looking straight down
    _fwd.normalize();
    _right.crossVectors(_fwd, _up).normalize();
    if (keys.has('KeyW')) _move.add(_fwd);
    if (keys.has('KeyS')) _move.sub(_fwd);
    if (keys.has('KeyD')) _move.add(_right);
    if (keys.has('KeyA')) _move.sub(_right);
    if (_move.lengthSq() > 0) {
      _move.normalize().multiplyScalar(speed);
      camera.position.add(_move); controls.target.add(_move);
    }

    // Vertical E/Q: raise/lower the CAMERA only — the pivot stays on the ground.
    // (Moving the pivot up would fight followGround, which re-grounds it each frame.)
    let vy = 0;
    if (keys.has('KeyE') || keys.has('Space')) vy += 1;
    if (keys.has('KeyQ')) vy -= 1;
    if (vy) camera.position.y += vy * speed;
  }

  // Keep the orbit pivot ON the ground. The terrain has real elevation (region mode
  // especially), but controls.target sat at y=0 — so panning/zooming toward raised
  // ground drove the pivot underground and the controls felt dead (the same fixed-
  // height bug as the billboards). Sample the terrain under the target and ease the
  // pivot AND camera to it together, so the offset is preserved and OrbitControls
  // stays consistent while zoom/pan act relative to the surface.
  const _hcaster = new THREE.Raycaster();
  const _hfrom = new THREE.Vector3(), _hdir = new THREE.Vector3(0, -1, 0);
  let groundAccum = 0, groundY = 0;
  function followGround(dt) {
    groundAccum += dt;
    if (groundAccum >= 0.1) {                          // ~10 Hz sampling is plenty for panning
      groundAccum = 0;
      const meshes = terrainTargetMeshes(controls.target);
      if (meshes && meshes.length) {
        _hfrom.set(controls.target.x, 1e5, controls.target.z);
        _hcaster.set(_hfrom, _hdir);
        const hit = _hcaster.intersectObjects(meshes, false)[0];
        if (hit) groundY = hit.point.y;
      }
    }
    const dy = (groundY - controls.target.y) * Math.min(1, dt * 6);
    if (Math.abs(dy) > 1e-4) { controls.target.y += dy; camera.position.y += dy; }
  }

  function frameTown(min, max) {
    const cx = (min[0] + max[0]) / 2, cz = (min[2] + max[2]) / 2;
    const r = Math.max(max[0] - min[0], max[2] - min[2], 20);
    controls.target.set(cx, 0, cz);
    camera.position.set(cx + r * 0.55, r * 0.7, cz + r * 0.95);
    camera.far = r * 20; camera.updateProjectionMatrix();
    controls.update();
  }

  // Region mode: framing the whole region (km across) would put everything past the
  // fog, so we drop the camera over the region centre at fog-scale altitude and let
  // the streamer fill in. Pan/zoom to roam — fog hides the moving tile edge.
  function frameRegion() {
    const regionMeta = getRegionMeta();
    const ts = regionMeta.tileSize;
    const cx = ((regionMeta.mx0 + regionMeta.mx1) / 2 - regionMeta.mx0) * ts;
    const cz = ((regionMeta.my0 + regionMeta.my1) / 2 - regionMeta.my0) * ts;
    const D = 1500;
    controls.target.set(cx, 0, cz);
    camera.position.set(cx + D * 0.4, D * 0.85, cz + D * 0.7);
    camera.far = 20000; camera.updateProjectionMatrix();
    controls.update();
  }

  addEventListener('resize', () => {
    camera.aspect = innerWidth / innerHeight; camera.updateProjectionMatrix();
    renderer.setSize(innerWidth, innerHeight);
  });

  // Capture: main's onSnapshot calls this each tick; grab the first frame at-or-past
  // CAP_TICK by posing the camera + pausing the sim, then let the loop settle 90 frames.
  function maybeCapture(tick) {
    if (_capState === 'ff' && tick >= CAP_TICK) {
      _capState = 'settle';
      hud.setSpeed(0);
      camera.position.set(CAP_CAM[0], CAP_CAM[1], CAP_CAM[2]);
      controls.target.set(CAP_CAM[3], CAP_CAM[4], CAP_CAM[5]);
      controls.update();
    }
  }
  // On socket open: capture fast-forwards (MAX), live runs at the normal rate.
  function onOpen() { hud.setSpeed(CAPTURE ? -1 : 10); }

  let lastFrame = performance.now() / 1000;
  function loop() {
    requestAnimationFrame(loop);
    const now = performance.now() / 1000;
    const dt = Math.min(0.1, now - lastFrame); lastFrame = now;
    moveCamera(dt);
    followGround(dt);
    controls.update();
    // Capture-mode settle (test infra): idempotent controls.update before the draw.
    if (_capState === 'settle') {
      controls.update();
      if (--_capSettle <= 0) { _capState = 'done'; window.__captureReady = true; }
    }
    // Hand the engine the camera + focal point; it streams + draws around the focal point.
    engine.render({ camera, focal: controls.target, dt, now, selectedId: inspector.selectedId });
    hud.updateClock();   // clock/weather readout (DOM-only; order vs the engine block is irrelevant)
  }
  function start() { loop(); }

  return {
    camera, hud, inspector,
    get focal() { return controls.target; },
    get capturing() { return CAPTURE; },
    frameTown, frameRegion, maybeCapture, onOpen, start,
  };
}
