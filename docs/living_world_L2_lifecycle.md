# L2 — Lifecycle & turnover

**Goal.** Turn on the loop Atoms calls entry→persist→decay→exit: agents **age**, **die**, are
**despawned** with full cross-registry cleanup, and are **replaced** so the town stays
populated. Today population is frozen at load: no aging, no natural death, no births. Death is
the "hard decay" case of L1 — and the thing that makes relationships, memory, and the economy
*matter*, because something can finally be lost.

**Atoms grounding.** World doc: entities have explicit **Spawn** (deterministic counter,
generational ref that "derefs as *gone*") and **Despawn** (frees the id and what `Relations`
says it contains, through a structural pass). "Nothing accumulates forever." Bunny doc:
breeding is a drive; kits are born into the warren with their own process; drive baselines and
personality shift slowly over a lifetime.

---

## Current state (anchors)

- **Death already half-exists.** `DeathSimEvent { Entity, Killer, FatalDamageType }`
  (`Assets/Sim/Events/HealthEvents.cs`) is **fired in exactly one place** —
  `HealthSystem.OnDamage` (`HealthSystem.cs:51`) when `CurrentHealth → 0` — and **handled only
  by `EventLog`** (logs it; no despawn). `VitalsData` carries an `IsDead` flag.
- **No age anywhere.** `IdentityData` = `{Name, Kind, Race, Gender, CareerIndex, Level,
  FactionId, Team}`; `VitalsData` = health/magicka/fatigue/breath/`IsDead`. No birth tick, no
  lifespan, no aging system. `grep age|birth` over registries is empty.
- **`EntityId` is a plain monotonic int** (`Core/EntityId.cs`): `IdentityRegistry.Allocate()
  = new EntityId(Interlocked.Increment(ref _nextId))`. **Never reused.** This is decisive (see
  §The dangling-reference problem).
- **No despawn coordination.** Registries each expose `.Remove(id)` (e.g.
  `RelationsRegistry.Remove`, `MemoryRegistry.Remove`) but nothing calls them in concert.
- **Spawn path** = `TownLoader.Spawn()` (`TownLoader.cs:402`), seeding ~12 components
  (Identity, Vitals, Stats, Position, Residency, Needs, Coin+CoinDef, Personality). RNG via
  `ctx.Random` at deterministic load order. `SettlementData.Residents` is a `List<EntityId>`.
- **Per-entity registries (the despawn checklist)** in `Core/SimulationContext.cs`: Identity,
  Position, Vitals, Lighting, Effects, EffectAggregate, StatusFlags, Stats, Progression,
  Residency, Needs, Behavior, Relations, Memory, Occupancy, Coin, Personality, PlaceMemory,
  Sensed, Intent, Employment. **Global / non-entity-keyed (skip):** WorldClock, Weather,
  Holiday, TownGrid, Buildings, Settlements, Ledger, Stock (per-building), Treasury
  (per-settlement).
- **Conservation is audited.** `LedgerRegistry` tracks Minted/Sunk; `CoinTransferEvent
  {From,To,Amount}` with `From=None`⇒mint, `To=None`⇒sink. Removing a `Coin` row without
  accounting **breaks the conservation invariant** — see §Death accounting.

## The gap

Spawn exists but exit does not, and there is no clock on a life. The architecture *anticipated*
death (the `IsDead` flag, every registry's `Remove`, the single death-fire site) but never
wired the cleanup or a natural cause. And because ids are monotonic, the hard part isn't the
removal — it's the **references other agents hold to the deceased**, which L3 is about to make
load-bearing (a death must be *felt* through the dossier).

---

## Design

### L2.1 — Age as a component + an `AgingSystem`

Add a dedicated component (Atoms one-concern-per-registry) rather than bloating Identity:

```csharp
// Assets/Sim/Registries/LifeRegistry.cs  (new) — per-entity, EntityId-keyed
public sealed class LifeData
{
    public long BirthTick;      // tick the agent was born (or load-stamped for initial pop)
    public double LifespanYears;// drawn at spawn; mortality hazard rises past it
}
```

Seed at spawn in `TownLoader.Spawn()`: initial residents are adults, so stamp
`BirthTick = now - randomAdultAgeYears` and `LifespanYears` from a distribution (placeholder:
~55–75 game-years). Draws use the existing deterministic load-time `ctx.Random` (same stream
the other seeds use — order is fixed).

`AgingSystem` (new), registered early (after `TimeSystem`), reacts to `NewYearSimEvent`
(coarse cadence — aging is the slowest timescale):

```csharp
// per NewYearSimEvent, for each LifeData in KEY ORDER:
double ageYears = (now - life.BirthTick) / TicksPerGameYear;
double hazard   = MortalityHazard(ageYears, life.LifespanYears);   // 0 below lifespan, rising after
// DETERMINISTIC draw — hash, not a shared stream (replay-exact, parallel-safe)
double roll = (Hash(id.Value, now) / (double)uint.MaxValue);
if (roll < hazard)
    _ctx.Events.Emit(new DamageEvent { Target = id, Source = EntityId.None,
                                       Amount = BIG, Type = DamageType.Age });
```

**Reuse the single death-fire site:** age death emits a lethal `DamageEvent(Age)` →
`HealthSystem` zeroes health → fires the one `DeathSimEvent`. (Add `DamageType.Age` to the
enum.) This keeps "`DeathSimEvent` is fired in exactly one place," so every downstream handler
is cause-agnostic.

### L2.2 — `LifecycleSystem`: the despawn structural pass

New system registered **last** (so every other system's `ProcessEvents`/`Update` this tick has
already run against a still-live entity). It subscribes to `DeathSimEvent`, collects ids into a
pending set during `ProcessEvents`, and performs cleanup in its `Update` (end of tick) — the
analogue of Atoms' post-Update structural pass. Because `HealthSystem` emits `DeathSimEvent`
during event handling and emissions flush to the next tick, the cleanup runs a tick after the
death is announced, by which point the event has fully propagated.

```csharp
void DespawnPass()
{
    foreach (var id in OrderByKey(_pending))      // key order = determinism
    {
        SettleEstate(id);                          // §L2.3 — account the coin BEFORE removing
        foreach (var reg in PerEntityRegistries)   // the checklist above
            reg.Remove(id);
        RemoveFromSettlementResidents(id);
        _ctx.Events.Emit(new DespawnedEvent { Entity = id });  // for L3 memory + replacement
    }
    _pending.Clear();
}
```

### L2.3 — Death accounting (conservation must hold)

Removing the `Coin` row silently would break the audited money supply. Before `Coin.Remove`,
**escheat** the purse so it's recorded:

```csharp
double purse = _ctx.Coin.Get(id);
if (purse > 0)
    _ctx.Events.Emit(new CoinTransferEvent { From = id, To = settlementTreasuryOwner, Amount = purse });
```

Escheat-to-treasury is deterministic, conserves the supply, and recirculates via the existing
`CivicDividend` — a clean default. **Inheritance to an heir** (a co-resident / highest-regard
relation) is the richer "alive" version and a natural L2-future / L4 tie-in (the dossier picks
the heir); flagged, not built first. The dead agent's *stock/treasury ownership* (if a keeper)
is handled by replacement (§L2.4) re-staffing the building.

### L2.4 — Birth / replacement (hold the population)

Atoms' destination is **reproduction** (libido drive → gestation → kits that age to
adulthood). That is a large, slow mechanism (decades of game-time to backfill an adult) and is
explicitly **L2-future**. The shippable wedge is **immigration/replacement**:

A `RepopulationSystem` (or fold into `LifecycleSystem`), on `DespawnedEvent` or monthly, if a
settlement's living population is below its target (or a building's role is vacant), spawns a
replacement **adult** via the existing `TownLoader.Spawn()` path into the vacant
building/`ResidentRole`. This reuses the whole seed block and naturally re-staffs a dead
keeper's shop (closing the employer-death ripple — see §The dangling-reference problem).
Determinism: iterate settlements/buildings in key order; for runtime spawn draws use a hashed
seed `hash(tick, settlementIndex, slot)` rather than the shared `ctx.Random` stream (which is
load-time only), so replays are exact.

---

## The dangling-reference problem (the L3-critical part)

When X dies and its per-entity rows are removed, **other agents still reference X**:
`Relations[other].Of[X]`, `Memory[other]` entries with `Other=X`, `Employment[other].Employer=X`,
`SettlementData.Residents`, `RequestSystem._pending[*].Target=X`.

**Because `EntityId` is monotonic and never reused, every dangling reference is *safe*** — a
later `TryGet(X)` returns false, which is exactly Atoms' *"a dead entity's ref derefs as
gone."* We get generational-ref semantics for free from non-reuse. So correctness does **not**
require scrubbing inbound references; it requires:

1. **Read-side liveness tolerance.** Confirm every consumer treats "X not found" as gone, not
   as an error. Survey: `ActionDiscovery.GatherAds` already guards employer with
   `ctx.Residency.TryGet(emp.Employer,…)` → a dead employer simply stops advertising Work (the
   employee falls back to other ads, then gets re-staffed by §L2.4). `RequestSystem` journeys
   need a `TryGet(target)` guard (dead target ⇒ abandon journey; the `JourneyTimeout` already
   bounds it). Audit each consumer; add guards where missing.
2. **Lazy prune to bound growth.** Dangling `Relations.Of[X]` entries waste space but are
   harmless. **Fold the prune into L1's `DecayRelations` pass:** drop an entry when its
   familiarity has decayed below `MetFamiliarity` *or* its `Other` is no longer live. Memory's
   ring buffer (32 entries, newest-first) self-bounds — dead-`Other` episodes age out. This is
   why L1 ships before L2: the decay pass is also the reference-GC.

Net: **no separate inbound-scrub system.** Monotonic ids + L1 decay + read-side guards = Atoms'
dead-ref semantics on the cheap.

---

## Staged sub-steps

1. **L2.1** — `LifeRegistry`/`LifeData`, seed at spawn, `DamageType.Age`, `AgingSystem` (hazard
   + deterministic draw → lethal `DamageEvent`). At this point agents can die of age but
   nothing cleans up — verify deaths fire and `EventLog` records them.
2. **L2.2** — `LifecycleSystem` despawn pass (registered last); the per-entity cleanup
   checklist; `DespawnedEvent`. Add the read-side liveness guards (audit consumers).
3. **L2.3** — death accounting (escheat `CoinTransferEvent` before `Coin.Remove`); verify the
   ledger conservation invariant still passes across deaths.
4. **L2.4** — `RepopulationSystem` (immigration/replacement) to hold population and re-staff
   vacated roles. Reproduction flagged L2-future.

## Per-stage gates (mechanism + invariants — provable on placeholders, no tuning)

- **Invariants (hard):** determinism preserved (replay-exact across deaths/births — extend
  `SnapshotTests`/`EconomyDeterminismTests`); **coin conservation holds across a death** (ledger
  Minted−Sunk balances including escheat); registry `Count`s stay **bounded** over a long soak
  (no leak — cleanup + prune work); no live system throws on a dangling ref.
- **Mechanism fires (qualitative, direction-only):**
  - Agents **die of age** and are **despawned** (rows gone, removed from `Residents`); the death
    is recorded.
  - A dead keeper's shop is **re-staffed** and its orphaned employees regain a Work ad (the
    employer-death ripple resolves) — scenario test, sign not rate.
  - Replacement keeps population from collapsing to zero or leaking upward (boundedness, not a
    tuned level).

Deferred to the post-L4 tuning phase: the births≈deaths *balance*, the *stationary level*, the
age-distribution shape, `LifespanYears`/`MortalityHazard`/replacement-rate. Partial-stack
population trajectories are observed, not tuned.

## Numbers (frozen placeholders — tuned only when the whole stack lands)

`LifespanYears` distribution, `MortalityHazard(age, lifespan)` shape, aging cadence (yearly vs.
monthly), replacement/immigration rate and per-settlement population target, escheat-vs-
inheritance policy. Pick sane placeholders and **leave them**; population equilibrium is coupled
to the economy and the rest of the L-stack and is fit in the single post-L4 tuning pass.

## Risks / open questions

- **`TicksPerGameYear` & timescale.** Aging cadence must be expressed in game-time, not ticks,
  so it's invariant to `TimeScale` (same discipline as L1's decay helper). Derive from
  `WorldClock`/`SimulationTime`.
- **Determinism of runtime spawn.** The id counter is `Interlocked` (fine), but the *trigger
  order* and any spawn RNG must be deterministic — iterate in key order, hash-seed draws.
  Extend the determinism tests to cover a birth/death sequence.
- **Reproduction vs. immigration.** Immigration is the wedge; full reproduction (libido drive,
  pairing, gestation, child→adult aging, inherited seed per the memory doc's "parent-transmitted
  seed") is a large future arc that also feeds L3/L4 (children learn norms socially). Keep it
  out of the first cut.
- **Player + non-civilian entities.** `AgingSystem`/`LifecycleSystem` must scope to civilian
  agents (`EntityKind.CivilianNPC`) and never despawn the player or quest-critical entities —
  gate on `IdentityData.Kind`.
