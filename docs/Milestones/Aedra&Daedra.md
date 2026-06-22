# MSxx — Divine Layer (Aedra / Daedra)        [world] (+ [client] inspector)

Goal: gods as two distinct entity-kinds, both expressed ONLY through effects on mortals — never
authored plot. Daedra = sparse optimizers acting through champions; Aedra = deified subsystems with
temples supplying all the agency. Metaphysical ambiguity falls out of the build: from inside the
world a hollow god (Aedra) and an inhabited one (Daedra) are indistinguishable; only the inspector
can tell them apart.

## Depends on (gate before digging in)

Hard blockers — the layer cannot function without these:
- [bug] Build fix (RenderSnapshot). Nothing builds/tests until this lands. P0, unrelated to gods but
  blocks all new work.
- [partial] S3 Meanings consolidation / awake-sleep fold. The dream-injection channel writes into the
  fold (false memory, valence shift, drive nudge); today this is *proto only*. THIS IS THE CRITICAL
  PATH — no fold, no divine channel.
- [absent] Factions. Temples *are* factions. No membership primitive => no temple institution, no
  Aedra surface at all.

Strong deps — needed for the core loop, minimal versions OK:
- [partial] Drive-marketplace injection seam: an exogenous bias term the channel can write ("a
  calling"). Marketplace exists; needs a documented seam for outside nudges.
- [partial] Per-agent trajectory readout (acts / drive history / OCEAN) for champion bidding. Rides
  the perception-memory-scoring pipeline; needs an aggregate-per-agent surface.
- [partial] S4 Conscience sacred-edge + norm installer. Required for devotion/zealotry and norm-acting
  gods. Struct exists; only begging-shame seeded today.

Soft deps — gate specific gods or the full payoff, NOT the MVP:
- [absent] Gossip / rumor spread => worship contagion, prophets, doctrine drift, champion reputation.
  Ship the channel without it; the emergent-religion payoff waits on it.
- [partial] Lifecycle reproduction/birth => Mara (attachment), full Arkay death/birth cycle,
  generational doctrine transmission.
- [absent]/[partial] Economy depth (production chains, real shop commerce) => Zenithar + economic gods.
- [partial] Culture-into-memory / norm drift => heresy + schism between temples of one god.

## Stages

- [infra] D0 — Prereq gate. Land the build fix; confirm fold + faction primitives are real and
  testable. No divine code until green.
- [world] D1 — Influence substrate (kernel both kinds share). Deterministic slow god-tick (seeded,
  runs on the fold pass, not per sim-tick). An influence-budget primitive (scarce spend). The write
  channel: inject false memory / valence shift / drive-bias into a target's consolidation. Done = a
  test god nudges one agent's next-day behavior reproducibly, within budget.
- [world] D2 — Aedra (cheap, ship first). Deified-subsystem registry mapping god -> an existing
  running mechanic (Arkay=death/birth, Mara=attachment, Zenithar=market...). Ambient regional pressure
  = weak salience/drive bias proportional to local worship (a tropism, no directives). Temple = an
  emergent faction that crystallizes around high-salience agents and offers real utility
  (healing/blessing/contracts). Ship only with gods whose subsystem already runs (Arkay works now;
  Mara/Zenithar gated on soft deps). Done = an Arkay temple forms unprompted in a death-salient town,
  recruits, and offers services, with NO god-entity in the codebase.
- [world] D3 — Daedra entities (the expensive, interesting part). A god = motive (a scalar over its
  domain-*projection* of world-state, not the whole world) + influence budget + champion lifecycle:
  select -> feed -> reap -> lose -> re-select. Selection BIDS for the mortal already trending toward
  the motive (no hand-assignment). Feeding = preferential use of the D1 channel on the champion +
  optional empowerment (artifact = routed budget). Done = a champion is selected emergently, fed, its
  worldly acts measurably move the motive; kill it and the god re-selects.
- [world] D4 — Pantheon proxy-game (payoff). Multiple Daedra with conflicting motives reallocate
  budget toward whichever champions are winning; the clash is logged to the World Chronicle as a named
  feud/era. Full effect gated on gossip. Done = two conflicting gods produce a champion clash +
  Chronicle entry with no scripted trigger.
- [client] D5 — Divine inspector. God's-eye / belief overlay: per-god domain view + budget + current
  champion; per-agent "is anyone home behind the god you worship?" readout. The only place the
  hollow-vs-inhabited distinction is visible. Done = watch a Daedra select/feed/lose a champion, and
  confirm an Aedra temple has no entity behind it.

## Guardrails (acceptance constraints, not polish)

- Motive over *texture*, not counters. "Maximize fear-events" min-maxes into one nightmare-factory
  agent. Motives must target a distribution/quality of world-state, satisfiable only diffusely. Treat
  as a reward-hacking risk.
- Champion selection EMERGENT, never assigned. The god bids for who's already becoming its thing; the
  prophecy makes the prophet. Any hand-pick is a regression.
- The god plants a seed in the substrate; it does NOT drive the car. Influence writes to
  memory/valence/bias/norm, then normal cognition runs. A god setting a drive directly or scripting an
  act = emergence gone, authored plot back.
- Both kinds act ONLY through effects. No god is ever directly perceivable by an agent or the world;
  existence is inferred from drift + champion-events alone.

## Deferred within this milestone
- Generational transmission of doctrine (needs reproduction).
- Economic/commerce gods at full strength (needs production chains).
- Schism/heresy as a tracked phenomenon (needs culture-drift).
- Apotheosis (a mortal taking an Aedra slot, à la Talos) — interesting, out of scope here.