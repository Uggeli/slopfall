# Architecture

## Two codebases in one repo — don't confuse them
- **The sim** = `Assets/Sim/` (the single source of truth) compiled by the headless solution under
  `Headless/` (`Sim.slnx`). No copies — Unity and the .NET solution both compile `Assets/Sim`.
- **Vendored Daggerfall Unity (DFU) reference** = `Assets/Scripts/Game/` and `Assets/Game/Addons/`.
  Full working DF logic (banks, guilds, quests, vampirism, alchemy, combat, dungeon crawl, …) lives
  here as a reference/data library, **not wired into the sim**. A grep hit under these paths means
  "DFU has it," not "the sim has it."

The repo is a fork of Daggerfall Unity (`upstream = Interkarma/daggerfall-unity`); the sim reuses
DFU's `DaggerfallConnect` data readers to load the original `ARENA2` game data.

> **End state:** the vendored DFU code is a *temporary* porting source — it gets removed once
> everything we want is ported into the sim. Treat `Assets/Scripts/` and `Assets/Game/` as scaffolding
> to delete later, not as code to depend on long-term. (`DaggerfallConnect` readers for `ARENA2` are
> the likely exception — they may stay as the data-loading layer.)

## Data-oriented core
State lives in flat **registries**; logic lives in **systems**, each the *sole writer* of its
registries, communicating only through an **EventBus**. No `Update()` spaghetti, no hidden
cross-references.

```
          ARENA2 game data  ──►  TownLoader  ──►  Registries (state)
                                                      ▲   │
                                                      │   ▼
 Inputs ──►  EventBus  ◄──────────────────────────  Systems (logic)
                                                      │
                                                      ▼
                                               RenderSnapshot  ──►  TUI / web / Unity
```

## The tick
`TickLoop.Step` is a fixed pipeline: drain cross-thread inputs → deliver last tick's events → each
system processes events into its registries → each system updates → flush newly-emitted events for
next tick → advance the clock. Everything is **game-time driven** (needs/decisions gate on
game-minutes, not raw ticks), so the sim is resolution-independent.

> **Target vs current:** the intended model is a double-buffer (read tick N / write tick N+1,
> immutable per-tick data, nested parallelism). The current engine is serial single-writer — see
> TODOS "Tick & determinism". The EventBus is the correct prototype for the target.

## Systems & registries (~20 each)
Systems run each tick: clock, weather, lighting, health, effects, skills/progression, holidays,
economy, needs, the **ODD decision core**, movement, perception, social fabric, and the request
(proto-quest) system. Registries hold identity, vitals, needs, personality, relations, memory,
residency, coin, items, and the world grid. Sim source is organised under:
`Assets/Sim/{Core, Engine, Registries, Systems, World, Bridge}` (most engine systems live in
`Assets/Sim/Engine/Units/`).

## Headless solution map (`Headless/`)
| Project | What |
|---------|------|
| `Sim.Core` | Registries, systems, events (targets `netstandard2.0` so Unity can consume it). |
| `Sim.Data` | `DaggerfallConnect` readers — parse `ARENA2` (`MAPS.BSA`, `BLOCKS.BSA`, …). |
| `Sim.World` | Town/region loading into the registries. |
| `Sim.AssetExport` | Geometry/sprite/terrain export for the web viewer (glTF, atlases, tiles). |
| `Sim.Net` | TCP snapshot protocol — networked spectators. |
| `Sim.Host` | Console runners: combat/town demos, TUI viewer, **soak**, serve/connect, region tools. |
| `Sim.Web` | Web spectator (`wwwroot/town3d.html`) — 3D WebGL viewer + inspect. |
| `Sim.Tests`, `Sim.SpatialTests`, `PoiClassifierTests` | Test projects. |

## Decision core (ODD)
NPC behaviour is a need/drive "marketplace": deficit poles drift, traits bias choices, perception can
interrupt a plan, and an argmax picks the next action from gathered **ads** (affordances). The
`OddTree` (a depth-1 Enables-DAG with backward-propagating scores) is wired into `OddSystem.Decide`.
Deeper planning (multi-step chains, goals/beacons) is future work. See
[glossary](glossary.md) and [design docs](design-docs-index.md).

## Rendering / viewer
The web viewer (`Headless/Sim.Web/wwwroot/town3d.html`) streams terrain/buildings/flats/agents per
map-pixel ring around the camera and supports agent / building / ODD-decision-tree inspect. Godot is
the engine-side renderer via `Assets/Sim/Bridge/` (engine-swap insurance, unverified on the build
machine). See [systems status](systems-status.md) for what's rendered vs missing.
