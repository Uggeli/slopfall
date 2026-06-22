# Per-Atom Strength: Importance Lives on the Atom, Not the Record (Design)

**Date:** 2026-06-22
**Status:** ready-for-review (code-mapped; ready for plan).
**Depends on:** memory core A1–A5, PLACES memory Phase 1 (write/seed layer).
**Part of:** memory core — a structural refinement that unblocks PLACES Phase 2 (ODD scoring) and
generalises to THINGS/EVENTS.

## The idea

Today `MemoryRecord.Strength` is **one byte per record** doing all three jobs the architecture
assigns it — write-depth, decay target, eviction order — and `Flags` (INNATE/SURPRISE) are
per-record too. That is correct for a coherent *dossier* (THINGS/EVENTS: one entity, one
importance), but wrong for a **bundle of heterogeneous facts**. A PLACES record for a building holds
`{BuildingKind, ProvisionsHere, DangerHere}` whose importances differ by orders of magnitude:
`BuildingKind` is structural (a resident always knows the tavern is the tavern), `DangerHere` is
survival-grade and flashbulb-etched, `ProvisionsHere` is ordinary learned fact. One record strength
cannot decay them at three different rates — so PLACES Phase 1's coarse decay erodes a whole
building's knowledge overnight (`rec/agent 128 → 6.7` in the 1-day soak).

**Move strength (and the INNATE/SURPRISE flags) from the record to the atom**, across all three
stores. Importance stays exactly what the canonical doc already says it is — *strength* — but now
each remembered fact carries its own. Decay becomes per-atom and **rate-scaled by importance**:
important atoms barely erode, trivial ones fade fast. This reproduces the doc's own target —
*"the calm fox stays vivid while a thousand ordinary chases blur away"* — at atom granularity.

> Vocabulary note (from `what_is_memory.md`): *"strength is the one scalar doing three jobs… already
> the importance proxy the whole design maintains."* We do not add a second "importance" field; we
> push the existing one down a level. And *"Surprise = per-atom prediction error"* — per-atom surprise
> already exists, so per-atom write-strength is a wiring of existing signal, not a new concept.

## Decisions (locked)

1. **Strength + flags go per-atom, parallel to the bag — `Atom` stays clean.** `Atom` remains the
   immutable `{Type, Value}` shared substrate (percepts, predictions, deltas all use it). The
   per-atom strength/flags live on the *record* as arrays parallel to `DeltaBag.Atoms`, never on
   `Atom` itself.
2. **Importance = strength (no new concept).** Seeded from an intrinsic per-atom-type **salience**,
   raised by reinforcement (refresh on re-observe), eroded by decay. INNATE atoms (structural facts
   like `BuildingKind`) are decay/evict-immune.
3. **Decay rate falls as strength rises** — integer/fixed-point, deterministic:
   `dec = max(1, base · (MAX − strength) / MAX)`; INNATE immune; an atom at 0 is dropped from the
   bag; a record with an empty bag dies. (Continuous, per the user's "important ≈ none, trivial ≈
   fast", but integer math — the doc requires integer decay for replay-exactness.)
4. **Record-level strength is derived, for eviction only.** Eviction still works at record grain
   (evict the weakest record when a store is full); a record's eviction-strength = **max** of its
   atoms' strengths (a building is as keepable as its most important fact). No second scoring system.
5. **All three stores** (PLACES/THINGS/EVENTS) adopt per-atom strength in one pass. THINGS/EVENTS go
   through the re-diff/mint/decay consolidation machinery, so that machinery is updated to carry
   per-atom strength through RE-DIFF (atom drop keeps its peers' strengths), MINT (minted/​re-keyed
   records get per-atom strength), and DECAY (now per-atom).
6. **Write-depth is per-atom**, sourced from per-atom surprise × arousal × intrinsic salience — each
   atom etched by how much *it* (not the percept as a whole) violated prediction. This also sidesteps
   the doc's open MAX-vs-SUM aggregation question for the *write-strength* site (each atom keeps its
   own; aggregation is only still needed for the attention spike).
7. **The record-WRITE gate is unchanged.** Whether a record is written still uses the existing
   `surprise.Encode (MEAN) > θ_s  OR  arousal > θ_a` test — per-atom strength only changes *how
   strongly each atom is etched once we write*, not *whether* we write. (Per-atom strength happens to
   dissolve the doc's "MEAN gate etches trivia at full strength" worry for free, but we don't touch
   the gate's MAX-vs-MEAN choice here — orthogonal, keeps encode behaviour stable.)
8. **The decay operator changes shape (deliberately), not just its home.** Going from flat
   `strength -= rate` to strength-scaled `dec = max(1, base·(MAX−S)/MAX)` is the mechanism your
   "important ≈ no decay" asks for. It generalises the current operator (at S=0 it *is* the old flat
   rate; at S=255 it's ≈0). This shifts exact record/atom lifetimes in **all** stores
   (THINGS/EVENTS too) — but the architecture only owes the *ordering* (doc: "rates are config"), and
   the load-bearing invariant **"confirming dissolves faster than surprising" is preserved** (a
   confirming atom starts lower / re-diffs away; a SURPRISE atom uses the smaller base AND starts
   high, so it plateaus). Tests that pin exact post-decay numbers get new expected values; tests that
   pin the *ordering* keep passing.

## Architecture

### Data structure — `MemoryRecord` gains parallel strength/flags

```
MemoryRecord {
  MemoryKey      Key;
  CategoryId     CategoryRef;
  AtomBag        DeltaBag;     // {Type,Value} atoms, sorted (unchanged)
  byte[]         Strengths;    // NEW — per-atom importance, index-aligned to DeltaBag.Atoms
  MemoryFlags[]  AtomFlags;    // NEW — per-atom INNATE/SURPRISE, index-aligned
  long           WrittenAt, LastRefresh;
}
```
- `Strengths[i]`/`AtomFlags[i]` describe `DeltaBag.Atoms[i]`. Arrays stay index-aligned through every
  bag transform (Merge/Diff/Decay rebuild both together).
- Derived: `EvictionStrength => max over i of (IsInnate(i) ? 255 : Strengths[i])`. INNATE ⇒ a record
  that never evicts (matches today's record-level INNATE immunity).
- Helpers replacing `WithStrength`: rebuild-with-new-strengths; per-atom refresh; drop-atom-at(i).

### Intrinsic salience — a small static table (the species seed for places/atoms)

A per-atom-type salience seeds write-strength and flags. PLACES kinds:

| Atom | Salience / flag | Rationale |
|---|---|---|
| `BuildingKind(*)` | INNATE | structural — a resident permanently knows what a place is |
| `DangerHere` | high (SURPRISE-grade) | survival; *"arousal etches deep"* (flashbulb) |
| `ProvisionsHere` | medium | ordinary learned fact; decays if the place stops provisioning |

THINGS/EVENTS atoms get their write-strength from per-atom surprise × arousal as today, now stored
per-atom rather than collapsed to one record strength. ⟨TBD: exact salience source for THINGS/EVENTS
atoms — confirm against MemoryEncoder.⟩

### Decay operator (per-atom, integer, deterministic)

```
for each atom i in record:
    if AtomFlags[i] has INNATE: continue            // immune
    base = (AtomFlags[i] has SURPRISE) ? r_resist : r_normal
    dec  = max(1, base * (255 - Strengths[i]) / 255)  // important ⇒ ~1, trivial ⇒ ~base
    Strengths[i] -= dec
    if Strengths[i] <= 0: mark atom i for drop
drop marked atoms (rebuild DeltaBag + Strengths + AtomFlags together, key order preserved)
if DeltaBag empty: drop the record
```
Determinism: integer subtraction, key-order iteration, no floats (the doc's Determinism section).

### How the existing machinery adapts (code-mapped)

- **Encode (`MemoryEncoder.Perceive`, MemoryEncoder.cs:36-68).** Today: gate on `Encode>θ_s OR
  arousal>θ_a`, then one record strength `= Scale(max(surprise.Encode, arousal))` + one `Flags`. New:
  same gate; then for each atom `i` of the delta bag (`Diff(percept, prediction)`), per-atom
  `error_i = |percept_i − prediction_i|` (the value `Surprise.Against` already iterates internally),
  `Strengths[i] = Scale(max(error_i, arousal))`, `AtomFlags[i] = SURPRISE if error_i>θ_s else None`.
  Novel record (`CategoryRef.None`): every atom SURPRISE at strength 255 (verbatim, maximal — as
  today, now per-atom). `Surprise.Against` (Surprise.cs:21-41) gains an out of per-(delta-)atom error
  so the encoder doesn't recompute it.
- **RE-DIFF (Consolidation.cs:19-40).** `AtomBag.Diff(rec.DeltaBag, prediction)` already drops
  absorbed atoms; now the rebuild drops their `Strengths[]`/`AtomFlags[]` slots in lockstep (a small
  index-aligned `Diff` that returns the surviving indices). Survivors keep their per-atom strength.
  Record dies when the bag empties (unchanged, just no single `rec.Strength` to carry).
- **MINT (Consolidation.cs:49-93).** Re-keys members and re-diffs shared atoms away; identical to
  RE-DIFF for strength handling — survivors keep per-atom strength; minted node carries no record
  strength (MeaningsStore is strength-agnostic — confirmed, §6 of the map).
- **Recall / reconsolidation (`MemoryRecall.cs:24-32` + `MemoryStore.Refresh`).** Recall reconstructs
  the whole bag, so reconsolidation refreshes the whole trace: bump **every non-INNATE atom's**
  strength by `refreshDelta` (clamped 0..255), set record `LastRefresh`. (Refresh stays a per-record
  call; it just walks the atoms.)
- **Eviction (`MemoryStore.cs:117-142`).** `LessKeepable` swaps `a.Strength` for the derived
  `EvictionStrength` (max over atoms, INNATE⇒255); tie-breaks (LastRefresh, Key) unchanged. INNATE
  skip becomes "skip records whose `EvictionStrength==255` via an INNATE atom" — i.e. a record with
  any INNATE atom never evicts (matches today).
- **Decay (`MemoryStore.cs:96-110`).** Replaced by the per-atom operator above; signature stays
  `Decay(normalRate, surpriseRate)` (the two bases), so `Consolidation.Pass` and the
  `AgentMemoryRegistry` PLACES-decay call I added in Phase 1 need no signature change.
- **`MemoryRecord` API.** `WithStrength(byte,long)` → `WithAtomStrengths(byte[], long)` (and a
  per-atom `WithRefreshed(delta, tick)` helper); `IsInnate`/`IsSurprise` become per-index queries
  `IsInnate(i)`/`IsSurprise(i)` plus record-level `AnyInnate`. Construction takes the two parallel
  arrays. *(Impl note for the plan: the two arrays may fold into one `AtomMeta[] {byte Strength;
  MemoryFlags Flags;}` index-aligned to `DeltaBag.Atoms` — safer against drift — but the on-record
  shape stays "per-atom strength + flags" either way.)*

## Data flow

```
encode   -> per-atom strength = f(per-atom surprise, arousal, salience); per-atom flags
observe  -> Merge new atoms into a record's bag, carrying per-atom strengths (re-observe refreshes)
sleep    -> RE-DIFF (drop absorbed atoms+slots) -> MINT -> per-atom DECAY -> drop dead atoms/records
evict    -> when a store is full, evict the record with lowest derived EvictionStrength
read     -> (Phase 2) ODD reads a building's bag; trivia has already faded, danger/kind persist
```

## Validation

- **New unit tests (the per-atom contract):** per-atom decay drops a trivial atom while an
  INNATE/high-salience peer in the SAME record survives; record dies only when its bag empties;
  strength-scaled decay (`dec` smaller at high strength) verified at two strengths; `EvictionStrength
  = max` picks the right record to evict; recall refreshes all non-INNATE atoms; merge of two place
  bags carries per-atom strengths.
- **Test migration (code-mapped — these pin record-level strength/flags and must move per-atom):**
  - *Becomes per-atom, assertions rewritten:* `MemoryRecordTests` (WithStrength, IsInnate/IsSurprise),
    `MemoryStoreEncodeTests` (`.Strength` reads), `MemoryRecallTests`
    (`Recall_Reconsolidates_BumpsStrength`).
  - *Becomes per-atom AND new decay numbers (operator shape change):* `MemoryStoreRefreshDecayTests`
    (the 5 Decay/Refresh tests — flat-rate expectations → strength-scaled), `ConsolidationPassTests`
    (`ConfirmingRecord_DissolvesFasterThanSurprising`, `RepeatedPasses_KeepStoreBounded`) — keep the
    *ordering* assertions, update exact strengths.
  - *Single-atom records — assert via the atom / EvictionStrength:* `MemoryEncoderTests` (the
    `Strength<64` / `>200` / `==255` etch-depth tests map to the one atom's strength).
  - *Eviction by derived strength:* `MemoryStoreEvictionTests`, `AgentMemoryStoresTests` (INNATE
    survives) — INNATE-immunity preserved via `EvictionStrength`.
  - *Unaffected (no strength assertion):* `ConsolidationReDiffTests` (drop-when-empty),
    `ConsolidationMintTests` (delta-shrink), `PlaceMemoryWriteTests`, `PlaceSeedingTests`,
    `PlaceDangerTests` — but PLACES seeding now sets per-atom salience (Kind=INNATE, Danger=high,
    Provisions=medium) instead of the flat `PlaceStrength=200`, so add an assertion that a seeded Kind
    atom is INNATE.
- **Soak (PLACES, the payoff):** the 1-day soak no longer collapses `rec/agent` overnight — building
  kind persists (INNATE), provisions persist while real, danger fades when the place goes quiet.
  Aggregate behaviour still == baseline (PLACES unread by ODD until Phase 2).
- **Determinism:** the serial == parallel fingerprint still holds (integer per-atom math, key-order
  iteration, no floats) — the refactor must not introduce a divergence.

## Scope / deferred

- **In:** per-atom `Strengths`/`AtomFlags` on `MemoryRecord`; the intrinsic-salience seed; per-atom
  decay operator; encode/​re-diff/​mint/​recall/​eviction updated; all three stores; test migration.
- **Deferred:** ODD reading PLACES (Phase 2, its own plan); personality-split salience thresholds;
  per-atom surprise-aggregation knob tuning (no premature tuning — build the mechanism first).
