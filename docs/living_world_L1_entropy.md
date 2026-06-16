# L1 — Entropy & satisfaction-models

**Goal.** Give the town a *second law*. Today every accumulating quantity is monotonic
(regard, familiarity, friend-edges, top-end coin); the soak confirms the social graph "never
reaches equilibrium." Introduce **decay-toward-baseline** as a first-class, reusable
mechanism, and apply it first to relationships — the soak's validated #1 social gap.

**Atoms grounding.** The drive doc: deficiency poles **deplete**, aversion poles
**reset-on-percept**, growth poles have **no discharge**. The memory doc: records **decay
unless refreshed**; "forgetting is a feature." The world doc: "every field has a baseline it
decays toward … nothing saturates to a static steady-state." Decay is the substrate, not a
bolt-on. We are encoding the *satisfaction-model* field the drive doc already names:
`{ deplete | reset-on-percept | decay-toward-baseline | none }`.

**Soak finding addressed** (`soak_findings.md`): "regard/familiarity climb monotonically; over
30 days second-half growth *exceeds* first-half; 4605 pairs pinned at |regard|≥0.95. → add a
satisfaction-model/decay to regard."

---

## Current state (anchors)

- `RelationData` (`Assets/Sim/Registries/RelationsRegistry.cs:9`): `double Familiarity` (0..1),
  `double Regard` (-1..1), `bool FriendAnnounced`. The doc comment already flags it as "the
  target-field half of a directed social drive."
- `SocialSystem.GrowRelations` (`Assets/Sim/Systems/SocialSystem.cs:101`): per co-located
  group, per tick, grows `Familiarity += gameMinutes/MinutesToFullFamiliarity` (clamped 0..1)
  and `Regard += Affinity(self,other)*familiarityGain` (clamped -1..1). Constants:
  `MinutesToFullFamiliarity=600`, `MaxPartners=6`, `MetFamiliarity=0.05`,
  `FriendFamiliarity=0.3`, `FriendRegard=0.25`. **No decrease term anywhere.**
- `FriendAnnounced` is a **one-way latch** — set once at the bar, never cleared.
- `RelationImpulseEvent` (`Assets/Sim/Events/SocialEvents.cs`): the cross-system write channel;
  applied in `SocialSystem.ProcessEvents` (whole-row `CloneRelations`→mutate→`Set`).
- Needs already decay-ish: `NeedsSystem.Update` drifts deficiency poles up by
  `DriftPerHour={0.04,0.05,0.03,0.0,0.03}` and clamps `[0,VMax=1.5]`; activities apply negative
  `Delta`. So the *deplete* satisfaction-model already exists implicitly for needs — L1 names
  it and adds the missing *decay-toward-baseline* for relationships.

## The gap (in Atoms terms)

`Regard` is a pole with `satisfaction = none` — it only integrates, never discharges or
decays. In the drive doc that is reserved for *bottomless growth drives*; a social bond is not
bottomless. The correct model: **familiarity and regard decay toward baseline unless
refreshed by contact** — exactly the memory doc's "re-encounter and recall refresh; unrecalled
fades." This converts the monotonically-saturating graph into a *dynamic equilibrium*: active
relationships stay warm, neglected ones cool, friendships can lapse.

---

## Design

### L1.1 — The `SatisfactionModel` vocabulary (the L0 scaffold)

Add an enum and make it explicit on the things that have poles. Pure naming + a decay helper;
no behavior change yet.

```csharp
// Assets/Sim/Core/SatisfactionModel.cs  (new)
public enum SatisfactionModel
{
    None,               // bottomless — integrates, never discharges (growth drives)
    Deplete,            // an action discharges it (hunger ← eat); needs already do this
    ResetOnPercept,     // resets while the cue is absent (fear ← threat gone)
    DecayTowardBaseline // erodes toward a baseline each tick unless refreshed
}

// shared deterministic decay step (fixed formula, replay-exact)
public static class Decay
{
    // exponential pull toward baseline; rate is per-game-hour fraction.
    public static double TowardBaseline(double v, double baseline, double ratePerHour, double gameHours)
        => v + (baseline - v) * (1.0 - System.Math.Pow(1.0 - ratePerHour, gameHours));
}
```

Rationale for `Pow(1-rate, hours)`: tick-rate-independent (same decay per game-hour regardless
of `TimeScale`), monotone, and a pure function of its inputs → deterministic and
machine-portable (no accumulated float drift across tick cadences). Document the rate as a
*placeholder*.

### L1.2 — Relationship decay in `SocialSystem`

The key insight from the anchors: `GrowRelations` only touches **co-located** groups, so a
decay applied *only there* would never reach neglected pairs (the ones we want to cool). Decay
must run over **all** relations each tick, with growth as the refresh that opposes it.

Two clean options; **recommend B**:

- **A — decay inside `GrowRelations`** after the growth write. Rejected: misses non-colocated
  pairs entirely (the soak's saturated pairs are exactly the ones not currently in a group).
- **B — a separate pass over `ctx.Relations.All`** (new `DecayRelations`, runs in
  `SocialSystem.Update` before/after `GrowRelations`). Reaches every pair; growth in the same
  tick refreshes the co-located ones so net change is positive only where there's contact.

```csharp
// SocialSystem.Update, new pass — runs over ALL relation rows, key-ordered for determinism
void DecayRelations(double gameHours)
{
    foreach (var kv in OrderByKey(_ctx.Relations.All))      // key-order = determinism
    {
        var next = CloneRelations(kv.Key);
        bool changed = false;
        foreach (var rel in next.Of.Values)
        {
            double fam0 = rel.Familiarity, reg0 = rel.Regard;
            rel.Familiarity = Decay.TowardBaseline(rel.Familiarity, 0.0, FamiliarityDecayPerHour, gameHours);
            rel.Regard      = Decay.TowardBaseline(rel.Regard,      0.0, RegardDecayPerHour,      gameHours);
            // friendship lapses if it falls back through the bar (clear the latch)
            if (rel.FriendAnnounced && (rel.Familiarity < FriendFamiliarity || rel.Regard < FriendRegard))
                rel.FriendAnnounced = false;
            changed |= rel.Familiarity != fam0 || rel.Regard != reg0;
        }
        if (changed) _ctx.Relations.Set(kv.Key, next);
    }
}
```

Decay baselines are `0.0` (acquaintances forgotten, neutral opinion the resting state).
**Refresh dominates contact:** in a co-located tick, `GrowRelations` adds
`familiarityGain ≈ gameMinutes/600` while `DecayRelations` removes a small fraction — tune so
**daily contact keeps a bond warm, a week of no contact noticeably cools it.** Numbers are
placeholders (see §Tuning).

Cost note: `DecayRelations` is O(total relation edges)/tick. The soak shows thousands of edges;
this is the same iteration `TownCensus` already does. If it shows up hot, amortize Atoms-style
(walk a key-ordered cursor, K rows/tick) — but only if the soak says so, not preemptively.

### L1.3 — `FriendAnnounced` becomes re-announceable

Today it's a one-way latch (set at the bar, never cleared). With decay, a friendship can
genuinely lapse and re-form. L1.2 clears it on lapse. Add a `FriendshipLapsedEvent` (symmetric
to `FriendshipFormedEvent`) so the census/event-log and L3 memory can observe it. Re-crossing
the bar re-emits `FriendshipFormedEvent` (already handled).

### L1.4 (optional, connects to economy.md E1) — the larder/goods sink as the same pattern

`GoodsDef` (`NeedAxis.GoodsDef`, drift `0.03/hr`) is already a *deplete* pole: it rises, `Buy`
relieves it `-0.3` per 30 min gated on shop stock (`ActivityCatalog.Buy`,
`EconomySystem.PaySale:557` draws `Stock.Add(building, good, -units)`). This is the **economic
instance of L1's principle** — a household supply that depletes and must be refilled from a
shop, recirculating keeper coin (the soak's #2 economy gap). The *full* larder model
(per-resident household stock keyed by `EntityId`, `Buy` refills it, drift tied to depletion)
is economy-track work; tracked in `economy.md` E1, cross-referenced here so the satisfaction-
model vocabulary stays shared. **Do not build the larder in L1** unless economy.md E1 lands
first — L1's deliverable is relationship decay.

---

## Staged sub-steps

1. **L1.1** — add `SatisfactionModel` enum + `Decay.TowardBaseline`; unit-test the decay helper
   (deterministic algorithm → thin unit layer is appropriate here: monotone, hits baseline,
   rate-independent across `gameHours` splits). No behavior change. *Suite stays green.*
2. **L1.2** — add `DecayRelations` pass + constants; register in `SocialSystem.Update`. Delete
   any brittle social-magnitude assertions (per testing discipline) and replace with the
   behavioral expectations below.
3. **L1.3** — re-announceable friendship + `FriendshipLapsedEvent`; `EventLog`/`TownCensus`
   surface lapses.
4. **L1.4** — *deferred to economy.md E1* (noted, not built here).

## Per-stage gates (mechanism + invariants — provable on placeholders, no tuning)

- **Invariants (hard):** determinism preserved (re-run = identical); no NaN; `Regard∈[-1,1]`,
  `Familiarity∈[0,1]`; with any positive decay rate the graph is now **bounded** —
  `Regard`/`Familiarity` can no longer climb without limit (a structural consequence of decay,
  not a tuned level).
- **Mechanism fires (qualitative, direction-only):**
  - A pair isolated after meeting ends up with **lower** regard than a pair kept in daily
    contact (scenario test — sign, not magnitude).
  - A `FriendshipLapsedEvent` **can** fire when a bond falls back through the bar and the latch
    clears (prove the path, don't tune the rate).

Deferred to the post-L4 tuning phase (`living_world.md` → *Tuning*): the plateau *level*, the
decay *rates*, and how the graph feels over a long soak. Partial-stack soaks are observed only
— a wrong-looking equilibrium here is expected.

## Numbers (frozen placeholders — tuned only when the whole stack lands)

`FamiliarityDecayPerHour`, `RegardDecayPerHour`, decay baselines (start `0.0`), and the flagged
option of a personality-set resting point instead of flat `0.0`. Set sane placeholders (rates
an order of magnitude below daily growth, so contact wins) and **leave them** — per
`living_world.md`, these are coupled to L3/L4 and are fit together in the single post-L4 tuning
pass, not here.

## Risks / open questions

- **Decay vs. gossip equilibrium.** Gossip (`HearGossip`) also moves regard without contact.
  Decay should pull gossip-inflated opinions back too; verify the two don't fight into
  oscillation (the soak/behavioral band will show it).
- **Baseline choice.** Flat `0.0` baseline means *everyone drifts to strangers*. Atoms would
  say the *innate seed* (a warm disposition) could set a small positive baseline. Deferred to
  tuning; structurally the baseline is a parameter, so this is a number, not a redesign.
- **Negative-regard decay.** Should a grudge fade as fast as fondness? Atoms' aversion poles
  are `reset-on-percept` (fear) — a grudge might warrant a *slower* decay than positive regard
  (resentment lingers). Captured as a possible asymmetric rate; default symmetric until L3/L4
  give grudges behavioral teeth worth tuning.
