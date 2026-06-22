// net/client.js — mode-agnostic transport for the town3d client (The Renderer, R1).
// Owns: WebSocket connect/reconnect, Frame decode, the snapshot double-buffer, and
// interpolation timing (alpha). It must NOT reference OrbitControls, the observer
// camera, the inspector, or any HUD DOM — app-side reactions come back via callbacks.
// Browser globals (WebSocket, location, performance) are used ONLY inside connect(), so this module
// imports cleanly in Node for unit tests.

const SNAP_INTERVAL = 0.2; // fallback gap (s); the web pump publishes ~5 Hz

export function createNet({ onSnapshot, onDetail, onBuilding, onStatus, onOpen } = {}) {
  let ws = null;
  let prevSnap = new Map(), curSnap = new Map(); // id -> [x, z, yaw, groundY, kind, activity]
  let prevTime = 0, curTime = 0;

  // Decode one 'snap' frame into the double-buffer. Pure (no socket/DOM); `now` is the
  // render clock in seconds. Exposed for tests; the live path calls it from onmessage.
  function ingest(msg, now) {
    prevSnap = curSnap; prevTime = curTime;
    // id -> [x, z, yaw, groundY, kind, activity]; groundY (idx 7) is the town's
    // terrain-pad height in region mode (0 in town mode); kind (idx 6) is the
    // render-kind; activity (idx 3) is the ActivityKind ordinal.
    curSnap = new Map(msg.entities.map(e => [e[0], [e[1], e[2], e[5], e[7] || 0, e[6] || 0, e[3] || 0]]));
    curTime = now;
  }

  // Interpolation factor 0..1 for render time `t` (seconds). 0 before the first snapshot.
  function alpha(t) {
    const interval = (curTime - prevTime) || SNAP_INTERVAL;
    return curTime ? Math.min(1, (t - curTime) / interval) : 0;
  }

  function send(obj) { if (ws && ws.readyState === 1) ws.send(JSON.stringify(obj)); }

  function connect() {
    onStatus && onStatus('agents: connecting…');
    ws = new WebSocket(`ws://${location.host}/ws`);
    ws.onopen = () => { onStatus && onStatus('agents: stream open, waiting…'); onOpen && onOpen(); };
    ws.onerror = () => onStatus && onStatus('agents: WebSocket error');
    ws.onmessage = (ev) => {
      const msg = JSON.parse(ev.data);
      if (msg.type === 'snap') { ingest(msg, performance.now() / 1000); onSnapshot && onSnapshot(msg); }
      else if (msg.type === 'detail') onDetail && onDetail(msg.detail);
      else if (msg.type === 'building') onBuilding && onBuilding(msg.building);
      // other frame types (e.g. the initial 'world' frame) are intentionally ignored — town geometry comes from /asset/town
    };
    ws.onclose = () => { onStatus && onStatus('agents: stream closed, retrying…'); setTimeout(connect, 1000); };
  }

  return {
    connect, send, ingest, alpha,
    get prevSnap() { return prevSnap; },
    get curSnap() { return curSnap; },
    get prevTime() { return prevTime; },
    get curTime() { return curTime; },
  };
}
