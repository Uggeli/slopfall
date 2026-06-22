# The Threshold

Agents respect contended shared affordances: they queue at a shopkeeper, take turns, and balk when
something more important comes up. Built on a generic **shared-activity instance** model with one
protocol (`ServiceQueue`) plus a **percept-driven preemption** loop.

- Spec: `docs/superpowers/specs/2026-06-22-the-threshold-shared-activities-design.md`
- Plan: `docs/superpowers/plans/2026-06-22-the-threshold-shared-activities.md`
- Branch: `the-threshold` (off `master`)

## What shipped (Phases 0–2)

**Pillar A — shared activities / queues**
- `SharedActivityInstance` / `SharedActivityRegistry` / `SharedActivitySystem` — a generic ServiceQueue:
  ordered waiters, capacity-limited served slots. Mutated via `QueueJoinIntent`/`QueueLeaveIntent` (CQRS).
- New `ActivityPhase.Queued` (wire-stable append). Flow: `Moving → Queued → Doing`.
- Shop `Buy` is the first serviced affordance: arrival joins the queue (`ExecutionSystem`); a waiter walks
  to its slot (`MovementSystem` moves `Queued` agents; arrival fires only for `Moving`); the head is
  promoted to `Doing` when the counter frees. `EconomySystem.PaySale` is unchanged — the queue gates
  *who* is in `Doing`, so service serializes for free. Capacity = 1 (one counter, tunable).

**Pillar B — percept-driven preemption**
- Somatic percepts: each agent stamps its own hunger/energy/fear into its own `AtomBag` (`SomaticPerceptSystem`).
  A seam — ODD still scores `NeedsData` directly (no double-count).
- Balking: a queued agent re-evaluates (~every 5 ticks + at the cap) and leaves the line when a better
  action beats waiting by a hysteresis margin (`ShouldSwitchCommitment`). Leaving publishes `QueueLeaveIntent`;
  staying re-commits with the decision clock reset (avoids per-tick re-decide).
- Sleep gating: a sleeper suppresses percept-driven re-evaluation and wakes only on a hit (`DamageEvent`),
  dawn/dusk, sleep expiry, or the cap (`SleepShouldWake`).

**Viewer**: `Queued` phase already rides the wire; the client tints queued agents (muted amber) and the
inspector shows "in line (#N)" / "at counter".

## Tuning constants
`ShopCapacity=1`, `QueueSpacing=1.5`, `PreemptEveryTicks=5`, `Hysteresis=0.15`.

## Verification
- **Determinism**: `--engineworld` parallel==serial after 20k ticks with all new systems. ✅
- **Unit tests**: 214 green (registry invariants, capacity serialization, pure decision helpers
  `ShouldStayInQueue`/`ShouldSwitchCommitment`/`SleepShouldWake`, movement predicates, somatic stamp).
- **Soak** (Daggerfall/Gothway Garden, 2 days / 1.728M ticks): lines form (258 concurrent `Buy` vs
  capacity-1 shops), agents balk to sleep at night, no stuck-`Queued`, pop 337→340 (no deaths).
- **Performance**: ~0.16 ms/tick at town scale — far under the 100 ms/tick budget.

## Watch-items / follow-ups
- `meanHunger` rises 0.35→0.83 over 2 days under capacity-1 back-pressure (the intended emergent signal).
  Not lethal (hunger has no death consequence yet). Lever: `ShopCapacity`. Compare against a pre-Threshold
  baseline soak before tuning — it may be partly the pre-existing retail-food-market gap, not the queue.
- **Doing-agent mid-activity preemption deferred**: `OddSystem` direct `BehaviorSetIntent` writes would race
  `ExecutionSystem`'s `RemainingGameMinutes` countdown. Needs the `IntentSetIntent` commit path. Balking from
  a queue (the headline interrupt) is delivered; mid-activity flee still works on the existing cadence.
- **Manual, not yet done**: browser visual check of the queue + parity baseline re-capture
  (`parity/baseline/local-r2-base.png` shifts because queues change agent placement).
- Loud-noise sleep wake deferred (no salient-noise event exists yet).
- Pre-existing, unrelated: headless `UtteranceLog` buffer overflow (comms substrate; nothing drains it
  outside the web client).

## Future (designed-for, not built)
- **Doors (Phase 3)** — wall gates + building-entry doors reuse `ServiceQueue` (capacity 1, instant) at
  movement-time; `Open`/`Close` executors; curfew. Its own plan.
- **Barter** replaces the single `PaySale` call-site (skill rolls + comms dialog). **Combat** = a Conflict
  protocol. The `SharedActivityInstance` participant/role model accommodates both.
