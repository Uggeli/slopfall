// engine/atmosphere.js — day/night sky, sun arc, weather tint, fog (The Renderer, R2).
// Reads the engine `world` (clock + weather) and `gfx` (brightness/fog) state and drives
// the scene background, fog, and the two lights. Mode-agnostic: no camera, OrbitControls,
// inspector, or HUD DOM (the shell writes the HUD clock from `world`).
import * as THREE from 'three';
import { scene, sun, ambient, world, gfx } from './scene.js';

const SKY_DAY = new THREE.Color(0x9fc0e8), SKY_NIGHT = new THREE.Color(0x10141f);
const SKY_DUSK = new THREE.Color(0xd98a4a);   // dawn/dusk warm
const tmpA = new THREE.Color(), tmpB = new THREE.Color();

// Weather → (sky desaturation toward grey, sun scale, fog tightness).
function weatherMods(w) {
  switch (w) {
    case 'Cloudy':   return { grey: 0.35, sun: 0.7, fog: 1.0 };
    case 'Overcast': return { grey: 0.6,  sun: 0.45, fog: 0.8 };
    case 'Fog':      return { grey: 0.5,  sun: 0.5, fog: 0.35 };
    case 'Rain':     return { grey: 0.7,  sun: 0.35, fog: 0.6 };
    case 'Thunder':  return { grey: 0.8,  sun: 0.25, fog: 0.5 };
    case 'Snow':     return { grey: 0.55, sun: 0.6, fog: 0.55, white: true };
    default:         return { grey: 0.0,  sun: 1.0, fog: 1.0 };   // Sunny
  }
}

export function updateAtmosphere(dt) {
  // dawn/dusk weighting: peaks near 6:00 and 18:00
  const h = world.hour + world.minute / 60;
  const twilight = Math.max(0, 1 - Math.min(Math.abs(h - 6), Math.abs(h - 18)) / 2.5);
  const sun01 = Math.max(0, Math.min(1, world.sun));
  const wm = weatherMods(world.weather);

  // target sky: night→day by daylight, warm push at twilight, grey by weather
  tmpA.copy(SKY_NIGHT).lerp(SKY_DAY, sun01);
  tmpA.lerp(SKY_DUSK, twilight * 0.6 * sun01);
  tmpB.setRGB((tmpA.r + tmpA.g + tmpA.b) / 3, (tmpA.r + tmpA.g + tmpA.b) / 3, (tmpA.r + tmpA.g + tmpA.b) / 3);
  if (wm.white) tmpB.setRGB(0.85, 0.87, 0.9);
  tmpA.lerp(tmpB, wm.grey);

  const k = 1 - Math.pow(0.001, dt);   // ~frame-rate-independent smoothing
  scene.background.lerp(tmpA, k);
  scene.fog.color.copy(scene.background);
  if (gfx.fogOn) {
    scene.fog.far = gfx.fogFar * wm.fog;
    scene.fog.near = scene.fog.far * 0.25;
  } else {
    scene.fog.near = scene.fog.far = 1e9;   // push fog past everything
  }

  // sun light: arc by hour (east→west), intensity by daylight*weather, warm at twilight
  const az = (h / 24) * Math.PI * 2 - Math.PI / 2;   // rough azimuth
  const pitch = Math.max(0.05, sun01);
  sun.position.set(Math.cos(az), pitch * 1.2 + 0.15, Math.sin(az));
  const sunTargetI = sun01 * wm.sun * 1.4 * gfx.bright;
  sun.intensity += (sunTargetI - sun.intensity) * k;
  tmpB.setRGB(1, 1 - twilight * 0.35, 1 - twilight * 0.55);   // warm at dawn/dusk
  sun.color.lerp(tmpB, k);
  const ambTarget = (0.25 + sun01 * 0.45 * (1 - wm.grey * 0.4)) * gfx.bright;
  ambient.intensity += (ambTarget - ambient.intensity) * k;
}
