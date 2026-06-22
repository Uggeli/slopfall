# TODOS — backlog

Flat inventory of everything missing or broken. Tags: `[bug]` broken behavior, `[absent]`
designed/expected but no code, `[partial]` exists but incomplete, `[stub]` placeholder only.
No priority order — this is an inventory, not a plan.

**Scope (current decision, 2026-06-21):** the unit of simulation is ONE region, run at full
fidelity — current tech runs Daggerfall and other big regions fine, so no LOD/sharding is needed
just to run them. Multiple regions at once, cross-region/cross-province trade & travel, and
per-region servers are DEFERRED. Inter-settlement features *within* a single region (e.g. caravans
between that region's own towns) stay in scope. Items below tagged `[deferred: multi-region]` are
out of the current single-region target.

## Build & infra
- [infra] Sim.Tests deleted (2026-06-22): stale since the CQRS rewrite (f385dd848 removed the old serial core). Sources remain in git history; a CQRS test-port is a future option when the new execution path stabilises. Live coverage: Sim.MemoryTests (184) + Sim.SpatialTests (13).

## Spatial layer (root of several listed bugs)
- [absent] Work-spot / location-resolution registry. Jobs target the *employer building center*, so
  fishers fish at the house, farmers farm on the doorstep, miners mine in the street. Need marked
  spots: water/fishing tiles, farmland cells, mine entrances, market stalls, guard posts.
  (Covers: "npcs fishing at their houses not at water", "jobs aint performing at right places".)
- [absent] Water bodies — no water geometry or registry at all. Blocks fishing spots *and* water
  rendering.
- [absent] Spatial occupancy / separation / steering. `OccupancyRegistry` is social-only; no
  collision/avoidance, so npcs stack on top of each other. Needs separation / local avoidance /
  steering behaviors.
- [bug] NPCs wander but don't actually move. Pathfinding works (MovementSystem + TownPathfinder);
  likely a PositionSetIntent publish/apply or render-sync gap. Needs a concrete repro.
- [bug] Fractional coins: `CoinRegistry` stores amounts as `double` (e.g. 9.99). Daggerfall gold is
  whole. Decide integer coins, or round at display + transaction boundaries.

## ODD / planning
- [partial] Multi-step planning. OddTree is wired into `OddSystem.Decide` but shallow (depth-1
  chains). Missing: real Enables-DAG on actions (work -> buy -> cook), Goals/Beacons (O3),
  smart-object flags (O4), fire-early scheduler (O5).
- [partial] Directed person-ads (L4). Chat/SeekHelp still *interrupt* rather than compete as scored
  ads in the marketplace; `RelationFactor` is ready to score targets.

## Economy & production
- [absent] Production recipes / industry layers. Goods spawn from void — no input->output chains, no
  raw/material/finished tiers, no crafting supply chains, no B2B production.
- [partial] Shop commerce. Buy is not wired to real stock for non-tavern shops; keepers earn
  "conjured" coin because non-tavern shops have no real customers.
- [absent] Regional finance (E3): property/ownership, rent collection, real taxation/treasury
  payroll, off-map import/export trade edges.
- [partial] Subsistence loop. Larder + EatHome + Steal work, but hunger -> provisions -> coin
  pressure isn't fully closed (CoinDef still reads as `1 - coin`, not driven by hunger).

## Cognitive & social depth (design docs)
- [partial] S3 Meanings: proto only (CategoryNode signature/valence/confidence). Missing
  consolidation: delta-bag, MINT/SETTLE, surprise-gated encoding, false memory, individual
  prototypes, full awake/sleep fold.
- [partial] S4 Conscience: data structure exists, only begging-shame seeded. Missing learned norm
  installer (altruistic punishment -> consolidation), guilt pole + reparation, social-standing pole,
  taboo hard-cull, sacred edge, crime-as-norm.
- [absent] Gossip / rumor spread. Every agent re-learns independently; no knowledge distribution
  across the population.
- [absent] Crime witnessing & enforcement. Theft/Attack charge guilt but there are no witnesses, no
  guard response, no public deterrent.
- [partial] S1/L3 membrane: `interpret()` is collapsed into the decision path (decision-driven lens)
  rather than a full per-tick SubjectiveView pipeline.

## Lifecycle & population
- [partial] Lifecycle. Aging -> mortality -> death -> despawn work. Missing: birth/reproduction,
  generational inheritance, aging-driven skill/personality drift. Population replacement is
  immigration-only (no net growth).

## Factions, reputation, skills, magic, combat
- [absent] Factions & reputation. Only pairwise Familiarity/Regard exists; no faction/guild
  membership, no faction-level standing, no reputation penalties for crimes.
- [partial] Skills & leveling. XP/advancement engine works but there is no defined skill list and no
  UI; `TrainSkill` is reserved vocabulary only.
- [absent] Spell casting / magic. Effects lifecycle + Magicka vitals exist, but `CastSpell` is
  reserved only — no spell definitions, casting mechanics, spellbook, spell lists, spell icons, or
  spell VFX.
- [partial] Creatures & combat. CreatureSystem + CombatSystem + HealthSystem exist but creatures are
  sparse placeholders (3 in Betony, ~1 HP bites). No per-creature AI, no creature pathfinding, no
  threat-at-distance — so the fear drive is rarely visible.

## Items & inventory
- [partial] Item affordances. Items have ownership/location/theft-guilt but don't advertise verbs (no
  Edible->Eat ad generation; items aren't in the ad-discovery pipeline). Missing: fuller atom vocab
  (perishable, quality, material, openable, readable, burnable...), stacking, containers, equipping,
  crafting, weight/encumbrance.

## Inter-settlement / regional
- [absent] Caravans & travel. No inter-settlement routes or trade; pathfinding is intra-town only.
  Discrete settlements load but nothing bridges them at runtime.

## Rendering & viewer
- [absent] Water shader/mesh — no water surface rendering.
- [absent] Building interiors / doors / stairs — exterior block geometry only; no interior meshes,
  door-opening, or vertical navigation.
- [absent] Post-processing — no bloom, tone mapping, or AA in the web viewer.
- [stub] Seasons — season flag is carried (0=normal) but no visual swap (textures/buildings don't
  change).
- [partial] Flat animations. Nature/prop billboards are static (no wind/sway). NPC sprites *are*
  animated and yaw/activity-aware; static civilians fall back to a single idle frame.

## GUI / inspect
- [partial] Settlement/house inspect — settlement info only shows nested inside the building detail
  panel; no dedicated settlement panel, no house interior view.
- [absent] Spell UI (icons, spell lists), minimap, building cutaway/floor view.

---

# Stock Daggerfall features missing from the sim

Tags here: `[world]` a living-world/sim system; `[agent]` a system every agent has — NPCs and
human-controlled players alike (the player is just a human-controlled agent, so these are NOT
player-only); `[client]` the human-control + presentation layer (input, camera/first-person view,
UI) that lets a human drive an agent. Multiple humans can connect at once — multiplayer/MMO is
already a current capability, so `[client]` work is presentation/input + a join/spawn flow, not
building netcode. (This `[agent]`/`[client]` split supersedes the coarser `[player]` tag.)

Almost everything below has a working **DFU reference implementation already vendored in-repo** under
`Assets/Scripts/Game/` and `Assets/Game/Addons/` (Daggerfall Unity) — but DFU's quests, combat, and
character logic are *scripted / player-centric*, whereas we want the **emergent** equivalents driven
by agent wants. So DFU is an algorithm + content reference, not a system to port wholesale into the
sim (`Assets/Sim/`, `Headless/`). The sim today only touches a few stock
concepts: `OwnerId` (court/knightly owners), DF building/POI kinds (temple, ship, potion shop...),
a disease event stub in `SimEvents.cs`, and temple alms in `RequestSystem`.

## Factions, guilds & reputation
- [world] Guild organizations: Mages Guild, Fighters Guild, Thieves Guild, Dark Brotherhood, the
  eight divine Temples, knightly orders, witch covens, vampire bloodlines. Membership, ranks,
  promotion/expulsion.
- [world] Guild services: skill training, spell buying, healing/curing, item identification, magic
  item lending, library/teleport, daedra summoning.
- [world] The full DF faction web (hundreds of factions: regional rulers, nobles, the Underking,
  etc.) with inter-faction relations and per-faction reputation. (Sim has only pairwise NPC
  Familiarity/Regard.)

## Banking & property
- [world] Banks: deposits, withdrawals, loans, interest, letters of credit, default/repossession,
  regional bank reputation. (DFU: `DaggerfallBankManager.cs`, `LoanChecker.cs`.)
- [world] Property ownership: buying/renting houses and ships, taxes/upkeep, storage in owned
  property. (Ties into the regional-finance [E3] item above.)

## Crime, law & enforcement
- [world] Reaction rolls / arrest: guards respond to witnessed crime, attempt arrest, escalate to
  combat. (Crime-witnessing was already listed under social depth — this is the DF-specific law
  layer on top.)
- [world] Courts & justice: regional court, fines vs. prison sentences, bail, loyalty/legal
  reputation hit, banishment.
- [world] Knightly orders / bounty as the legitimate-violence faction layer.

## Disease, affliction & status
- [world] Diseases: full DF disease roster with incubation/progression/attribute damage; poison;
  curing at temples; blessings/curses. (Sim has only a disease event stub.)
- [world] Vampirism & lycanthropy as NPC afflictions: infection -> transformation, clans/bloodlines,
  feeding/hunting drive, curing questline. (DFU: `LycanthropyInfection.cs`.) Applies to any agent —
  NPC or human-controlled.

## Quests & talk
- [world]/[agent] Emergent quests (NOT scripted): quests arise from agent wants — agents post
  jobs/contracts to each other and to guilds, who may or may not fulfil them. Need a job/contract
  board: posting, claiming, fulfilment, and reward + reputation settlement on completion or default.
  Sits on the directed-drives (L4) + economy layer, not a port of DFU's scripted Questing system
  (DFU quest content is at most a flavour/data source).
- [world] Talk/rumor system: topic graph (location/person/work/rumor), reaction modifiers
  (etiquette/streetwise), spreading rumors, anonymous messages. (DFU: `TalkManager.cs`.)

## Services & commerce
- [world] Shop services: repair, item identify, alchemist potion-making, mercantile haggling, shop
  quality tiers, type-specific stock (weapons/armor/clothing/general/bookseller/etc.).
- [world] Taverns/inns: rent a room to sleep, buy food & drink, hear rumors, pick up work, anonymous
  messages. (Sim has tavern commerce + EatTavern only.)
- [world] Regional services map: which town offers which guild/temple/service by size and wealth.

## World structure & travel
- [world] Region political layer: regional ruler & politics, regional wealth/economy,
  town/city/hamlet/wilderness classification *for the loaded region*. (Sim has climate + per-region
  load; not the political layer.) The full ~60-region province map is [deferred: multi-region].
- [world] Travel network: roads, fast-travel graph, wayside inns, random travel encounters between
  the region's own settlements. (Overlaps the [absent] caravans/travel item above.) Ships/travel
  *between provinces* are [deferred: multi-region].
- [world] Calendar & holidays: 12 months, the DF holiday set, birthsigns. (Sim has a HolidaySystem +
  time boundaries — extend to the full DF calendar.)
- [world] Languages skill effect: monster-language skills (Daedric/Orcish/Dragonish/Giantish/
  Harpy/Nymphic/Spriggan/Centaurian/Etiquette/Streetwise) modulating reactions.

## Character system (agent systems — shared by NPCs and human-controlled agents)
- [agent] Character generation: race, gender, class/archetype (or emergent role), name, birthsign,
  background — applied to every agent at spawn, not just a player.
- [agent] Attributes (STR/INT/WIL/AGI/END/PER/SPD/LUC) + derived HP/SP/fatigue; DF leveling formula.
- [agent] Full 35-skill DF list (weapon groups, 6 magic schools, languages, stealth, social,
  mercantile...). Sim has an XP/advancement engine but no defined skill set.
- [agent] Equipment: equip slots, armor by body location, weapons, layered clothing. The paperdoll
  compositing itself is [client] (presentation).

## Magic (agent systems; UI is [client])
- [agent] Spellcasting system: 6 schools of magic, spell costs, spell maker (custom spells), soul
  gems / soul trap — usable by any agent. The spellbook/casting UI is [client].
- [agent] Item maker (enchanting) and potion maker / alchemy as agent crafting actions.
- [world]/[agent] Spell *effects* (buffs/debuffs/diseases/curses) — partially served by the existing
  EffectsSystem; needs a spell-effect catalog wired to agent behavior.

## Items & equipment depth (agent systems)
- [agent] Material tiers (iron -> steel -> silver -> elven -> dwarven -> mithril -> adamantium ->
  ebony -> orcish -> daedric) with stat scaling.
- [agent] Item condition/durability + repair; magic items; books/ingredients.
- [world]/[agent] Daedric artifacts (the named artifacts) and daedra-summoning days.

## Dungeon & combat
- [world] 3D dungeon interiors, levers/switches, traps, treasure piles, random dungeon generation.
  (Building interiors already listed as [absent] in rendering.) The automap is [client].
- [agent] Combat: swing types, ranged, hand-to-hand, hit locations, critical/backstab, parry,
  weapon-vs-material/skill formulas — shared by all agents. First-person camera/aiming is [client].
- [agent] Survival/movement: fatigue & resting, encumbrance, torches/light, levitation, swimming,
  climbing.

## Main quest & narrative
- [deferred] The scripted DF main quest (Mantellan Crux, Totem, Numidium, Underking / King of Worms,
  six province endings) runs against the emergent-quest direction — out of scope as a scripted chain.
  Any equivalent would have to emerge from agent/faction goals, not a script.

## Meta / infra
- [client] Multiplayer is already a current capability — multiple human agents can connect at once
  ("MMO by accident"). Remaining client work is presentation/input + a join/spawn flow, not netcode.
- [deferred] Time-skipping / time-compression (rest-to-skip, fast-travel-as-time-skip) — a shared
  real-time multiplayer world can't fast-forward for one agent.
- [infra] Save/load = persistence of the shared world (not classic single-player saves).
- [client] World/region map UI + travel screen (intra-region; no time-skip per above).
- [world] Music & ambient audio by location/time. (Low priority.)

---

# Cross-cutting: engineering, multiplayer & world-meta

Systemic/foundational work, not Daggerfall-feature ports. Tags as above plus `[infra]` (engine /
tooling / persistence).

## Tick & determinism
- [infra] Double-buffer tick model: read tick N / write tick N+1, immutable per-tick data,
  single-writer eliminated. Current engine is serial single-writer (violates the target). EventBus
  is the correct prototype.
- [infra] Nested parallelism: systems in parallel + Parallel.For over agents inside each system,
  with deterministic ConcurrentDictionary iteration order (F3) — mandatory for multiplayer /
  multi-thread agreement.
- [infra] Throughput at region scale: single-thread is ~165 ticks/s @ ~1k civ today; validate
  Betony/Daggerfall-scale under the parallel model.

## Navigation at region scale
- [world] Multi-resolution nav: fine 64-cell grids inside settlements + a coarse wilderness/road
  layer between them; agents path coarse, then hand off to fine on arrival. Prerequisite for caravans
  and region-scale travel. (Today: intra-settlement pathfinding only.)

## Multiplayer mechanics
- [client]/[infra] Agent possession & handoff: a human takes control of an agent (AI <-> human
  control of the same body) and releases it back to AI. The control seam, not the netcode.
- [client] Join/spawn flow: a connecting human picks/instantiates an agent and enters the live world.
- [infra] Server authority & conflict resolution: who is authoritative for an agent/region; resolve
  concurrent intents deterministically.
- [infra] Shared-world persistence: snapshot/restore the live region (not classic single-player
  saves).

## World knowledge & emergence
- [world] Realistic agent knowledge: agents know only what they've perceived/heard, replacing today's
  omniscient `SeedTownKnowledge`. Ties into gossip/rumor spread.
- [world] Emergent world-event / director layer: significant life-tier events (famine, plague, feud)
  bubble up to persistent, named world events.
- [world] Inheritance & estate transfer on death: property, items, coin, titles pass to
  heirs/claimants (depends on reproduction/lineage; LineageRegistry has hooks).

## Content & tooling
- [infra] Region/industry authoring: hybrid discovered (terrain/climate via WOODS.WLD + HomeFarms) +
  authored industries per region, with an authoring/validation path.
- [infra] Settlement-profile table: LocationType -> role/tax/guards/wealth so the loader is
  settlement-aware (partly landed via Stage-5 kind-scaling; formalize).
- [infra] Remove vendored DFU scaffolding (`Assets/Scripts/`, `Assets/Game/`) once everything wanted
  is ported into the sim. Likely keep only the `DaggerfallConnect` ARENA2 readers as the data layer.
  (Final-cleanup task; gated on the porting backlog above.)
