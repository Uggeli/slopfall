# Direction — canonical design decisions

The load-bearing decisions that constrain everything else. Keep this current; when a decision
changes, edit here.

## What we are building
A whole **Daggerfall region** simulated as a persistent, continuously-alive world — civilians with
needs, economy, relationships, memory, and emergent goals — running headless, with a renderer as an
optional consumer of sim state.

## Scope (2026-06-21)
- **One region at a time, at full fidelity.** Current tech runs Daggerfall-scale regions fine, so no
  LOD/sharding is required just to *run* a region. The Daggerfall region is ~8,700 km² — space is
  plentiful; even Betony is large.
- **Multiple regions, cross-region/cross-province trade & travel, and per-region servers are
  DEFERRED.** Inter-settlement work *within* the loaded region (e.g. caravans between its own towns)
  stays in scope.
- **Betony** is the first proving ground (rural/primary archetype, ~1–1.5k civ); the full hamlet↔city
  loop comes with the Daggerfall region later.

## Architecture principle — "don't LOD the life, LOD the body"
Split the sim along life-vs-embodiment, not spatial detail:
- **Life tier (always on, every agent):** economy, needs, social/gossip, health, demographics, trade.
  Cheap, and *this is the aliveness* — temporal continuity + persistent identity.
- **Embodied tier (only where an observer is):** movement, pathfinding, perception, collision.
  Expensive; safe to drop where unobserved because aliveness is temporal, not spatial.
Determinism (seeded RNG) lets an unembodied agent be re-derived and hydrated with a body consistent
with its life state. *(Two-tier split itself is future work — see TODOS "Tick & determinism" and
"Multiplayer".)*

## Quests are emergent, not scripted
Quests arise from **agent wants**: agents post jobs/contracts to each other and to guilds, who may or
may not fulfil them. The mechanism is a job/contract board on the directed-drives + economy layer —
**not** a port of Daggerfall Unity's scripted quest system. DFU quest content is at most a
flavour/data source.

## Player = a human-controlled agent
There is no special "player" subsystem. Attributes, skills, embodiment, equipment, combat, magic, and
afflictions are **agent** systems shared by NPCs and humans alike. The only genuinely player-specific
work is the **client** layer: input, camera/first-person view, UI, and a join/spawn flow.

## MMO by accident
Multiple human agents can connect simultaneously — multiplayer is already a current capability, not a
future port. Remaining client work is presentation/input, not netcode.

## Deferred (with reasons)
- **Time-skipping / time-compression** — a shared real-time multiplayer world can't fast-forward for
  one agent.
- **The scripted DF main quest** — runs against the emergent-quest direction.
- **Multi-region / sharding** — see Scope above.

## Economy stance (current)
Money enters a settlement by **exporting**; a town's general stores are its one outside trade edge and
local market hub. Food is local everywhere (farms/fisheries → larders). Settlement profiles
(hamlet/village/city) drive role/tax/guards/wealth. The unit of economic balance is the **region**, not
a single settlement (an isolated hamlet can't be a closed economy). See
[economy stage docs](design-docs-index.md).

## Honest-measurement rule
Don't trust promised effects or tuned numbers — **histogram what every agent actually does across a
day**. Past "famines" were behavioral scoring bugs (promised effects ≠ real effects), fixed with zero
tuning once measured honestly.
