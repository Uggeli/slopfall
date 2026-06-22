# PLACES Memory: Rich Learned Place Facts Drive ODD (Design)

**Date:** 2026-06-22
**Status:** approved-pending-review, ready for plan.
**Depends on:** agent memory write+consolidation (piece 2), memory core A1–A5, perceivable atoms.
**Part of:** memory integration (Phase B), **piece 4** — the PLACES → `GatherAds` dense coupling. The lever where memory visibly moves behavior.

## The idea

Agents keep a **rich atom-bag memory per place** (building) in the new PLACES store — `{BuildingKind,
ProvisionsHere, DangerHere, …}` — seeded with what they structurally know and learned by experience.
ODD reads it on **every decision** (the dense coupling) to *score* candidate place-ads: avoid places
remembered as dangerous, prefer ones with provisions. Behavior visibly shifts (danger-avoidance,
provisions-awareness) because place memory is consulted constantly, not once-per-rare-interaction.

## Decisions (locked)

1. **Additive, not a risky migration.** The old `PlaceMemory.Known` keeps driving *which* ads
   `GatherAds.Discover` generates (settlement navigation — seeded, proven, untouched → no regression).
   The new PLACES store is the **rich learned layer** ODD additionally reads to *score* those ads.
2. **Owner-stamps-its-own** (no central mapper): the loader seeds `BuildingKind`; `EconomySystem`
   stamps `ProvisionsHere` (it already observes it); threat/death events stamp `DangerHere`.
3. **Places use simple per-building atom accumulation**, not the recognition/surprise path — a place
   record's deltaBag IS its remembered atom bag, keyed by building, `categoryRef = None`. (Place
   *categories* — "taverns", "the dangerous quarter" — are a richer follow-on.)
4. **Seeding is a GENERAL agent-init capability.** Place-seeding is the first `SeedX` of a composable
   `AgentMemorySeeding` path; ownerships / reputations / social seed beside it later (see
   [[agent-memory-seeding-is-general]]). Structure it so they slot in without rework.
5. **Phased for safety:** Phase 1 writes+seeds the store with **no ODD change** (validate facts
   accumulate + behavior == baseline). Phase 2 has ODD score on the atoms (behavior moves).

## Architecture

### Place-atom vocabulary — `PlaceAtoms` catalog (id range 5000+)

| Range | Fact | Kind | Source |
|---|---|---|---|
| 5000+ | `BuildingKind(kind)` | presence (per `BuildingKind`) | loader seed (from `BuildingRegistry`) |
| 6000 | `ProvisionsHere` | graded 0..1 | `EconomySystem` observation |
| 6001 | `DangerHere` | graded 0..1 (recency/severity) | witnessed kill/attack near the building |

### Write path — `PlaceObserveIntent` → `AgentMemoryRegistry` (sole writer)

- `PlaceObserveIntent { EntityId Agent; int Building; AtomTypeId Atom; Fixed Value; }`.
- The registry merges the atom into the agent's PLACES record for that building: `TryGet` the existing
  record; `deltaBag = Merge(existing.DeltaBag, {atom:value})` (new value wins); re-`Encode` with a
  refreshed strength (so observed places stay vivid, unrefreshed fade). New building → a fresh record.
- **Seeding:** `AgentMemorySeeding.SeedPlaces(world, agent)` — for each building the agent `Known`s
  (from `PlaceMemory`/residency), stamp its `BuildingKind` atom directly into the PLACES store at load.
  Called from `TownLoader` spawn beside `world.AgentMemory.Seed(id)`.
- **Provisions:** where `EconomySystem` already calls `Note(id, building, ProvisionsHere, v, t)`, also
  emit `PlaceObserveIntent(agent, building, PlaceAtoms.ProvisionsHere, v)`.
- **Danger:** a `PlaceDangerSystem` reads `DeathEvent` (and/or attacks): for each agent who **witnessed**
  it (sensed the victim/killer or was near the building), emit `PlaceObserveIntent(witness, building,
  PlaceAtoms.DangerHere, severity)`. (Dead agents don't act — witnesses learn.)
- **Decay:** extend `ConsolidationSystem`/`Consolidation.Pass` to also decay the PLACES store, so
  `DangerHere` fades if the place stops being dangerous (memory is not permanent).

### Read path — ODD scores ads on place atoms (Phase 2)

- `OddSystem` gains read access to `AgentMemory`. In `Discover`/`V()`, for a candidate building, read
  `mem.Stores.Places.TryGet(MemoryKey(building))` → its atoms:
  - `DangerHere` present → multiply the ad's score down (avoid), proportional to the remembered value;
  - `ProvisionsHere == 0` → an eat/shop ad there is a wasted trip (the existing provisions gate, now
    sourced from the rich store).
- "Give ODD more": ad *generation* stays on `Known` (old store); ad *scoring* reads the rich PLACES
  atoms. No scalar mangling — ODD reads the atom bag and uses what each verb needs.

## Data flow

```
load        -> AgentMemorySeeding.SeedPlaces: BuildingKind atoms for Known buildings
economy obs -> PlaceObserveIntent(ProvisionsHere)  -> PLACES record updated
kill/attack -> PlaceDangerSystem: witnesses -> PlaceObserveIntent(DangerHere)
sleep       -> Consolidation decays PLACES (danger fades)
decide      -> OddSystem reads PLACES atoms -> scores ads (avoid danger, prefer provisions)  [Phase 2]
```

## Validation

- **Unit:** `PlaceObserveIntent` merges atoms into a building record (accumulates, value-wins);
  `SeedPlaces` writes BuildingKind for Known buildings; danger system emits for witnesses; ODD scoring
  helper (danger → lower score).
- **Phase 1 soak:** behavior histogram == baseline (ODD unchanged); a new soak metric shows PLACES
  records/agent and mean `DangerHere`/`ProvisionsHere` accumulating.
- **Phase 2 soak:** danger-avoidance visible — fewer agents repeatedly entering the monster kill-zones
  over days; assert behavior drifts from the Phase-1/baseline shape in the expected direction.

## Scope / deferred

- **In:** PlaceAtoms (kind/provisions/danger), the write paths + general seeding, PLACES decay, ODD
  scoring (Phase 2).
- **Deferred:** place *categories* (generalize across places); the future seed kinds (ownerships,
  reputations, social — [[agent-memory-seeding-is-general]]); retiring the old `PlaceMemory`; position-
  (not building-) keyed place memory; richer facts (crowdedness, safety).
