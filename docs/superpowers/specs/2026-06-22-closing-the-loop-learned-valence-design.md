# Closing the Loop: Learned Social Valence Drives Behavior (Design)

**Date:** 2026-06-22
**Status:** approved-pending-review, ready for plan.
**Depends on:** agent memory write+consolidation ([[memory-integration-via-odd-ads]] piece 2), perceivable atoms, memory core.
**Part of:** memory integration (Phase B), **piece 3** — the `interpret`/`afford` read of the bunny-doc loop. This is the piece where behavior changes.

## The idea

Close the loop: agents' reading of a perceived stranger comes from **their own learned memory** (the
new `MeaningsStore`) instead of the old shared role-scalar. Two parts: teach the new categories
*feeling* from interaction outcomes (reinforcement), then have `Interpret` read that feeling
(repoint). Behavior shifts gradually as agents accumulate learned, reinforced categories.

Per `docs/what_is_a_bunny.md`: "the MEANINGS store IS the interpretation substrate"; recognition /
valence / trust are memory lookups. This piece makes `Interpret` a lookup into the *new* store.

## Decisions (locked)

1. **Reinforcement source = the 4 social-outcome events** the old `MeaningsSystem` already folds:
   `HelpGrantedEvent` (+), `HelpRefusedEvent` (−), `GreetingEvent` (mild +, both ways),
   `DislikeNearbyEvent` (−). Outcomes mirror the old constants (`GrantOutcome`/`RefuseOutcome`/
   `GreetOutcome`/`DislikeOutcome`) so the *learning signal* is unchanged — only the store it lands in
   is new.
2. **Reinforce the recognized category.** For an outcome between `self` and `other`: look up `other`'s
   signature (identity atoms of `Perceivable.Bag(other)`), `Recognize` it in `self`'s `MeaningsStore`;
   if recognized, `Reinforce(catId, signature, outcome)`. If not yet a category (early days), skip —
   feeling accretes only once perception+consolidation has minted the category.
3. **Repoint = confidence-weighted blend, not hard replace.** In `Interpret`, the stranger
   `baseValence` becomes `lerp(oldCategoryValence, newCategoryValence, newConfidence)` — where
   `newCategoryValence`/`newConfidence` come from recognizing `other` in `self`'s new store. Confidence
   0 (unknown) → exactly today's behavior (no regression); confidence 1 (well-learned) → fully the
   agent's own learned feeling. Honors the doc's "confidence weights how much the category colors."
4. **Old `MeaningsSystem`/`MeaningsRegistry` stay** (parallel) — not deleted. The blend falls back to
   them; they remain the day-1 signal. Retiring them is a later cleanup once the new path is trusted.
5. **Gradual by design.** Behavior diverges over days, not ticks. Validation is a multi-day soak
   comparison, not a 1-day histogram.

## Architecture

### Reinforcement (Part 1 — additive, no behavior change)

- New intent `MemoryReinforceIntent { EntityId Perceiver; AtomBag Signature; Fixed Outcome; }`.
- `AgentMemoryRegistry.Update` handles it: `mem.Meanings.Recognize(Signature)` → if not `None`,
  `mem.Meanings.Reinforce(catId, Signature, Outcome)`.
- New `MemoryReinforceSystem : SimSystem` (reads `Perceivable` + the 4 event types): for each event,
  resolve the (self, other, outcome) triple, read `other`'s identity signature from `Perceivable`, emit
  `MemoryReinforceIntent`. Slots beside the old `MeaningsSystem` (both read the same events).

### Repoint (Part 2 — behavior changes)

- `MeaningsStore` gains a read helper `bool RecognizedValence(AtomBag signature, out Fixed valence, out Fixed confidence)` —
  `Recognize` → `TryGetNode` → node's `Valence`/`Confidence`; false (and zeros) if unrecognized.
- `SubjectiveSystem` gains `PerceivableRegistry` + `AgentMemoryRegistry` inputs. In `Interpret`, the
  stranger branch becomes:
  ```
  double oldV = MeaningsSystem.CategoryValence(meanings, residency, self, other);   // unchanged fallback
  double newV = 0, conf = 0;
  if (agentMem.TryGet(self, out var mem) && mem.Meanings.RecognizedValence(SignatureOf(other), out var v, out var c))
      { newV = v.ToDouble(); conf = c.ToDouble(); }
  baseValence = oldV * (1 - conf) + newV * conf;     // confidence-weighted blend
  ```
  where `SignatureOf(other)` = identity atoms of `Perceivable.Bag(other)`.
- Everything downstream (the EntityRead, ODD's `RegardFieldAt`/`V()`) is unchanged — it already consumes
  `baseValence`. (The `OddSystem` *occupant-at-destination* `CategoryValence` read stays on the old store
  for now — a noted follow-up; the primary sensed-stranger path is `Interpret`.)

## Data flow

```
social outcome (HelpGranted/Refused/Greeting/Dislike)
   ├─ old: MeaningsSystem folds role->valence (unchanged)
   └─ new: MemoryReinforceSystem -> MemoryReinforceIntent
            AgentMemoryRegistry: Recognize(other-sig) -> Reinforce(category valence)     [Part 1]

perceive stranger -> Interpret: baseValence = lerp(oldV, newV, newConfidence)            [Part 2]
                     -> EntityRead -> OddSystem score -> activity (behavior shifts as agents learn)
```

## Validation

- **Unit:** reinforce path (recognized category's valence moves toward outcome; unrecognized = no-op);
  `RecognizedValence` helper; the blend math (conf 0 = old, conf 1 = new).
- **Multi-day soak (ARENA2):** run ~3–5 game-days. Expect: new-store mean |valence| rises above 0
  (categories gain feeling via reinforcement); the activity histogram **starts** matching baseline
  (conf≈0 day 1) and **drifts** over days as confidence grows. Report a "blend lean" metric (mean
  confidence) so the migration is visible. No day-1 regression (assert day-1 shape ≈ baseline).

## Scope / deferred

- **In:** reinforcement wiring + the `Interpret` blend + validation.
- **Deferred:** retiring the old `MeaningsSystem`/`MeaningsRegistry`; the `OddSystem` occupant-valence
  repoint; feeding `Recognition`/`Trust` (beyond valence) into the EntityRead; recall (Door 1) and
  place/threat-memory ad generation; arousal.
