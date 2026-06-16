# L3 — The membrane: SubjectiveView & memory-as-interpretation

**Goal.** Give each agent its own *subjective view* so the same world affords different
behavior to different agents — and do it by the one mechanism Atoms says makes interpretation
per-agent: **memory**. Today the pipeline is `senses → ads → uniform V`, where `V` is
relation-blind and memory-blind, and town knowledge is seeded omnisciently. Insert the missing
`interpret()` step and read the per-agent dossier into scoring — **without breaking the "ad is
ad" uniform-`V` invariant** (O0).

**Atoms grounding.** Bunny doc: *"an agent is a private process wrapped in a public molecule"*;
between raw senses and the affordance market sits `interpret()`, producing the **SubjectiveView**
(the *subjective molecule*) — same `scent=fox` atom, bold bunny reads "safe foraging ground,"
timid bunny reads "predator near." Memory doc, the load-bearing line: *"the MEANINGS store …
**is** the interpretation substrate. Recognition / valence / trust are direct semantic
lookups."* So **subjective view and the memory→decision loop are the same task** — memory is
*how* a read becomes per-agent.

**Soak/architecture finding addressed.** `MemoryRegistry` is written (Met/ReceivedHelp/
WasRefused/…) and **never read by deliberation** (grep: `OddSystem`/`ActionDiscovery` touch
neither `Relations` nor `Memory`). The directional `RelationData` is symmetric-driven (the
`Affinity` force is symmetric) and inert. Both are epiphenomenal — measured by the census,
displayed as friendships, but they never change what an agent *does*.

---

## Current state (anchors)

- **The decider** `OddSystem.Decide` (`OddSystem.cs:77`): builds a `ScoreContext` **once**
  (`:111`), then `V(ad, sc)` per ad (`:194`), argmaxes with a `Sticky=1.4` incumbent bonus
  (`:120-129`), writes `IntentData` (`:167`).
- **`ScoreContext`** (`:180`): `Needs[]`, `W[]` (personality weights), `Person`, `Hour`,
  `Night/Wet/Holiday`, `Outdoor`, `Cozy`, `Prepotency`, `Px/Pz`. **No relation/memory field.**
- **`V`** (`:194-214`): a uniform gap-score — `OddScore.Compute(needs, Spec.Delta, W, gate,
  base)` where `gate` is a product of **data-flagged** soft modulators
  (`BaseGate·Day/NightGate·DistFactor·Outdoor·Social·Prepotent·HolidayFactor·trait`). **No
  `switch(ad.Verb)`** — this is the "ad is ad" property: `V` reads `Ad.Spec` data + `ctx`, and
  hard gates live in `Preconditions` (checked in `GatherAds`, not here).
- **`Ad`** (`Systems/Affordance.cs`): `{ Verb, Building, X, Z, Spec }`. **Place-directed only —
  no person `Target`.** Person interactions (Chat/SeekHelp) arrive as *interrupts*
  (`PerceptionSystem`, `RequestSystem`), not as ads.
- **The dossier** `RelationData` (`RelationsRegistry.cs:9`): per-`Other` `{Familiarity, Regard,
  FriendAnnounced}` — the THINGS-store valence, *already directional*, and (post-L1)
  *already decaying*. Some interaction-driven writes already exist: `RequestSystem` emits
  `RelationImpulseEvent` (+0.3 `ReceivedHelp`, −0.2 `WasRefused`, …) applied in
  `SocialSystem.ProcessEvents`.
- **Who-is-where** `OccupancyRegistry` (`ctx.Occupancy.CompanyOf` is already used by
  `NeedsSystem` to gate Social relief) → the hook for relation-conditioning a *place* ad by its
  current occupants.

## The gap (precisely)

The **read** side is missing. The dossier is the interpretation substrate and (after L1) is
even a *live, decaying* one — but nothing reads it into `V`. So:

1. No `interpret()` step — agents afford against objective truth.
2. `V` cannot express "this place is where my friends are" or "avoid the one who refused me."
3. Therefore two agents with totally different histories score the *same* ad identically.

L3 is mostly a **read-path** change. We do **not** need a full per-tick percept rewrite; we
need a subjective *lens* the decider consults, and a uniform modulator that reads it.

---

## Design

### L3.1 — The subjective lens on `ScoreContext` (the `interpret()` step, collapsed)

Atoms' `SubjectiveView` is a per-tick pipeline register. Our deliberation is **event-driven**
(decide on activity-end / hourly), not per-tick, so building a per-tick `SubjectiveViewRegistry`
for every agent would be costly **and premature** (proto discipline: don't build the registry
until per-tick percepts need it). The faithful depth-1 realization: compute the lens **lazily
inside `Decide`**, per-agent, only when deciding. Document it explicitly as *the `interpret()`
step, collapsed into the decider for the depth-1 subset*; promoting it to a standalone
`SubjectiveSystem` + `SubjectiveViewRegistry` is the O1+ convergence step when percepts become
per-tick.

```csharp
// built once per Decide, added to ScoreContext
struct SubjectiveLens
{
    public RelationsData Dossier;        // ctx.Relations[self] — per-Other valence (THINGS store)
    public Dictionary<EntityId,double> MemoryValence; // distilled from ctx.Memory[self] recent episodes
    // interpret(other) := valence of `other` to me
    public double Regard(EntityId other) =>
        (Dossier != null && Dossier.Of.TryGetValue(other, out var r) ? r.Regard : 0.0)
        + (MemoryValence != null && MemoryValence.TryGetValue(other, out var m) ? m : 0.0);
}
```

`MemoryValence` is the memory doc's "valence is a semantic lookup" made concrete on our thin
store: walk `ctx.Memory[self].Entries` (bounded ring, ≤32) and sum a per-kind weight by `Other`
(ReceivedHelp `+`, GaveHelp small `+`, WasRefused `−`, RefusedToHelp small `−`, Met/BecameFriend
neutral/`+`). Recent-weighted if desired (entries are newest-first). Pure read, no RNG.

### L3.2 — A uniform relation modulator in `V` (preserving "ad is ad")

Add **one** modulator to the `gate` product, gated by a **data flag** on `Spec` exactly like
`Social`/`Outdoor`/`Prepotent` — *not* a verb switch:

```csharp
// in V(ad, c): same uniform form, new data-flagged term
* (s.RelationSensitive ? RelationFactor(ad, c) : 1.0)
```

```csharp
double RelationFactor(Ad ad, ScoreContext c)
{
    // PLACE ad: interpret the place by who is there now (Atoms: the molecule reads through
    // the company present). Mean of my regard toward current occupants.
    if (ad.Building >= 0)
    {
        double sum = 0; int n = 0;
        foreach (var occ in c.OccupantsOf(ad.Building))   // ctx.Occupancy, key-ordered
        { sum += c.Lens.Regard(occ); n++; }
        double meanRegard = n > 0 ? sum / n : 0.0;
        return Clamp(1.0 + RelationGain * meanRegard, RelationFloor, RelationCeil);
    }
    // PERSON ad (L4, once Ad.Target exists): interpret the target directly.
    if (!ad.Target.IsNone)
        return Clamp(1.0 + RelationGain * c.Lens.Regard(ad.Target), RelationFloor, RelationCeil);
    return 1.0;
}
```

Why this keeps **"ad is ad"** intact:
- `V` still has **no `switch(ad.Verb)`** — the new term is `f(Ad.Spec.flag, ctx.Lens,
  ad.Building/Target)`, structurally identical to how `Social` reads `Liveliness(ad)` and
  `Outdoor` reads `c.Outdoor`.
- **Same ad + same context ⇒ same score.** The dossier/memory are part of `ScoreContext`, not
  the `Ad`. Different agents differ only because their *context* (their lens) differs — which is
  the entire point of a subjective view.
- Preconditions (the hard gates) are untouched; this is purely a soft `gate` factor.

Flag `RelationSensitive` on the specs where company matters: `Socialize`, `EatTavern`, `Visit`,
`Chat` — *not* on `Sleep`/`Work`/`Farm` (you work and sleep regardless of who's around). That
authoring choice is data, per-activity, like the existing trait-coupling.

### L3.3 — Memory read = the loop closes

L3.1's `MemoryValence` is the operative half of "close the memory→decision loop": a building
where my **refuser** currently sits now scores *lower* for a Socialize ad (avoidance), a
building with a **friend** scores *higher* (seeking) — emergent from the same uniform modulator,
no special-case code. This is also where the symmetric-`Affinity` problem dissolves: regard now
diverges A→B vs B→A because their *memories* of the interaction differ (one received help, one
gave it; one was refused, one refused).

### L3.4 — Surface in the census (`Explain`)

Extend `TownCensus.Explain(ctx, entity)` (the motivations view) to show the relation/memory
contribution to the chosen ad. This is both the acceptance instrument (prove the term fired)
and the human "does it feel right?" check via the live spectator.

---

## Staged sub-steps

1. **L3.1** — `SubjectiveLens` + build it in `Decide`; add to `ScoreContext`. Compute but do
   **not** yet use it in `V` (verify determinism + per-decision cost; the lens is bounded by
   dossier size and the 32-entry memory ring).
2. **L3.2** — `Spec.RelationSensitive` flag + `RelationFactor` place-modulator in `V`; flag the
   handful of social specs. Re-tune is expected (soak/behavioral bands), not a re-baseline of
   magnitude tests — delete those.
3. **L3.3** — wire `MemoryValence` into the lens (it was already structured in L3.1; this
   turns it on in scoring). Confirm A→B vs B→A divergence in a scenario test.
4. **L3.4** — `Explain` surfacing + spectator check.

## Per-stage gates (mechanism + invariants — provable on placeholders, no tuning)

- **Invariants (hard):** determinism preserved (lens is pure reads, no RNG); **"ad is ad"
  holds** — unit test that `V` is a pure function of `(Ad, ScoreContext)`, that two agents with
  **identical** context score an ad identically (the invariant) while two with **different**
  dossiers *can* differ (the feature); the relation modulator is **clamped** so it cannot flip
  a score's sign or swamp needs (a starving agent still eats among enemies — a structural guard
  that holds for *any* clamp value, independent of tuning).
- **Mechanism fires (qualitative, direction-only):**
  - **Scenario (the headline):** A (helped by C) scores a tavern-with-C ad **higher** than B
    (refused by C) — same ad, different agents, different score. Sign, not magnitude.
  - `Explain(ctx, entity)` surfaces a **non-zero** relation/memory term for social activities.

Deferred to the post-L4 tuning phase: `RelationGain`/clamps, `MemoryValence` weights, the
above-chance homophily *rate* over a soak. The directional effect is the gate; its strength is
tuned later, with L4's, on the full stack.

## Numbers (frozen placeholders — tuned only when the whole stack lands)

`RelationGain`, `RelationFloor`/`RelationCeil`, per-kind `MemoryValence` weights, memory recency
weighting, and *which* specs carry `RelationSensitive`. Set placeholders that keep the modulator
a *coloring* term (small relative to the gap-score) and **leave them** — these same numbers rank
directed ads in L4, so they're fit together in the single post-L4 tuning pass.

## Risks / open questions

- **Don't let the social term swamp needs.** The clamp + flooring is load-bearing: relations
  *color* the gap-score, they don't replace it. The "starving agent still eats among enemies"
  test guards this.
- **Occupancy timing.** `RelationFactor` reads *current* occupants; an agent decides to go
  somewhere based on who's there *now*, and they may leave (perceptual staleness — Atoms says
  the wasted journey is the membrane being honest). Acceptable and realistic at depth-1; a
  predictive "who *will* be there" belongs to future ACTION systems, not this read.
- **Promotion to a real `SubjectiveSystem`.** The collapsed-into-decider lens is the depth-1
  wedge. When O1 brings per-tick percepts, promote it to a `SubjectiveViewRegistry` written by a
  `SubjectiveSystem` (the true membrane register), and `V` reads that instead of computing it
  inline. Structure is forward-compatible: `RelationFactor` already reads a `Lens`, not the
  registries directly.
- **Cost.** Lens build is O(dossier + 32) per decision, modulator is O(occupants) per social
  ad. The soak (~7,700 decisions/day) will show if it bites; amortize only if measured.
