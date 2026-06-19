namespace DaggerfallWorkshop.Sim.Host.Render
{
    /// The single-page region viewer, served at GET /. Fetches /static once to draw
    /// the walkability grid + buildings, then streams /stream (SSE) to plot agents.
    public static class ViewerPage
    {
        public const string Html = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>slopfall — region viewer</title>
<style>
  html,body{margin:0;height:100%;background:#0b0d10;color:#cdd6e0;font:12px/1.4 ui-monospace,Menlo,Consolas,monospace;overflow:hidden}
  #c{position:absolute;inset:0;cursor:grab}
  #c.drag{cursor:grabbing}
  #hud{position:absolute;top:8px;left:8px;background:rgba(12,15,20,.82);border:1px solid #2a3340;border-radius:6px;padding:8px 10px;min-width:190px;pointer-events:none}
  #hud b{color:#fff}
  .row{display:flex;justify-content:space-between;gap:12px}
  #hist{margin-top:6px;border-top:1px solid #2a3340;padding-top:5px}
  .bar{display:flex;align-items:center;gap:5px;margin:1px 0}
  .sw{width:9px;height:9px;border-radius:2px;flex:none}
  #bar{position:absolute;top:8px;right:8px;display:flex;gap:6px}
  button{background:#1a2230;color:#cdd6e0;border:1px solid #34404f;border-radius:5px;padding:5px 9px;font:inherit;cursor:pointer}
  button:hover{background:#243044}
  button.on{background:#2f6df0;border-color:#2f6df0;color:#fff}
  #help{position:absolute;bottom:6px;left:8px;color:#6b7686}
</style>
</head>
<body>
<canvas id="c"></canvas>
<div id="hud">
  <div class="row"><span id="loc">loading…</span></div>
  <div class="row"><span>date</span><b id="date">—</b></div>
  <div class="row"><span>time</span><b id="time">—</b></div>
  <div class="row"><span>weather</span><b id="wx">—</b></div>
  <div class="row"><span>population</span><b id="pop">—</b></div>
  <div class="row"><span>tick</span><b id="tick">—</b></div>
  <div id="hist"></div>
</div>
<div id="bar">
  <button id="pp">⏸ pause</button>
  <button data-spd="40">slow</button>
  <button data-spd="200" class="on">normal</button>
  <button data-spd="900">fast</button>
  <button data-spd="4000">ultra</button>
</div>
<div id="help">drag = pan · wheel = zoom · F = fit</div>
<script>
const cv = document.getElementById('c'), ctx = cv.getContext('2d');
let W = 0, H = 0;            // grid cells
let cell = 1.6;             // metres per cell
let backdrop = null;        // offscreen ImageBitmap of the walkability grid
let buildings = [];
let cam = { x: 0, y: 0, s: 1 };   // screen = (cellX*s + x, cellY*s + y)
let snap = null;

// activity → colour
const ACT = {
  Sleep:'#3b5bdb', Wander:'#9aa6b2', Idle:'#5a6472',
  Farm:'#3fb950', Fish:'#2bb3c0', Mine:'#b08948', Weave:'#c678dd',
  Work:'#e3b341', Labor:'#d9863a', EatHome:'#7ee787', EatTavern:'#e3b341',
  Socialize:'#ff7b9c', Visit:'#ff9ec4', Chat:'#ff7b9c', Buy:'#58d6a0',
  Beg:'#8b7355', Steal:'#d2691e', Flee:'#ff5555', Attack:'#ff2d2d',
  UseItem:'#9aa6b2', StoreItem:'#9aa6b2'
};
const actColor = a => ACT[a] || '#cdd6e0';

// building kind → colour bucket
function bColor(k){
  if(k==='Tavern') return '#e3b341';
  if(k==='Temple') return '#36c5d0';
  if(k==='Bank') return '#58d6a0';
  if(k==='Farm') return '#2f6b32';
  if(k==='Fishery') return '#1f6b73';
  if(k==='Mine') return '#6b5330';
  if(k==='Pasture') return '#4a6b2f';
  if(k==='Weaver') return '#7d4fa0';
  if(k==='Palace'||k==='HouseForSale') return '#7a8290';
  return '#39424f';
}

function resize(){ cv.width = innerWidth; cv.height = innerHeight; }
addEventListener('resize', resize); resize();

function fit(){
  if(!W) return;
  const s = Math.min(cv.width / W, cv.height / H) * 0.95;
  cam.s = s;
  cam.x = (cv.width  - W * s) / 2;
  cam.y = (cv.height - H * s) / 2;
}

async function boot(){
  const st = await (await fetch('/static')).json();
  W = st.gridWidth; H = st.gridHeight; cell = st.cellSize;
  buildings = st.buildings || [];
  document.getElementById('loc').textContent = st.regionName + ' — ' + (st.settlements?.length||0) + ' settlements';
  // paint the walkability grid into an offscreen bitmap (1px per cell)
  if(st.costBase64){
    const raw = atob(st.costBase64);
    const img = ctx.createImageData(W, H);
    for(let i=0;i<raw.length;i++){
      const walk = raw.charCodeAt(i) !== 0;
      const o = i*4;
      img.data[o]   = walk?28:14;
      img.data[o+1] = walk?34:17;
      img.data[o+2] = walk?42:21;
      img.data[o+3] = 255;
    }
    backdrop = await createImageBitmap(img);
  }
  fit();
  const es = new EventSource('/stream');
  es.onmessage = e => { snap = JSON.parse(e.data); hud(); };
  requestAnimationFrame(draw);
}

function draw(){
  ctx.fillStyle = '#0b0d10';
  ctx.fillRect(0,0,cv.width,cv.height);
  const {x,y,s} = cam;
  // backdrop grid
  if(backdrop){
    ctx.imageSmoothingEnabled = false;
    ctx.drawImage(backdrop, x, y, W*s, H*s);
  }
  // buildings
  const bs = Math.max(2, s*1.4);
  for(const b of buildings){
    const px = x + (b.x/cell)*s, py = y + (b.z/cell)*s;
    ctx.fillStyle = bColor(b.kind);
    ctx.fillRect(px - bs/2, py - bs/2, bs, bs);
  }
  // agents
  if(snap){
    const r = Math.max(1.5, s*0.5);
    for(const a of snap.agents){
      const px = x + (a.x/cell)*s, py = y + (a.z/cell)*s;
      const monster = a.kind==='EnemyMonster' || a.kind==='EnemyClass';
      ctx.beginPath();
      ctx.arc(px, py, monster?r*1.4:r, 0, 6.283);
      ctx.fillStyle = monster ? '#ff2d2d' : actColor(a.activity);
      ctx.fill();
    }
  }
  requestAnimationFrame(draw);
}

function hud(){
  if(!snap) return;
  const p2 = n => String(n).padStart(2,'0');
  document.getElementById('date').textContent = `${p2(snap.day+1)}/${p2(snap.month+1)}/${snap.year}`;
  document.getElementById('time').textContent = `${p2(snap.hour)}:${p2(snap.minute)} ${snap.isNight?'🌙':'☀'}`;
  document.getElementById('wx').textContent = snap.weather;
  document.getElementById('pop').textContent = snap.population;
  document.getElementById('tick').textContent = snap.tick.toLocaleString();
  // histogram
  const counts = {};
  for(const a of snap.agents) counts[a.activity] = (counts[a.activity]||0)+1;
  const rows = Object.entries(counts).sort((a,b)=>b[1]-a[1]);
  document.getElementById('hist').innerHTML = rows.map(([k,v])=>
    `<div class="bar"><span class="sw" style="background:${actColor(k)}"></span>${k}<span style="margin-left:auto">${v}</span></div>`
  ).join('');
}

// ---- camera controls ----
let drag = null;
cv.addEventListener('mousedown', e => { drag = {x:e.clientX, y:e.clientY, cx:cam.x, cy:cam.y}; cv.classList.add('drag'); });
addEventListener('mouseup', () => { drag = null; cv.classList.remove('drag'); });
addEventListener('mousemove', e => { if(!drag) return; cam.x = drag.cx + (e.clientX-drag.x); cam.y = drag.cy + (e.clientY-drag.y); });
cv.addEventListener('wheel', e => {
  e.preventDefault();
  const f = e.deltaY < 0 ? 1.12 : 1/1.12;
  const mx = e.clientX, my = e.clientY;
  cam.x = mx - (mx - cam.x) * f;
  cam.y = my - (my - cam.y) * f;
  cam.s *= f;
}, {passive:false});
addEventListener('keydown', e => { if(e.key==='f'||e.key==='F') fit(); });

// ---- sim controls ----
const pp = document.getElementById('pp');
let paused = false;
pp.onclick = async () => {
  paused = !paused;
  await fetch('/control?cmd=' + (paused?'pause':'play'));
  pp.textContent = paused ? '▶ play' : '⏸ pause';
  pp.classList.toggle('on', paused);
};
for(const btn of document.querySelectorAll('[data-spd]')){
  btn.onclick = async () => {
    await fetch('/control?cmd=speed&v=' + btn.dataset.spd);
    document.querySelectorAll('[data-spd]').forEach(b=>b.classList.remove('on'));
    btn.classList.add('on');
  };
}

boot();
</script>
</body>
</html>
""";
    }
}
