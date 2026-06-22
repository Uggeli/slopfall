// engine/index.js — the engine facade (The Renderer, R4).
// createEngine({net}).render({camera, focal, dt, now, selectedId}) streams + draws the world
// around a *focal point* it cannot tell apart from an avatar's. The mode owns the camera and
// supplies the focal point (observer: controls.target; player: avatar position). The engine
// never moves a camera, references OrbitControls, or touches the inspector/HUD DOM.
import { renderer, scene, world } from './scene.js';
import { updateGates } from './world.js';
import { updateAtmosphere } from './atmosphere.js';
import { streamRegionTerrain } from './terrain.js';
import { updateAgents } from './agents.js';

export function createEngine({ net }) {
  // One frame of world simulation + draw, in the canvas-affecting order it has always run.
  function render({ camera, focal, dt, now, selectedId }) {
    updateGates(world.night);
    updateAtmosphere(dt);
    streamRegionTerrain(dt, focal, camera, net);
    if (net.curSnap.size) updateAgents(now, camera, net, selectedId);
    renderer.render(scene, camera);
  }
  return { render };
}
