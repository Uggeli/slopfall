# Running it

## Prerequisites
- **.NET 10 SDK**.
- A copy of Daggerfall's `ARENA2` game data (any legal copy). Point the sim at it:

```bash
export DAGGERFALL_ARENA2=/path/to/daggerfall/arena2
```

On the dev machine the data lives at `/home/uggeli/df-data/arena2`. Note: `ARCH3D.BSA` (building
models) has gone missing before — if building models 404, see the memory note / re-fetch from
archive.org.

## Build & test (run from `Headless/`)
```bash
dotnet build
dotnet test
```
The solution is `Headless/Sim.slnx`. The test suite is substantive (determinism, ODD scoring,
industry chains, subsistence, social) — if most tests don't run, suspect a **build break** before
assuming the tests are bad. `Sim.SpatialTests` runs independently.

## Single-town runs (`Sim.Host`)
```bash
# List locations in a region (or all regions if no arg)
dotnet run --project Sim.Host -- --probe Daggerfall

# Fast-forward a town and print a day-in-the-life trace (1440 ticks = 1 game-day)
dotnet run --project Sim.Host -- --town Daggerfall "Gothway Garden" --ticks 1440

# Multi-day stability soak: N game-days, daily metrics + verdict
dotnet run --project Sim.Host -- --soak Daggerfall "Gothway Garden" --days 7

# Watch live in the terminal
dotnet run --project Sim.Host -- --view Daggerfall "Gothway Garden"

# Serve + connect a spectator
dotnet run --project Sim.Host -- --serve Daggerfall "Gothway Garden" --port 7777
dotnet run --project Sim.Host -- --connect localhost:7777
```

## Region runs
Region-scale entry points (load/soak an entire region's settlements into one unified space):
```bash
dotnet run --project Sim.Host -- --loadregion <Region>          # smoke-load all settlements
dotnet run --project Sim.Host -- --soakregion <Region> --days N  # multi-day regional soak
```
Other probes seen in the codebase: `--gatecheck` (guard/gate behaviour), `--floracheck` (nature
scatter). When in doubt, run `Sim.Host` with no/garbage args to print usage.

## Web viewer
The 3D WebGL spectator is served by `Sim.Web` from `wwwroot/town3d.html` (pan the region, click a
civilian or building, watch ODD decisions live). See [architecture](architecture.md).

## The soak harness
`--soak` / `--soakregion` is the stability harness: it runs N game-days at one game-minute per tick,
sampling aggregate state daily and checking **structural invariants** (liveness, solvency, no deaths,
no NaNs) plus **shape observations** (wealth Gini, social saturation, relationship decay). Two runs
of the same input must be bit-identical (determinism). It exists to surface decay-shaped gaps before
they get baked in — see [`../behavior.md`](../behavior.md) §4.

## Throughput (rough, single-thread, full stack)
~165 ticks/s at ~1k civ; ~150 KB/civ RSS. A 30-day soak (43,200 ticks): ~4.5 min @ 1k, ~42 min @ 10k.
