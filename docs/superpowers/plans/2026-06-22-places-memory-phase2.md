# PLACES Memory — Phase 2 (ODD scores ads on place atoms) Implementation Plan

> REQUIRED SUB-SKILL: superpowers:subagent-driven-development / executing-plans. Steps use `- [ ]`.

**Goal:** Close the dense coupling — `OddSystem` reads each agent's rich PLACES memory on **every
decision** to *score* candidate building-ads: avoid places remembered as dangerous, prefer places
remembered as provisioned. This is where memory **visibly moves behaviour** (danger-avoidance), the
payoff the whole PLACES + per-atom-strength groundwork was for.

**Spec:** `docs/superpowers/specs/2026-06-22-places-memory-design.md` (the "Read path" section).
Builds on Phase 1 (write/seed) + per-atom strength (kind persists, danger fades when a place quiets).
Branch `perceivable-atoms`.

## Global Constraints
- **Additive to ad GENERATION:** ad *generation* stays on `_placeMemory.Known` → `Discover` (proven,
  untouched → no navigation regression). Only ad *SCORING* (`V`) reads the rich PLACES store.
- **Modulate, never zero:** the place factor is a *clamped multiplicative gate factor* (like
  `RelationFactor`/`ConscienceFactor`), floored well above 0 — a desperate need still overrides
  avoidance, so the sim can't deadlock (a starving agent still enters a remembered-dangerous tavern).
- Determinism: integer/`Fixed` reads, no new RNG. C# 7.3. `--engineworld` parallel==serial must hold.
- Test: `dotnet test Headless/Sim.MemoryTests` + soak via `Sim.Host --soak`.

## Design decisions (locked)
1. **One new gate factor** `PlaceMemoryFactor(self, ad)` multiplied into `V`'s `gate` product
   (OddSystem.cs:562–573), reading `_agentMemory.TryGet(self).Stores.Places.TryGet(MemoryKey(ad.Building))`.
   Building-less ads (`ad.Building < 0`) and unremembered buildings → factor 1.0 (no effect).
2. **Danger → aversion (the headline):** `DangerHere` value `d∈[0,1]` →
   `PlaceAversion(d) = clamp(1 − DangerAversion·d, DangerFloor, 1)`. Applies to **all verbs** at that
   building (a dangerous place is avoided whatever the errand). Pure `public static` fn (unit-testable,
   mirrors `RelationFactor`).
3. **Provisions → preference (secondary):** for provisioning specs only, a remembered-empty place is
   mildly deprioritised — `ProvisionPreference(p)` a gentle factor (soft prior; the hard `LarderGated`
   ground-truth gate at OddSystem.cs:210 stays). Small effect; danger is the measurable payoff.
4. **Constants are config, tuned from the soak, not up front** (no_premature_tuning): start
   `DangerAversion≈0.9, DangerFloor≈0.1`; the soak says whether avoidance is visible without starving.

## Files
- `Assets/Sim/Engine/Units/OddSystem.cs` — `_agentMemory` dep; `PlaceMemoryFactor` + `PlaceAversion`/`ProvisionPreference` statics; one `* PlaceMemoryFactor(c.Self, ad)` in `V`'s gate (Task 1)
- `Assets/Sim/Engine/SimWorld.cs` — pass `AgentMemory` to the `OddSystem` ctor (Task 1)
- `Assets/Sim/Engine/EngineSoak.cs` — danger-avoidance metric (Task 2)
- Tests: `Headless/Sim.MemoryTests/PlaceScoringTests.cs` (Task 1)

---

### Task 1: `OddSystem` reads PLACES to score ads

**Interfaces:** `OddSystem` ctor gains `AgentMemoryRegistry agentMemory` (beside `placeMemory`).
`public static double PlaceAversion(double danger)` and `public static double ProvisionPreference(double prov)`
(pure, clamped). Private `double PlaceMemoryFactor(EntityId self, Ad ad)` reads the PLACES record and
composes the two; returns 1.0 for building-less / unremembered ads. `V` multiplies it into `gate`.

- [ ] **Step 1: Write the failing tests** — `Headless/Sim.MemoryTests/PlaceScoringTests.cs`: pure-fn
  tests — `PlaceAversion(0)==1`, `PlaceAversion(1)==DangerFloor`, monotone decreasing, clamped;
  `ProvisionPreference` neutral at full, gentle penalty at 0. (Mirror how `RelationFactor` is tested.)
- [ ] **Step 2: Run, verify FAIL.**
- [ ] **Step 3: Implement** the statics + `PlaceMemoryFactor` (read `mem.Stores.Places.TryGet`,
  `DeltaBag.TryGet(PlaceAtoms.Danger/Provisions)`), wire `_agentMemory`, add the `* PlaceMemoryFactor`
  to `V`'s gate, pass `AgentMemory` in `SimWorld`.
- [ ] **Step 4: Run, verify PASS;** `--engineworld` parallel==serial still holds.
- [ ] **Step 5: Commit** `feat(memory): ODD scores ads on PLACES atoms (danger-avoidance + provisions-pref)`.

---

### Task 2 — OUTCOME (validated; no further work)

3-day soak: with 61 kills, danger memory reached only **6 records / 340 agents** — danger is **sparse
by design**. A witness is an agent that *personally perceived* the kill (sensed the victim/killer);
most kills have no memory-agent in range, so few learn. **This is correct, not a limitation**: an
agent cannot know danger it never witnessed. Population-scale danger-avoidance is therefore not a soak
signal today, and that is the intended semantics. The coupling itself is validated by the pure-fn
tests + its live presence in `V`'s gate + the determinism gate; economy/provisions stay healthy
(pop 340, provisions memory persists ~200, kind atoms permanent). No densifying of witnessing, no
dedicated avoidance metric (6/340 would be noise).

**Deferred (the natural next sim feature):** *social information-relaying* — talking / shouting /
rumor as communication channels that **propagate** place facts (danger especially) between agents, so
one witness's knowledge spreads through the population. That is what makes danger-avoidance visible at
scale, by the principled route (heard-from-others), not by widening first-hand perception.

### (original) Task 2: soak validation — is danger-avoidance visible?

**Interfaces:** soak metric counting decisions that landed on a danger-remembered building vs total
building-decisions (an "avoidance rate"); and the existing kills trend over a multi-day run.

- [ ] **Step 1: Add the metric** to `EngineSoak` (e.g. per checkpoint: `dangerAvoid[ads-at-danger-bldg
  chosen=X/Y]`, and keep `kills[...]`).
- [ ] **Step 2: Multi-day soak A/B** — run 3–5 days with the factor ON vs a temporary OFF toggle.
  Expectation: with avoidance ON, agents who witnessed kills stop re-choosing those kill-zone buildings
  → the avoidance rate falls over days and/or kills/day trends below the OFF run. Behaviour drifts from
  the Phase-1/baseline shape in the danger-avoidance direction (spec's Phase-2 validation).
- [ ] **Step 3: Tune** `DangerAversion`/`DangerFloor` from what the soak shows — avoidance visible,
  population/economy still stable (no starvation spike). Record the numbers chosen + why.
- [ ] **Step 4: Commit** `feat(soak): danger-avoidance metric + Phase-2 tuning`.

---

## Self-Review
Coverage: read-path scoring + pure-fn tests (T1), behavioural validation + tuning (T2). Additive —
generation untouched, factor clamped so the sim can't deadlock; determinism preserved (no RNG, integer
reads). Danger is the measurable payoff; provisions a soft prior beside the existing larder gate.
Deferred: place *categories* ("the dangerous quarter" generalised across places); danger keyed to
position/region rather than nearest-building; richer facts (crowdedness, safety).
