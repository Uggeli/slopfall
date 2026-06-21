# Memory Core — Phase A Roadmap (build the dynamics, isolated)

**Date:** 2026-06-21
**Architecture spec:** [`docs/what_is_a_memory.md`](../../what_is_a_memory.md) (pinned; this roadmap is the build decomposition).
**Decision:** Core-first — build + validate the memory dynamics in isolation, then wire into the live
CQRS engine (Phase B). Rationale: the architecture is pinned but the *dynamics* are unsettled (the p6
questions), and the current proto-subset `MeaningsRegistry` is wired into live decisions
(`SubjectiveSystem.Interpret` → `OddSystem`), so the dynamics must be proven before they steer agents.

## Grounding (what exists vs what the spec assumes)

The spec says `deltaBag`/`predicted` *"reuse the stored-atom-bag machinery — no new container."* In this
sim that machinery **does not exist** — it must be built as Phase A's first brick:

| Spec assumes | Sim has today | ⇒ |
|---|---|---|
| general `Atom = (AtomTypeId, value)` | `AtomKind` enum = item property flags, fixed `double` fields on `ItemData` | build |
| sorted, binary-searchable `Atom[]` bag, delta/merge ops | none | build |
| `Signature` = prototype (richer than role) | `int` role-id only (`SignatureOf` = `(int)Residency.Role`) | extend |
| `predicted` = per-AtomType running stats (count/mean/spread, fixed-point) | running-**mean** only, all `double` | build |
| `MemoryRecord` / four stores / strength / eviction | episodic = raw 32-tuple FIFO; meanings = `{Signature,Valence,Confidence}` | build |

`Decay.TowardBaseline` exists and is reused. The route-around test pattern exists (`Sim.FactionTests`
refs Sim.Core/Data/World, not the now-deleted Sim.Net) — Phase A adds a new **`Headless/Sim.MemoryTests`**
in the same mould.

## Where it lives

Core types under `Assets/Sim/` (compiled by `Sim.Core`), as **pure data structures + algorithms with
no live-system wiring** — not added to `SimWorld`'s systems/registries arrays yet. Phase B does the
wiring. Tested in the new `Headless/Sim.MemoryTests`. This mirrors how the faction substrate was built
(types in Sim.Core, tested in their own project, wired into the engine as a separate step).

## Phase A sub-plans (each isolated, green, mapped to a p6 question)

### A1 — Atom substrate + AtomBag (the missing brick)
- `AtomTypeId` (readonly struct over int, `IComparable`); `Atom { AtomTypeId Type; <fixed-point> Value }`.
- `AtomBag` — sorted `Atom[]` by `AtomTypeId`; binary-search `TryGet`; deterministic `Merge` (predicted
  ⊕ delta, delta wins) and `Diff` (atoms in the percept that diverge from a prediction).
- Fixed-point running-stats helper: `RunningStat { int Count; <8.8> Sum, SumSq }` → mean/spread, integer
  adds only (A4/A5 determinism — no float sums in Memory).
- **Tests:** sorted invariant, binary-search, Merge/Diff correctness, fixed-point arithmetic,
  key-order determinism. **p6: the determinism foundation.**

### A2 — MemoryRecord + four stores + bounded eviction
- `MemoryRecord { key, categoryRef(nullable), deltaBag: AtomBag, strength: byte, writtenAt, lastRefresh,
  flags: INNATE|SURPRISE }`.
- Four bounded, key-sorted stores (PLACES 128 · THINGS 64 · EVENTS 128 · MEANINGS 128) — flat slot
  arrays, SoA-friendly. `Encode/Refresh/Decay/StatFold` as plain methods (CQRS-eventized in Phase B).
- Eviction = lowest-strength-first, tie-break oldest `lastRefresh` then key order; a write into a full
  store must beat the weakest or it doesn't take; `INNATE` evict-immune (cap must exceed seed size).
- **Tests:** encode/refresh/decay/evict, caps, write-into-full, INNATE immunity, key-order determinism.
  **p6: eviction under pressure.**

### A3 — MEANINGS as variance-gated stats + recognition + StatFold + seed
- `CategoryNode { prototype: Signature, predicted: per-AtomType RunningStat, valence, confidence,
  flags }`. Recognition = nearest-prototype above a match threshold; below = novel (maximal surprise).
- `StatFold` = integer count/sum/sumsq adds per atom type; **only low-spread atom types enter
  `predicted`** (variance-gated intersection — the one genuinely new primitive).
- `MemorySeeds` static table: species → innate `CategoryNode`s (predator/food/water/… , `INNATE`).
- **Tests:** variance-gated stats converge to true contingencies under noise; recognition/novelty
  threshold; seed bootstrap; a contradicting stream weakens not corrupts. **p6: convergence.**

### A4 — encode path + the surprise operators
- Surprise = per-atom prediction error (percept atoms vs `node.predicted`). **Two operators:** MAX for
  the attention spike, SUM/count for the encode gate + write-strength.
- Encode gate: `surprise > θ_s OR arousal > θ_a` → `Encode(record)` with `deltaBag = Diff(percept,
  prediction)`, `strength = scale(max(surprise, arousal))`. Novelty (`categoryRef = null`) stores
  verbatim.
- **Tests:** surprise gating; the **notched-ear test** (a familiar entity + one trivial new atom spikes
  attention but does NOT flood the store); delta-only storage. **p6: the surprise aggregation split.**

### A5 — sleep consolidation + recall/reconsolidation
- Consolidation pass (the doc's steps 1–4): RE-DIFF (drop atoms the updated prediction now covers) ·
  MINT (cluster novel by signature proximity → mint node = variance-gated intersection, re-key+re-diff)
  · DECAY (`strength -= rate · (SURPRISE ? r_resist : 1)`, drop at 0, INNATE immune) · SETTLE
  (contradiction → confidence down → split past threshold; split mechanics under-pinned, p6 informs).
- Recall = reconstruct (`predicted ⊕ deltaBag`); reconsolidation (recall → `Refresh`).
- **Tests:** compression honesty (bounded stores + useful recall accuracy over a long run); false-memory
  rate (reconstruct-from-drifted-category → *tunable* confident-wrong); confirming-dissolves-fastest
  lifetime asymmetry; the Oak/Tuesday erosion (high-spread atoms never enter a fact). **p6: compression
  honesty, false memory, lifetime asymmetry.** (The shared-K recall *spiral* is a perception-pipeline
  dynamic — its harness test rides with Phase B wiring unless cheap to stub here.)

## Phase B (later — not this roadmap)

Wire the validated core into the live CQRS engine: `MemoryWrite` deltas through the gather-reduce
(rows 1b/2/5/7), one writer per agent state (row 5 awake, row 7 asleep), replace the proto-subset
`MeaningsRegistry`, and upgrade `SubjectiveSystem.Interpret`/`OddSystem` to read the new store. The
spec's three **sign-offs owed** (plan-surprise→Memory on row 3; attended memory-percepts→row 5 read
set; `MemorySeeds` static table) are Phase-B decisions.

## Sequencing notes

- A1→A2→A3 are a hard chain (each builds on the prior). A4 needs A3 (predictions to be surprised
  against). A5 needs A2+A3 (stores + stats). So the order is linear: A1, A2, A3, A4, A5.
- Each sub-plan is its own spec→plan→subagent-execution cycle, green on completion in `Sim.MemoryTests`.
- The "open knobs" (similarity metric/threshold, surprise aggregation, encode θ's, caps/rates, split
  mechanics) are built **configurable**; settling them is what the p6 tests are *for* — they are
  dynamics tuning, not architecture.
- Recommend a dedicated branch for the memory core (it's independent of the faction/identifier work on
  `design/faction-system`).
