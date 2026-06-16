# S1 — The membrane: `interpret()` → `SubjectiveView`

**Goal.** Make the Atoms membrane real: between raw senses and the marketplace, one per-agent
`interpret()` step that turns *what is sensed* into *what it means to me* — recognition,
valence, trust, attention — written to a `SubjectiveView` register that **every** downstream
system reads. This is the substrate the rest of the track deposits into; on its own it mostly
*unifies and relocates* interpretation we already do ad-hoc, and turns L3's collapsed lens into
a first-class register.

**Atoms grounding.** Bunny doc: *an agent is a private process wrapped in a public molecule*;
between perceive and act sits `interpret()`, producing the **subjective molecule** — same
read-only atoms, opposite reading per agent (bold reads "safe foraging," timid reads "predator
near"). World doc: rows 1a (raw sense) → 1b (attention-filtered percepts) → 2 (`SubjectiveView`:
recognition/valence/trust). Memory doc: *"the MEANINGS store **is** the interpretation
substrate"* — which S1 reads from the dossier now, and from MEANINGS once S3 lands.

**Makes emergent / unifies.** The two places we interpret today — `PerceptionSystem`'s
greet/dislike and L3's `RelationFactor` — become two *reads of one register*. Stigma, pity, and
fear-of-X (S2/S4) have a single place to live.

---

## Current state (anchors)

- `SenseSystem` (`Systems/SenseSystem.cs`) → `SensedRegistry`: raw "who is near, now" (12m,
  grid-LOS, every 5 ticks, only the `IsOutAndAbout`). Pure senses, no meaning. **Keep — this is
  Atoms row 1a.**
- `PerceptionSystem` (`Systems/PerceptionSystem.cs`): the ad-hoc `interpret()`. Reads
  `SensedRegistry` + `RelationsRegistry`, emits `GreetingEvent` (regard≥0.3 & fam≥0.25) and
  `DislikeNearbyEvent` (regard≤−0.3). Its own comment calls dislike *"the first directed
  emotion."* Bespoke thresholds + cooldowns.
- L3's lens in `OddSystem.Decide` (`OddSystem.cs`): `RelationFactor(MeanRegardAt(building))`
  reads the dossier (`ScoreContext.Dossier`) — `interpret()` **collapsed into the decider**.
- `RelationsRegistry` (the dossier): per-`Other` `{Familiarity, Regard, FriendAnnounced}` — the
  Atoms THINGS store, the per-entity valence we already hold.

## The gap (in Atoms terms)

There is no `SubjectiveView` register and no single `interpret()`. Interpretation is **scattered
and duplicated** (PerceptionSystem does greet/dislike; OddSystem does the regard-lens), each with
its own thresholds. So there is nowhere for S2's emotion to bias attention, nowhere for S3's
MEANINGS to deposit recognition/valence, and nowhere for S4's conscience to read self-valence.
The membrane is the missing shared surface.

---

## Design

### S1.1 — The `SubjectiveView` register + `SubjectiveSystem`

```csharp
// Assets/Sim/Registries/SubjectiveViewRegistry.cs (new) — per-agent
public struct EntityRead          // one interpreted entity (Atoms row 2 output)
{
    public EntityId Other;
    public double Valence;         // good/bad TO ME  (S1: dossier regard; S3: MEANINGS lookup)
    public double Recognition;     // 0 stranger .. 1 well-known  (S1: familiarity)
    public double Trust;           // how much I credit my read   (S1: familiarity; S3: confidence)
    public double Attention;       // salience this tick (S1: proximity·|valence|; S2: +affect bias)
}
public sealed class SubjectiveViewData { public List<EntityRead> Entities; }   // top-K, attention-ordered
```

A `SubjectiveSystem` (promote `PerceptionSystem`), registered **after `SenseSystem`**, rebuilds
each agent's `SubjectiveView` from `SensedRegistry` + the dossier (+ MEANINGS in S3): for each
sensed `other`, `interpret(self, other)` →

- `Valence = dossier.Regard(other)` (S1 placeholder; S3 makes it a MEANINGS lookup that the
  *dossier delta* adjusts),
- `Recognition = clamp(Familiarity/MetBar)`, `Trust = Familiarity`,
- `Attention = proximityWeight · (baseSalience + |Valence|)` — kept to a top-K (Atoms'
  capacity-limited attention; the bottleneck that yields tunnel-vision later).

Pure reads, key-ordered, deterministic. Built on the sense cadence (every 5 ticks); consumers
read last refresh (perceptual staleness — Atoms-acceptable).

### S1.2 — The decider reads `SubjectiveView` (retire the collapsed lens)

`OddSystem.Decide` already builds the lens from the dossier (L3). Re-source it: `MeanRegardAt`
becomes the mean **`SubjectiveView.Valence`** over a place's occupants, not the raw dossier
regard. `RelationFactor` is unchanged (still the clamped uniform modulator — "ad is ad" holds);
it just reads the membrane instead of reaching into `Relations` directly. The dossier is now
read in **one** place (the `SubjectiveSystem`), not two.

### S1.3 — Percepts fall out of the view (retire bespoke greet/dislike)

The greet/dislike interrupts become **reads of `SubjectiveView`**, not separate threshold logic:
greet a recognized high-valence other in range; flinch from a strongly-negative-valence one.
`PerceptionSystem`'s hand-set `GreetRegardBar`/`DislikeBar` collapse into "valence above/below a
bar on the interpreted read." The greet/dislike `RelationImpulseEvent`s stay for now (S2 turns
*all* such impulses into emotion residuals).

### Forward-compatibility (why the shape is right)

- **S2** writes an `affect` bias into `Attention` (fear/loneliness raise their channels) and
  reads `Valence` to prune the marketplace.
- **S3** replaces `Valence`/`Recognition`/`Trust` with MEANINGS lookups (prototype match →
  concept → valence/confidence); the dossier becomes a *delta* over the recognized category.
- **S4** adds a *self-read*: `interpret(self, candidateAction)` → anticipated valence (shame),
  the same machinery pointed at a forecast.

So `EntityRead{Valence,Recognition,Trust,Attention}` is exactly Atoms' `interpret()` output; the
later stages fill the fields with richer sources without changing the shape.

---

## Staged sub-steps

1. **S1.1** — `SubjectiveViewRegistry` + `SubjectiveSystem` building per-entity reads
   (valence=regard placeholder). Built but unused by consumers; verify determinism + top-K cost.
2. **S1.2** — re-source `OddSystem`'s `MeanRegardAt` to `SubjectiveView.Valence`; delete the
   direct `Relations` read in the lens.
3. **S1.3** — derive greet/dislike from `SubjectiveView`; retire `PerceptionSystem`'s bespoke
   thresholds (fold the system into `SubjectiveSystem`, or leave it as a thin reader).

## What it retires

L3's `RelationFactor`-reaches-into-`Relations` (now reads the membrane); `PerceptionSystem`'s
`GreetRegardBar`/`DislikeBar`/duplicate sensed-iteration (now one interpreted read). Net: the
dossier is interpreted **once**, in one system.

## Acceptance (per-stage gates: mechanism + invariants)

- **Invariants:** determinism preserved (pure reads, key-ordered); "ad is ad" holds (the lens
  still reads `ScoreContext`, no verb switch); `SubjectiveView` is single-writer
  (`SubjectiveSystem`).
- **Mechanism fires (direction only):** two agents with opposite histories toward C produce
  opposite-sign `Valence` for C in their `SubjectiveView` (a unit/scenario check); the decider's
  place score and the greet/dislike percepts both derive from that one view (grep: no system
  reads `Relations` for interpretation except `SubjectiveSystem`).
- **Deferred to tuning:** attention top-K size, salience weights — frozen placeholders.

## Risks / open questions

- **Cost/cadence.** Building `SubjectiveView` for every out-and-about agent each sense-tick is
  O(sensed-set); bounded by the 12m neighbourhood. Amortise (cursor) only if the soak shows it
  hot — don't preoptimise.
- **Decision vs sense cadence.** The decider reads the last sense-tick's view (≤5 ticks stale) —
  perceptual staleness, which Atoms treats as honest, not a bug.
- **Scope creep.** S1 is *plumbing*. Resist adding emotion (S2) or MEANINGS (S3) here — valence
  stays the dossier scalar until those stages, or the order collapses.
