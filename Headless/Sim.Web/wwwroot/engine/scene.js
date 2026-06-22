// engine/scene.js — the mode-agnostic render core (The Renderer, R2).
// Owns the THREE singletons every engine module shares: the WebGL renderer (canvas
// appended here), the scene graph, the two lights, and the debug grid. It must NOT
// reference the camera, OrbitControls, the inspector, or any HUD DOM — those are
// mode/ui concerns the shell owns. The render loop also stays in the shell (a thin
// consumer) until R4 turns it into engine.render(frame).
import * as THREE from 'three';

export const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setPixelRatio(devicePixelRatio);
renderer.setSize(innerWidth, innerHeight);
document.body.appendChild(renderer.domElement);

export const scene = new THREE.Scene();
scene.background = new THREE.Color(0x9fc0e8);
scene.fog = new THREE.Fog(0x9fc0e8, 400, 1600);   // hides the 3x3 terrain edge too

export const ambient = new THREE.AmbientLight(0xffffff, 0.6);
scene.add(ambient);
export const sun = new THREE.DirectionalLight(0xffffff, 1.2);
sun.position.set(0.5, 1, 0.4);
scene.add(sun);

export const grid = new THREE.GridHelper(2000, 200, 0x335577, 0x1c2738);
scene.add(grid);

// Per-frame render input the shell's onSnapshot writes and atmosphere reads — this is
// the "Frame" the engine draws (clock + weather + sun level).
export const world = { hour: 12, minute: 0, night: false, sun: 1, weather: 'Sunny' };

// Graphics settings the HUD sliders write and the renderer/atmosphere read. fogFar lives
// here (not a module-scope let) so it stays mutable across modules: town scale 1600;
// region mode widens it to 3200 so several tiles show.
export const gfx = { bright: 1, fogOn: true, fogFar: 1600 };
