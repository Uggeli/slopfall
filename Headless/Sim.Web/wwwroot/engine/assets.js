// engine/assets.js — the /asset/* loader client (The Renderer, R2).
// Owns the shared texture loader and the GLTF model loader (+ nearest-filter pass for
// the pixel-art look). Mode-agnostic: no camera, OrbitControls, inspector, or HUD DOM.
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';

// Shared by terrain (ground atlas), agents (sprite sheets), and world (flat sheets).
export const texLoader = new THREE.TextureLoader();

const manager = new THREE.LoadingManager();
manager.onError = url => console.warn('load error', url);
const loader = new GLTFLoader(manager);
loader.setResourcePath(location.origin + '/');   // resolve /asset/texture URLs

function applyNearest(root) {
  root.traverse(o => {
    if (!o.isMesh) return;
    const mats = Array.isArray(o.material) ? o.material : [o.material];
    for (const m of mats) if (m.map) {
      m.map.magFilter = THREE.NearestFilter;
      m.map.minFilter = THREE.NearestFilter;
      m.map.generateMipmaps = false;
      m.map.needsUpdate = true;
    }
  });
}

export function loadModel(id, climate, season) {
  return new Promise(res => {
    loader.load(`/asset/model/${id}?climate=${climate}&season=${season}`,
      gltf => { applyNearest(gltf.scene); res(gltf.scene); },
      undefined,
      () => res(null));   // missing model -> skip its instances
  });
}
