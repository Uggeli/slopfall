# S2 — Emotion: two-pole drives + `Affects` + emotion-as-controller

**Goal.** Give the agent emotions as a *mechanism*, not a hand-coded reaction. A drive becomes
**(level pole, target field)**; **emotion is the residual** between them; it biases the
membrane's attention (S1) and **prunes/weights the marketplace** (the controller rung between
reflex and full-V). Once this exists, the grudges and gratitude we hand-coded (`±0.x` impulses)
become *emergent residuals*, and directed emotions (fear-of-X, resentment-of-Y, loneliness)
fall out of one shape.

**Atoms grounding.** Drive doc: a drive is a **transient relation** minted from a persistent
**pole** (homeostatic level) + a **target field** (what it's about), felt as the **residual**;
*motivation = internal state × incentive stimulus*. Two classes: physical (arousal-led, e.g.
hunger) vs **mental/directed** (target-led: fear-of-X, social-bond-with, curiosity-about).
Emotion **arises** from the gap, **fills** the missing pole (completes ambiguous percepts), and
is a **shortcut** (somatic marker) that prunes the market so the obvious verb wins without a full
V sweep — the rung between reflex and ODD-plan. Spirals (maintenance/runaway) are pipelined, not
cyclic, and damp via act/decay/hysteresis.

**Makes emergent.** Grudge/gratitude/resentment/loneliness (and, with a threat source later,
fear) — all become residuals over the same poles+targets, written to memory/dossier, instead of
per-interaction magic numbers.

---

## Current state (anchors)

- `NeedsRegistry` (`Registries/NeedsRegistry.cs`): 5 **scalar deficit poles** (Hunger, EnergyDef,
  SocialDef, CoinDef, GoodsDef) — levels only, **no target field**.
- `DriveCatalog` (`Systems/DriveCatalog.cs`): `DriveDef {Weight, DriftPerHour, Satisfaction,
  Prepotent}` — the authored drive table. `OddSystem.PrepotencyGate` is the graded leisure
  suppression (no hard-cull yet, no DAG).
- The hand-coded "emotions": `RequestSystem.Resolve` deposits literal `+0.3/−0.2` regard
  (gratitude/resentment); `SocialSystem` grows regard by an `Affinity` hash; `PerceptionSystem`
  emits dislike→`DislikeNearbyEvent` which `NeedsSystem` turns into social discomfort. These are
  *directed emotions, hard-coded one at a time.*
- S1's `SubjectiveView` (prereq): the per-entity reads emotion will bias (attention) and read
  (valence).

## The gap

Drives are scalar levels with no targets, so "what I feel *about whom*" has nowhere to live
except hand-written impulses. There is no `Affects` register and the marketplace is never pruned
by feeling — only the full uniform-V runs. So every directed emotion is a bespoke system, and
none compose.

---

## Design

### S2.1 — Target field on drives + the `Affects` register

Extend the drive model so a drive instance is `(poleLevel, target)`. Physical drives
(hunger/energy) keep `target = null` (the affordance supplies it). **Mental/directed** drives
carry a target field over entities:

- **social-bond** → bonded others (the positive pole of the dossier),
- **resentment / aversion** → disliked others (the negative pole),
- **fear-of-X** → threats (dormant in the town: no threat source until creatures/crime; build
  the shape, leave it unfired — Atoms says project fear via MAX, social via SUM).

```csharp
// Assets/Sim/Registries/AffectsRegistry.cs (new) — per-agent emotional state = residuals
public struct Affect { public EntityId Target; public AffectKind Kind; public double Intensity; }
// AffectKind: Loneliness, Gratitude(toward), Resentment(toward), Fear(toward) [dormant], ...
public sealed class AffectsData { public List<Affect> Active; public double Arousal; }
```

An `AffectsSystem` mints affects each tick from poles + targets + S1's `SubjectiveView`: e.g.
SocialDef high + a bonded other absent ⇒ loneliness; a refusal event ⇒ a resentment residual
toward the refuser; an alms received ⇒ gratitude toward the giver. **Intensity is a function of
stakes/arousal**, not a literal constant — that is the whole point.

### S2.2 — Emotion-as-controller (the missing rung)

`Affects` feed two places, exactly per the drive doc:

1. **Bias the membrane (S1) attention:** raise the attention channel of an affect's target
   (loneliness raises bonded others; resentment/fear raise the disliked/threatening) — so who
   you feel about is who you notice. (Tunnel-vision and rumination emerge from the top-K
   bottleneck later.)
2. **Prune/weight the marketplace:** before the full uniform-V, a strong directed affect **culls
   or down-weights** ads (resentment-of-C culls/penalises ads that put you near C; loneliness
   lifts social ads toward a bonded other). This is the **shortcut rung** — it replaces L3's
   `RelationFactor` place-modulator with an *affect-driven* one (regard becomes one input to the
   affect, not a direct multiplier).

### S2.3 — Re-express interactions as residuals (retire the magic numbers)

The greet/help/refuse interactions stop emitting literal `±0.x` `RelationImpulseEvent`s. Instead
each is an **event with stakes**; `AffectsSystem` mints the residual (gratitude/resentment),
whose intensity scales with arousal, and that residual is what **encodes to memory and updates
the dossier valence** (the THINGS target-field store). So `RequestSystem`/`SocialSystem`/
`PerceptionSystem` emit *what happened* (HelpGranted/Refused/Greeting/Met), and the emotion layer
decides *how much it moves regard* — uniformly, for every interaction kind.

This is where the **begging social-souring** dissolves cleanly: a routine street-refusal is a
*low-arousal* event → a small residual → little souring; a betrayal by a friend is *high-arousal*
→ a real grudge. The −0.2-per-refusal pathology was a flat constant; emotion makes the magnitude
proportional to stakes, for free.

---

## Staged sub-steps

1. **S2.1** — target field on drives + `AffectsRegistry`/`AffectsSystem` minting residuals
   (computed, not yet consumed). Structure + determinism.
2. **S2.2** — emotion-as-controller: `Affects` bias S1 attention and prune/weight the
   marketplace; re-express L3's place-modulator as affect-driven.
3. **S2.3** — route greet/help/refuse through residuals; **delete** the literal `±0.x` impulses
   in `RequestSystem`/`PerceptionSystem` and the `Affinity`-hash growth in `SocialSystem` (regard
   now accrues from residuals + co-presence).

## What it retires

`RequestSystem.Resolve`'s `+0.3/+0.05/−0.2/−0.02` constants; `PerceptionSystem`'s greet/dislike
`RelationImpulseEvent`s; `SocialSystem`'s `Affinity`-hash regard growth (familiarity-from-
co-presence stays; the *regard* delta becomes an emotion residual). Directed-emotion behavior is
now one mechanism, not three hand-coded sites.

## Acceptance (per-stage gates: mechanism + invariants)

- **Invariants:** determinism (residuals are deterministic functions of poles/targets/percepts;
  `hash(tick,id)` for any draw); `Affects` single-writer; conservation/liveness unaffected;
  "ad is ad" preserved (the affect modulator reads `ScoreContext`, no verb switch).
- **Mechanism fires (direction only):** no literal `±0.x` regard constant remains in any system
  (grep); a refusal still produces *resentment* and an alms *gratitude* — now as residuals; a
  strong directed affect measurably **narrows** an agent's chosen options (prune) in a scenario.
- **Deferred to tuning:** residual intensities, arousal scaling, prune thresholds, hysteresis/
  decay rates of affects, the ignition floor for runaway spirals — all frozen placeholders.

## Risks / open questions

- **Fear is dormant.** The town has no threat source, so fear-of-X is built but unfired until
  creatures/crime exist. Build the shape (target field + MAX projection), don't force a behavior.
- **Spirals.** Runaway affect (resentment/loneliness feeding itself via attention) needs damping
  (decay + hysteresis + discharge-on-act). Keep the damping minimal and let a proto/soak reveal
  if it spirals — don't over-build the full spiral set (proto discipline).
- **Don't let affect swamp needs.** Same guard as L3: the prune/weight colors choice; a starving
  agent still eats. Clamp the affect modulator.
- **Overlap with L3.** S2.2 *replaces* L3's `RelationFactor` (regard→place) with an affect-driven
  modulator; regard becomes an input to the residual, not a direct multiplier. Re-express, don't
  double-count.
