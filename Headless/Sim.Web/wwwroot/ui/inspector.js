// ui/inspector.js — click-to-inspect: pick a person/building, show its live detail (R3).
// Mounted by the mode with the camera it owns + the net it owns; fed live by net's
// onDetail/onBuilding callbacks. Picking reads agents/town (engine) against the handed-in
// camera. It must not create the camera or OrbitControls — it's handed one.
import * as THREE from 'three';
import { renderer } from '../engine/scene.js';
import { agents } from '../engine/agents.js';
import { town } from '../engine/world.js';
import { clearSelRing } from '../engine/agents.js';

const ui = id => document.getElementById(id);

// Server needs.V axes, in order (NeedsRegistry.NeedAxis); each is a deficit 0..1.
const NEED_LABELS = ['hunger', 'tiredness', 'loneliness', 'poverty', 'goods', 'fear', 'attire'];

// Format the phase string from an inspect detail object, appending queue position when present.
// Server sends queuePos (-1 if not in a line) and queueServed (bool).
function phaseLabel(d) {
  let label = d.phase ?? '—';
  if (d.queuePos != null && d.queuePos >= 0) label += ` — in line (#${d.queuePos + 1})`;
  else if (d.queueServed) label += ` — at counter`;
  return label;
}

export function mountInspector({ net, camera }) {
  const raycaster = new THREE.Raycaster();
  const ptr = new THREE.Vector2();
  let selectedId = -1, selectedBuildingI = -1, inspectTimer = null, downX = 0, downY = 0;
  let agentTab = 'status', lastAgentDetail = null, oddCollapsed = new Set();

  // Distinguish a click from an orbit-drag: only pick if the pointer barely moved.
  renderer.domElement.addEventListener('pointerdown', e => { downX = e.clientX; downY = e.clientY; });
  renderer.domElement.addEventListener('pointerup', e => {
    if (e.button !== 0 || Math.hypot(e.clientX - downX, e.clientY - downY) > 5) return;
    ptr.x = (e.clientX / innerWidth) * 2 - 1;
    ptr.y = -(e.clientY / innerHeight) * 2 + 1;
    raycaster.setFromCamera(ptr, camera);
    const hitA = raycaster.intersectObjects(agents.children, false)[0];
    if (hitA && hitA.object.userData.id != null) { selectAgent(hitA.object.userData.id); return; }
    // No agent under the cursor: try a building. Recurse — region tiles are nested groups.
    const hitB = raycaster.intersectObjects(town.children, true)[0];
    if (hitB) { pickBuilding(hitB.point.x, hitB.point.z); return; }
    deselect();
  });
  addEventListener('keydown', e => { if (e.code === 'Escape') deselect(); });

  // Drop whatever is currently selected (agent or building) and stop watching.
  function clearSelection() {
    if (selectedId >= 0) net.send({ type: 'watch', id: null });
    selectedId = -1; selectedBuildingI = -1; lastAgentDetail = null; oddCollapsed.clear();
    clearInterval(inspectTimer); inspectTimer = null;
    clearSelRing();
  }
  function deselect() { clearSelection(); ui('detail').style.display = 'none'; }

  function selectAgent(id) {
    if (selectedId === id) return;
    clearSelection();
    selectedId = id; agentTab = 'status';
    ui('detail').style.display = 'block';
    ui('detail').innerHTML = `<div class="dh">#${id}…</div>`;
    net.send({ type: 'watch', id });                       // start ODD snapshotting this agent
    const ask = () => net.send({ type: 'inspect', id });
    ask();
    inspectTimer = setInterval(ask, 1000);             // keep the panel live while selected
  }

  function pickBuilding(x, z) {
    clearSelection();
    selectedBuildingI = -2;                            // pending (any non -1 marks "building mode")
    ui('detail').style.display = 'block';
    ui('detail').innerHTML = `<div class="dh">building…</div>`;
    net.send({ type: 'pickBuilding', x, z });
  }

  function renderDetail(d) {
    if (!d || d.id !== selectedId) return;   // stale (deselected or reselected since the ask)
    if (d.busy) return;                      // transient registry contention — keep last render
    lastAgentDetail = d;
    renderAgentPanel();
  }

  function renderAgentPanel() {
    const d = lastAgentDetail; if (!d) return;
    if (d.missing) {
      ui('detail').innerHTML = `<div class="dh"><span class="dx">×</span>#${d.id}</div><div class="dim">no longer here</div>`;
      wireAgent(); return;
    }
    let h = `<div class="dh"><span class="dx">×</span>#${d.id} ${d.name ?? ''}</div>`
      + `<div class="tabs"><span class="tab ${agentTab === 'status' ? 'active' : ''}" data-tab="status">status</span>`
      + `<span class="tab ${agentTab === 'odd' ? 'active' : ''}" data-tab="odd">ODD</span></div>`;
    if (agentTab === 'status') {
      h += `<div class="dim">${d.kind} · ${d.race} · lvl ${d.level}</div>`
        + `<div class="dim">activity: ${d.activity} (${phaseLabel(d)})</div>`
        + `<div class="dim">coin: ${(d.coin ?? 0).toFixed(2)}</div>`
        + (d.spouse >= 0 ? `<div class="dim">spouse: <span class="dlink" data-id="${d.spouse}">#${d.spouse}</span></div>` : '');
      if (Array.isArray(d.needs)) {
        h += `<div class="ndh">needs</div>`;
        for (let i = 0; i < NEED_LABELS.length; i++) h += needBar(NEED_LABELS[i], d.needs[i]);
      }
    } else {
      h += renderOdd(d.odd);
    }
    ui('detail').innerHTML = h;
    wireAgent();
  }

  // Reconstruct the tree from the flat nodes[] via parent links. Node 0 is the root
  // sentinel; its children are the root-level actions. Children sort by Total desc.
  function renderOdd(odd) {
    if (!odd || !Array.isArray(odd.nodes) || odd.nodes.length < 2)
      return `<div class="dim">no decision captured yet…</div>`;
    const kids = {};
    odd.nodes.forEach((n, i) => { if (i === 0) return; (kids[n.parent] ||= []).push(i); });
    let out = `<div class="dim">decided ${odd.ts ?? '—'}</div><div class="odd">`;
    const walk = (idx, depth) => {
      const cs = (kids[idx] || []).slice().sort((a, b) => odd.nodes[b].total - odd.nodes[a].total);
      for (const ci of cs) {
        const n = odd.nodes[ci];
        const hasKids = !!kids[ci];
        const collapsed = oddCollapsed.has(ci);
        const caret = hasKids ? `<span class="tcaret" data-idx="${ci}">${collapsed ? '▸' : '▾'}</span> ` : '• ';
        const mark = ci === odd.chosen ? ' ◄' : '';
        out += `<div class="tnode${ci === odd.chosen ? ' chosen' : ''}" style="padding-left:${depth * 12}px">`
          + `<span>${caret}${n.verb}${mark}</span>`
          + `<span class="sc">${n.direct.toFixed(2)} / ${n.prop.toFixed(2)} / <b>${n.total.toFixed(2)}</b></span>`
          + `</div>`;
        if (hasKids && !collapsed) walk(ci, depth + 1);
      }
    };
    walk(0, 0);
    return out + `</div>`;
  }

  function renderBuilding(b) {
    if (selectedBuildingI === -1) return;   // deselected since the request
    if (b.busy) return;
    selectedBuildingI = b.i ?? -2;
    if (b.missing) {
      ui('detail').innerHTML = `<div class="dh"><span class="dx">×</span>building</div><div class="dim">none here</div>`;
      wireBuilding(); return;
    }
    let h = `<div class="dh"><span class="dx">×</span>${b.name ?? b.kind}</div>`
      + `<div class="dim">${b.kind}${b.production ? ' · ' + b.production : ''} · q${b.quality}</div>`;
    if (b.settlement)
      h += `<div class="dim">${b.settlement.name} (${b.settlement.kind}) · pop ${b.settlement.residents} · treasury ${Math.round(b.settlement.treasury)}</div>`;
    if (Array.isArray(b.residents) && b.residents.length) {
      h += `<div class="ndh">residents</div>`;
      for (const r of b.residents)
        h += `<div class="dim">• <span class="dlink" data-id="${r.id}">${r.name ?? '#' + r.id}</span> <span class="role">${r.role}</span></div>`;
    }
    if (Array.isArray(b.workers) && b.workers.length) {
      h += `<div class="ndh">workers</div>`;
      for (const w of b.workers)
        h += `<div class="dim">• <span class="dlink" data-id="${w.id}">${w.name ?? '#' + w.id}</span></div>`;
    }
    ui('detail').innerHTML = h;
    wireBuilding();
  }

  function wireAgent() {
    const p = ui('detail');
    const x = p.querySelector('.dx'); if (x) x.onclick = deselect;
    p.querySelectorAll('.tab').forEach(t => t.onclick = () => { agentTab = t.dataset.tab; renderAgentPanel(); });
    p.querySelectorAll('.dlink').forEach(el => el.onclick = () => selectAgent(Number(el.dataset.id)));
    p.querySelectorAll('.tcaret').forEach(el => el.onclick = () => {
      const i = Number(el.dataset.idx);
      if (oddCollapsed.has(i)) oddCollapsed.delete(i); else oddCollapsed.add(i);
      renderAgentPanel();
    });
  }
  function wireBuilding() {
    const p = ui('detail');
    const x = p.querySelector('.dx'); if (x) x.onclick = deselect;
    p.querySelectorAll('.dlink').forEach(el => el.onclick = () => selectAgent(Number(el.dataset.id)));
  }
  function needBar(label, v) {
    v = Math.max(0, Math.min(1, v || 0));   // deficit 0..1 (drift can exceed 1; clamp the bar)
    const pct = Math.round(v * 100);
    const col = v < 0.4 ? '#5cc77a' : v < 0.7 ? '#d4b04a' : '#c75c5c';   // green→amber→red as it worsens
    return `<div class="nb"><span>${label}</span><span class="nbar"><i style="width:${pct}%;background:${col}"></i></span></div>`;
  }

  return { renderDetail, renderBuilding, deselect, get selectedId() { return selectedId; } };
}
