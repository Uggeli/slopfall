# Glossary

Project vocabulary. Terms cross-reference the [design docs](design-docs-index.md).

- **Registry** — a flat store of one kind of state (identity, needs, coin, relations, the world grid,
  …). Mutated only by its owning system.
- **System** — a unit of logic, the *sole writer* of its registries; communicates via the EventBus.
  ~20 run each tick.
- **EventBus** — the only channel between systems. Events emitted this tick are delivered next tick
  (the prototype for the target double-buffer model).
- **Tick / game-time** — the fixed pipeline step (`TickLoop.Step`). Needs and decisions gate on
  **game-minutes**, not raw ticks, so the sim is resolution-independent (1440 ticks ≈ 1 game-day).
- **ODD** — the decision core (Overview-Design-concepts-Details lineage, converging on the *Atoms*
  cognitive architecture). A need/drive "marketplace" that picks the next action by argmax.
- **Ad (affordance)** — a candidate action offered to an agent (e.g. "Work here", "EatHome",
  "Chat with X"). Agents gather ads, score them, and pick the best. "An ad is an ad" — person- and
  building-directed opportunities are scored uniformly.
- **OddTree** — a depth-1 Enables-DAG over actions with backward-propagating scores (geometric
  decay), wired into `OddSystem.Decide`. Deeper chaining/goals are future work.
- **Drive / deficit pole** — a need that drifts over time (hunger, rest, coin, social, fear, …),
  biased by personality **traits**, creating pressure the marketplace resolves.
- **Wedge** — the scoring shape that converts a deficit + context into an action's utility.
- **S-layers (cognitive substrate)** — S1 Membrane (perception/interpretation), S2 Emotion (affect),
  S3 Meanings (semantic store/consolidation), S4 Conscience (norms/guilt).
- **L-layers (living world)** — L1 Entropy (decay), L2 Lifecycle (birth/age/death/despawn), L3
  Membrane (subjective view → decision), L4 Directed Drives (person-targeted actions).
- **Larder** — a home's stored provisions; farms/fisheries fill it, `EatHome` consumes it.
- **OwnerId** — a forward-compatible treasury/ownership handle; per-settlement treasuries each get a
  distinct OwnerId, designed to "become a faction id" later.
- **Settlement profile** — role/tax/guards/wealth derived from Daggerfall `LocationType`
  (hamlet/village/city/farm/temple/tavern).
- **Request system** — the current proto-quest path (e.g. alms-seeking); the seed of the emergent
  job/contract board described in [direction](direction.md).
- **Soak** — the multi-day stability harness; see [running](running.md).
- **ARENA2** — the original Daggerfall game data the sim loads via `DaggerfallConnect` readers.
- **DFU** — Daggerfall Unity, the upstream engine; its source is vendored under `Assets/Scripts/` and
  `Assets/Game/` as a reference (see [architecture](architecture.md)).
- **Embodied vs life tier** — the LOD axis: life (always on, every agent) vs embodied (movement,
  perception — only where observed). See [direction](direction.md).
