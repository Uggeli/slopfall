# MSxx — Living Locations (functional nodes, not loot boxes)        [world] + [infra]

Goal: every off-settlement location earns its existence by being a SOURCE, SINK, or RIVAL in a flow
the rest of the world depends on. DF's ~19 dungeon art-types collapse onto ~5 functional node-roles
sharing one substrate; remove the respawn-timer loot-piñata model entirely. The payoff: a dungeon
roster the sim authors from its own history rather than from hand placement.

## Depends on (gate before digging in)

Hard blockers — the layer cannot function without these:
- [bug] Build fix (RenderSnapshot). Same P0 gate as everything else.
- [infra] Location-as-stateful-node registry. Dungeons/wilderness must exist as addressable sim
  entities with per-node state (population / stock / faction / coupling edge). Settlements load today;
  off-settlement nodes-with-state is new. Foundational — nothing below works without it.
- [absent] Work-spot / location-resolution registry. Resource nodes need marked spots (mine
  entrances, farmland, fishing tiles). This is your existing "jobs perform at the wrong place" item;
  resource nodes are blocked on it directly.

Strong deps — needed for the core loop, minimal versions OK:
- [absent] Production recipes / industry layers. A resource node is purposeless without a downstream
  consumer. Minimal = ore as a raw good with one consumer (smithing).
- [partial] Creatures & combat. Predator habitats need real creature *populations*, not "3 in Betony,
  ~1 HP bites". Coarse population + minimal AI is enough; deep tactical combat is not required here.
- [absent] Caravans / travel + coarse wilderness-road nav. Rival bases raid caravans and spill onto
  roads; without an inter-settlement layer there's nothing to raid and no road to threaten.
- [partial] Factions. Rival bases, covens, and bandit camps ARE factions — shares the gods milestone's
  faction gate.
- [partial] Lifecycle. Death works (crypts-as-sink function now); birth/reproduction gates the
  demographic feedback where predators thin a population that then recovers.

Soft deps — gate specific node-roles or the full payoff, NOT the MVP:
- [world] World Chronicle (exists). The ruins-from-history generator (L6) reads recorded
  depopulation/faction-death/temple-collapse out of it. Gated on TIME + those systems running, not on
  building the Chronicle.
- Fencing / stolen-goods => rival hoards traceably made of robbed goods (richer with it, works without).
- [absent] Gossip => threat-knowledge spreads ("that road is dangerous"). Soft.
- [partial] S4 Conscience taboo => the necromancy taboo-flip on death sinks (L5).
- Gods layer (sister milestone) => covens as Daedric champion-recruitment, desecrated temples as dead
  Aedra-nodes. Locations can ship first; these get richer once gods exist.

## Stages

- [infra] L0 — Prereq gate. Build fix; land the node registry (L1's substrate is real and testable).
  No node-role code until green.
- [world] L1 — Node-role substrate (kernel all 5 roles specialize). A node carries: a role tag, a
  population/stock state, a coupling edge to the settlement sim (source | sink | threat), and a coarse
  update tick. Done = a generic node updates its state and a settlement reads its coupling edge.
- [world] L2 — Resource nodes (ship first, cheapest clear payoff). Mine / caves / forest produce raw
  goods into the economy; depletion + slow regen; worked by agents via the work-spot registry. Done =
  a mine produces ore consumed downstream, and depleting it moves a price signal in town.
- [world] L3 — Predator habitats. Creature populations with coarse carrying-capacity + spillover onto
  farmland/roads; finally makes the fear drive visible. Done = a nest grows, spills onto a road so
  agents route around it / draw a guard response, and culling crashes-then-recovers it on a modeled
  curve — NO respawn timer.
- [world] L4 — Rival-faction bases. Hostile agent populations that raid caravans/farms and hoard loot
  that is traceably robbed settlement goods; coupled antagonist (needs the towns; towns need defense).
  Coven = the divine/recruitment variant. Done = a bandit base raids a caravan, its hoard provenances
  back to stolen goods, and sustained town pressure can collapse it.
- [world] L5 — Death & decay nodes. Crypts/cemeteries = the lifecycle death-sink + Arkay's domain made
  physical; vampire haunt = predator-on-population. The twist: a necromancy taboo-violation flips a
  sink into a predator habitat (reuses L3). Done = sim dead are interred at a crypt, and a taboo
  violation converts it into a predator node.
- [world] L6 — Ruins-from-history generator (the payoff). Chronicle-recorded deaths — depopulated
  towns, wiped factions, collapsed temples — instantiate as ruin/decay nodes carrying their residue
  (former goods, their dead, faction remnants). Done = a town that depopulates IN-SIM leaves a
  raidable ruin node with no hand placement.

## Guardrails (acceptance constraints, not polish)

- The source/sink/rival test: if deleting a node changes NOTHING in any settlement, it's content, not
  a node. Cut it or couple it.
- Ecology stays COARSE — carrying capacity + slow recovery + spillover, never Lotka-Volterra. No
  extinction-cascade tuning rabbit hole unless you elect that as its own project.
- No respawn timers. A cleared node stays cleared until repopulated by the same modeled dynamics that
  filled it (growth from survivors / migration), or stays dead.
- Loot is provenanced. Everything in a hoard came from somewhere — robbed, mined, crafted. Nothing
  spawns from void. (Same discipline as "no conjured coin".)
- Node function precedes interior. The node simulates (produces, raids, spills) as a pure sim entity;
  the walkable 3D crawl is a separate, later layer and must never block node logic.

## Deferred within this milestone
- 3D walkable dungeon interiors / the crawl layer (rendering [absent]) — separate concern.
- Cross-region resource/faction flows ([deferred: multi-region] per current scope).
- Deep per-creature tactical AI (coarse population suffices here).
- Macro economic propagation (boom/bust from a choked mine) — basic price signal here; cycles ride the
  economy milestone.