# Slopfall — a living-world simulation rewrite of Daggerfall Unity

> A fork of [Daggerfall Unity](https://github.com/Interkarma/daggerfall-unity) that rebuilds
> the town and NPC layer as a **headless, data-oriented simulation**. Civilians have needs,
> daily routines, opinions of one another, money, episodic memory, and the first emergent
> quests — and the whole thing runs as a plain .NET console app, with Unity demoted from
> *the program* to *one possible renderer*.

The active work lives on the **`sim-rewrite`** branch.

---

## What this is

Classic Daggerfall Unity simulates a town only while you're looking at it. This fork pulls the
town's life out into a standalone simulation that:

- **Runs without Unity.** The sim core compiles into a normal .NET solution (`Headless/`) and
  ticks a real Daggerfall location loaded straight from the original `ARENA2` game data — no
  engine, no renderer, no scene required. This is deliberate: the machine this was built on
  can't run the Unity editor, so the sim is **headless-first** and the engine is an optional
  consumer of its state.
- **Is data-oriented.** State lives in flat **registries**; logic lives in **systems** that are
  each the *sole writer* of their registries and communicate only through an **event bus**.
  No `Update()` spaghetti, no hidden cross-references — the same discipline that makes a
  dedicated server or an engine swap tractable.
- **Grows minds, not scripts.** NPC behaviour is driven by a need/drive model (a "wedge" ported
  from the [ODD](https://en.wikipedia.org/wiki/Overview,_design_concepts,_and_details) lineage,
  converging on the *Atoms* cognitive architecture): deficit poles that drift, traits that bias
  choices, perception that can interrupt a plan, and an argmax "marketplace" that decides what
  to do next. See [`docs/behavior.md`](docs/behavior.md) for the honest scorecard and roadmap.

What already emerges from this: civilians walk real streets via hierarchical pathfinding, keep
need-driven daily schedules, form opinions through shared time in taverns, gossip those opinions
through the social graph, earn and spend coin, become friends, and ask each other for help when
they can't help themselves — the seed of an emergent quest system.

## Architecture

```
              ARENA2 game data  ──►  TownLoader  ──►  Registries (state)
                                                          ▲   │
                                                          │   ▼
   Inputs ──►  EventBus  ◄──────────────────────────  Systems (logic)
                                                          │
                                                          ▼
                                                   RenderSnapshot  ──►  TUI / web / Unity
```

**The tick** (`TickLoop.Step`) is a fixed pipeline: drain cross-thread inputs → deliver last
tick's events → each system processes events into its registries → each system updates →
flush newly-emitted events for next tick → advance the clock. Everything is **game-time
driven** (needs drift and decisions are gated on game-minutes, not raw ticks), so the sim is
resolution-independent and a simulated week is just a tick count.

Roughly 20 systems run each tick — clock, weather, lighting, health, effects, skills/progression,
holidays, economy, needs, the ODD decision core, movement, perception, social fabric, and the
request (proto-quest) system — over ~20 registries holding identity, vitals, needs, personality,
relations, memory, residency, coin, and the world grid. The full inventory and the design
direction are documented in [`docs/behavior.md`](docs/behavior.md).

## Project layout

| Path | What |
|------|------|
| `Assets/Sim/` | The simulation core — **single source of truth**. Registries, systems, events, threading. Compiled both by Unity *and* by the headless solution (no copies). |
| `Assets/Sim/Bridge/` | The Unity bridge (mirrors/driver) for when the engine *is* the renderer. Unverified on this machine — engine-swap insurance. |
| `Headless/` | Standalone .NET solution (`Sim.slnx`) that compiles `Assets/Sim` with zero Unity dependency. |
| `Headless/Sim.Core` | Registries, systems, events (targets `netstandard2.0` so Unity can consume it). |
| `Headless/Sim.Data` | `DaggerfallConnect` readers — parse `ARENA2` (`MAPS.BSA`, `BLOCKS.BSA`, …). |
| `Headless/Sim.World` | Town loading into the registries. |
| `Headless/Sim.Net` | TCP snapshot protocol — networked spectators. |
| `Headless/Sim.Host` | Console runners: combat demo, town demo, TUI viewer, **soak**, serve/connect. |
| `Headless/Sim.Web` | Web spectator — pan the town, click a civilian, watch their life. |
| `Headless/Sim.Tests` | 119 tests covering the sim. |

## Running it headless

**Prerequisites:** the [.NET 10 SDK](https://dotnet.microsoft.com/download) and a copy of
Daggerfall's `ARENA2` game data (any legal copy — e.g. the free Steam release). Point the sim at
it with an environment variable:

```bash
export DAGGERFALL_ARENA2=/path/to/daggerfall/arena2
```

All commands run from the `Headless/` directory.

```bash
# Build and test
dotnet build
dotnet test                      # 119 tests

# List locations in a region (or regions, if no args)
dotnet run --project Sim.Host -- --probe Daggerfall

# Fast-forward a real town and print a day-in-the-life trace
dotnet run --project Sim.Host -- --town Daggerfall "Gothway Garden" --ticks 1440

# Multi-day stability soak: run N game-days, report daily metrics + a verdict
dotnet run --project Sim.Host -- --soak Daggerfall "Gothway Garden" --days 7

# Watch the town live in the terminal
dotnet run --project Sim.Host -- --view Daggerfall "Gothway Garden"

# Run as a server; connect a spectator from another process
dotnet run --project Sim.Host -- --serve Daggerfall "Gothway Garden" --port 7777
dotnet run --project Sim.Host -- --connect localhost:7777
```

### The soak test

`--soak` is the multi-day stability harness. It runs a real town for N game-days at one
game-minute per tick and samples aggregate state every day, checking **structural invariants**
(liveness — does the town keep deciding; solvency — does the economy stay in a band; no deaths;
no NaNs) and reporting **shape observations** (wealth concentration via a Gini coefficient,
social saturation, the no-decay signature of the relationship model). It exists to de-risk
deeper modeling and to surface decay-shaped gaps before they get baked in — see
[`docs/behavior.md`](docs/behavior.md) §4.

---

## Built on Daggerfall Unity

This is a fork. The entire Daggerfall engine recreation underneath it — the rendering, the
formulas, the `DaggerfallConnect` data readers this sim reuses to load `ARENA2` — is the work of
[Daggerfall Workshop](http://www.dfworkshop.net) and the Daggerfall Unity community. Daggerfall
Unity is an open-source recreation of Bethesda's *The Elder Scrolls II: Daggerfall* in the Unity
engine. If you're looking for the *game* (to play, mod, or build on the engine), go upstream:

- **Upstream repository:** https://github.com/Interkarma/daggerfall-unity
- **Daggerfall Workshop:** http://www.dfworkshop.net/
- **Nexus mods:** https://www.nexusmods.com/daggerfallunity
- **Discord (Lysandus' Tomb):** https://discord.gg/rn95kxPGpg
- **Forums:** http://forums.dfworkshop.net/

Daggerfall Unity requires a free copy of DOS Daggerfall for its game assets; the same `ARENA2`
data feeds this simulation.

## License

MIT, inherited from Daggerfall Unity — Copyright (c) 2009-2023 Daggerfall Workshop. See
[`LICENSE`](LICENSE).
