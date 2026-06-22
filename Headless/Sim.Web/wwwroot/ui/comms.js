// ui/comms.js — the comms log (The Renderer, R3, P4).
// Structured utterances off the snapshot stream, rendered as a minimal scrolling feed.
// Atom ids -> concept names via a one-time /asset/atoms fetch. No engine/net deps — the
// shell's onSnapshot hands us msg.utterances.
let atomNames = {};   // "6001" -> "Danger"
fetch('/asset/atoms').then(r => r.json()).then(t => { atomNames = t || {}; })
  .catch(() => { /* names optional — fall back to raw ids */ });

const CHANNEL_GLYPH = { Whisper: '·', Talk: '–', Shout: '⟶' };
const commLines = [];               // recent rendered lines (newest last)
const COMM_MAX = 10;                // keep the panel small

function atomName(id) { return atomNames[String(id)] || ('#' + id); }

function utterText(u) {
  // "#42 ⟶ shout: Danger,Provisions @b7" — speaker, channel glyph, act, content, subject.
  const glyph = CHANNEL_GLYPH[u.channel] || '–';
  const body = (u.content && u.content.length)
    ? u.content.map(c => atomName(c.atom)).join(', ')
    : (u.act || '');
  const subj = (u.subject != null && u.subject >= 0) ? ` @b${u.subject}` : '';
  return `#${u.speaker} ${glyph} ${(u.channel || '').toLowerCase()}: ${body}${subj}`;
}

export function pushUtterances(list) {
  if (!list || !list.length) return;
  for (const u of list) commLines.push(utterText(u));
  while (commLines.length > COMM_MAX) commLines.shift();
  const el = document.getElementById('commlines');
  if (el) {
    el.textContent = '';
    for (const line of commLines) {
      const div = document.createElement('div');
      div.textContent = line;
      el.appendChild(div);
    }
    el.parentElement.scrollTop = el.parentElement.scrollHeight;   // follow the tail
  }
}
