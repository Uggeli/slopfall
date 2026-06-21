# Slopfall Wiki

Lookup hub for the slopfall living-world simulation (a headless, data-oriented rewrite of the
town/NPC layer of [Daggerfall Unity](https://github.com/Interkarma/daggerfall-unity)). This wiki is
the in-repo source of truth for *orientation* — what the project is, how it's laid out, how to run
it, and where to find the deep design docs.

## Start here
- **[Direction](direction.md)** — the canonical design decisions (what we are building and the rules
  that constrain it). Read this first.
- **[Architecture](architecture.md)** — repo layout, the tick model, systems & registries, and the
  sim-vs-DFU split.
- **[Running](running.md)** — data setup, environment, build/test/soak commands.
- **[Systems status](systems-status.md)** — implemented / partial / absent inventory across sim,
  economy, rendering, and tests.
- **[Glossary](glossary.md)** — project vocabulary (ODD, ad/affordance, drives, S/L layers, larder,
  OwnerId, …).
- **[Design docs index](design-docs-index.md)** — annotated map of everything under `docs/`.

## The backlog
The exhaustive task inventory lives in **[`../../TODOS.md`](../../TODOS.md)** — bugs, missing
systems, the full stock-Daggerfall gap (tagged `[world]`/`[agent]`/`[client]`), and the cross-cutting
engineering/multiplayer work. This wiki explains *context*; TODOS.md tracks *work*.

## One-paragraph what-it-is
Classic Daggerfall Unity simulates a town only while you look at it. Slopfall pulls the town's life
into a standalone .NET simulation: civilians have needs, daily routines, opinions, money, episodic
memory, and the seeds of emergent quests, ticking headless with Unity demoted from *the program* to
*one possible renderer*. See [`../../README.md`](../../README.md) for the full pitch.
