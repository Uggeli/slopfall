# Per-Atom Strength — Implementation Plan

> REQUIRED SUB-SKILL: superpowers:subagent-driven-development / executing-plans. Steps use `- [ ]`.

**Goal:** Move record `strength` + INNATE/SURPRISE flags from per-record to **per-atom** across
PLACES/THINGS/EVENTS, with a strength-scaled decay operator (important ≈ immune, trivial ≈ fast).
Importance = strength (no new concept). PLACES seeds per-atom salience (Kind=INNATE, Danger=high,
Provisions=medium) so a building's knowledge stops collapsing overnight.

**Spec:** `docs/superpowers/specs/2026-06-22-per-atom-strength-design.md`. Branch `perceivable-atoms`.

## Global Constraints
- Determinism: integer/`Fixed` math only; key-order iteration; no floats (the doc's Determinism §).
- C# 7.3. CQRS unchanged (`AgentMemoryRegistry` sole writer). Per-atom arrays stay index-aligned to
  `DeltaBag.Atoms` at all times — every bag transform rebuilds atoms+meta together.
- **Stay green at every task** via a temporary back-compat `MemoryRecord` constructor that broadcasts
  a record-level `(strength, flags)` into per-atom meta; call sites migrate task-by-task; the shim is
  deleted in the final task. `record.Strength`/`IsInnate`/`IsSurprise` stay as **derived** getters
  (max / any) throughout, so unmigrated record-level *reads* keep working.
- Test: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj` (ARENA2 set for integration).

## Design primitives (used across tasks)
- `struct AtomMeta { byte Strength; MemoryFlags Flags; }` — one per atom, index-aligned to
  `DeltaBag.Atoms`. (Single struct array beats two parallel arrays — can't drift.)
- `MemoryRecord` carries `AtomMeta[] Meta`. Derived: `EvictionStrength => max_i(IsInnate(i)?255:Meta[i].Strength)`;
  `Strength => EvictionStrength` (compat); `AnyInnate`; `IsInnate(i)`/`IsSurprise(i)`.
- Salience table `MemorySalience` (static): `AtomTypeId → (byte seedStrength, MemoryFlags)`; default
  medium/None; PLACES: `Kind(*)→(255, Innate)`, `Danger→(255, Surprise)`, `Provisions→(160, None)`.
- Decay operator (integer): `dec(S, base) = Innate ? 0 : max(1, base*(255-S)/255)`.

## Files
- `Assets/Sim/Memory/AtomMeta.cs` (new, Task 1)
- `Assets/Sim/Memory/MemoryRecord.cs` — per-atom meta + derived getters + back-compat ctor (Task 1)
- `Assets/Sim/Memory/MemoryStore.cs` — per-atom Decay/Refresh, derived-strength eviction (Task 1)
- `Assets/Sim/Memory/Surprise.cs` — emit per-(delta-)atom error (Task 2)
- `Assets/Sim/Memory/MemoryEncoder.cs` — per-atom strength/flags at encode (Task 2)
- `Assets/Sim/Memory/Consolidation.cs` — re-diff/mint carry per-atom meta (Task 3)
- `Assets/Sim/Memory/MemoryRecall.cs` + `MemoryStore.Refresh` — per-atom refresh (Task 3)
- `Assets/Sim/Memory/MemorySalience.cs` (new) + `AgentMemoryRegistry.cs` PLACES seed (Task 4)
- Tests across `Headless/Sim.MemoryTests/` (each task migrates its own; see spec's list)

---

### Task 1: `MemoryRecord` per-atom meta + `MemoryStore` per-atom decay/refresh/eviction

**Interfaces:** `AtomMeta{Strength,Flags}`; `MemoryRecord.Meta[]`; derived `EvictionStrength`,
`Strength`, `AnyInnate`, `IsInnate(i)`, `IsSurprise(i)`; `WithAtomMeta(AtomMeta[], long)`;
back-compat ctor `(key,cat,bag,byte strength,written,refresh,flags)` broadcasts to `Meta[]`.
`MemoryStore.Decay` per-atom (drop atom at 0, drop record when bag empty); `Refresh` bumps all
non-INNATE atoms; eviction `LessKeepable` uses `EvictionStrength`.

- [ ] **Step 1: Migrate + add tests** — rewrite `MemoryStoreRefreshDecayTests` for strength-scaled
  per-atom decay (two-strength curve; INNATE atom immune while a peer decays; record dies only when
  bag empties; Refresh bumps per-atom). Update `MemoryRecordTests` (Meta, derived getters,
  `WithAtomMeta`). Add `Eviction_UsesMaxAtomStrength`. Keep `MemoryStoreEvictionTests` INNATE-immunity.
- [ ] **Step 2: Run, verify FAIL.**
- [ ] **Step 3: Implement** `AtomMeta`, `MemoryRecord` (meta + derived + back-compat ctor),
  `MemoryStore` (per-atom Decay/Refresh, derived eviction). All other call sites still use the
  back-compat ctor → compile green.
- [ ] **Step 4: Run, verify PASS** (whole suite — record-level readers still work via derived getters;
  only the decay-number + record-meta tests changed).
- [ ] **Step 5: Commit** `feat(memory): per-atom strength on MemoryRecord + strength-scaled decay`.

---

### Task 2: per-atom strength at encode (`Surprise` + `MemoryEncoder`)

**Interfaces:** `Surprise.Against` also emits per-delta-atom error (aligned to `Diff(percept,pred)`).
`MemoryEncoder.Perceive` builds `Meta[i] = (Scale(max(error_i, arousal)), error_i>θ_s?Surprise:None)`;
novel ⇒ every atom `(255, Surprise)`. **Write-gate unchanged** (`Encode>θ_s OR arousal>θ_a`).

- [ ] **Step 1: Migrate + add tests** — `MemoryEncoderTests`: the `Strength<64`/`>200`/`==255` etch
  tests assert the (single) atom's `Meta.Strength`; add "the notched-ear atom is etched strong while a
  trivial co-atom rides weak in the SAME record" (the per-atom payoff).
- [ ] **Step 2: Run, verify FAIL.**
- [ ] **Step 3: Implement** Surprise per-atom out + encoder per-atom meta (drop the back-compat ctor
  here — encoder now builds `Meta[]` directly).
- [ ] **Step 4: Run, verify PASS.**
- [ ] **Step 5: Commit** `feat(memory): encode etches per-atom strength from per-atom surprise`.

---

### Task 3: consolidation + recall carry per-atom meta

**Interfaces:** index-aligned `AtomBag.Diff` variant returning surviving indices (so RE-DIFF/MINT drop
`Meta[]` slots with their atoms); survivors keep meta. `MemoryRecall`/`MemoryStore.Refresh` refresh
all non-INNATE atoms.

- [ ] **Step 1: Migrate + add tests** — `ConsolidationPassTests` (keep ordering asserts, update exact
  strengths; add "an INNATE atom rides through re-diff while its decayed peer drops"); `MemoryRecallTests`
  (per-atom refresh). `ConsolidationReDiff/Mint` tests adjusted only where they touch strength.
- [ ] **Step 2: Run, verify FAIL.**
- [ ] **Step 3: Implement** index-aligned diff helper; re-diff/mint preserve per-atom meta; recall
  refresh per-atom. Migrate `Consolidation.cs`/`MemoryRecall.cs` off the back-compat ctor.
- [ ] **Step 4: Run, verify PASS.**
- [ ] **Step 5: Commit** `feat(memory): consolidation + recall carry per-atom strength`.

---

### Task 4: PLACES salience seeding + remove the shim

**Interfaces:** `MemorySalience` static table; `AgentMemoryRegistry.MergePlaceAtom` stamps each atom at
its salience strength/flags (Kind=INNATE, Danger=255/Surprise, Provisions=160) instead of flat
`PlaceStrength=200`. Delete the back-compat `MemoryRecord` ctor (all call sites migrated).

- [ ] **Step 1: Migrate + add tests** — extend `PlaceMemoryWriteTests`/`PlaceSeedingTests`: a seeded
  `Kind` atom `IsInnate`; `Danger` is Surprise-flagged; `Provisions` ordinary. Confirm
  `PlaceDangerTests` still green.
- [ ] **Step 2: Run, verify FAIL.**
- [ ] **Step 3: Implement** `MemorySalience` + registry seed/merge; delete the shim ctor; fix any last
  call site.
- [ ] **Step 4: Run, verify PASS;** **soak (1 day):** `rec/agent` no longer collapses overnight (Kind
  persists); `meanDanger` rises on kills and fades when quiet; aggregate behaviour == baseline.
- [ ] **Step 5: Commit** `feat(memory): per-atom salience seeding for PLACES + drop record-strength shim`.

---

## Self-Review
Coverage: record/store per-atom + decay (T1), encode (T2), consolidation+recall (T3), salience seed +
shim removal (T4). Always-green via the temporary broadcast ctor + derived record-level getters.
Ordering invariant "confirming dissolves faster than surprising" preserved (asserted in T3). Scope =
all stores (locked). Determinism: integer per-atom decay, key-order iteration; the serial==parallel
fingerprint must hold — re-check in T4 soak. Deferred: ODD reads PLACES (Phase 2); salience tuning.
