# Cognitive Phase E / L1 — Social / Vicarious Learning ("monsters hurt my friend") design

**Status:** design, ready for implementation planning
**Date:** 2026-06-26
**Predecessors:** A (atoms) · B/L1 (affect read) · C/L1 (innate priors) · D/L1 (episodic "this kind hurt me") — all on branch `cognitive-phase1-subjective-interpret`
**Roadmap:** `docs/cognitive_framework_roadmap.md` — phase **E** ("social/vicarious — reputation/culture; transmit category beliefs between agents").

## Goal

A kind-belief **spreads** without first-hand experience. When a monster attacks, the witnesses already shout a danger alarm; Phase E makes that alarm also carry **the attacker's kind**, and makes every hearer in earshot reinforce their `{EnemyMonster}` category **second-hand, trust-scaled** by how much they trust the shouter. So fear of monster-kinds propagates through the town via talk — reputation/culture — reaching agents who were neither attacked (D) nor saw it. The vector is **gossip (transmission)**, riding the existing shout→hear→communicate pipeline.

## Governing principles (carried)

- **Beliefs are learned, never stamped.** The kind-belief lives in the agent's `MeaningsStore` category node (innate-seeded in C, reinforced first-hand in D, now reinforced second-hand here). No verdict atom.
- **Reuse the wired pipeline.** The shout (`PlaceDangerSystem`) → hear (`HearingSystem`) → relay (`CommunicationSystem`) path already carries place-danger second-hand, trust-scaled. E adds a parallel relay for the kind-belief — no new transport.
- **Trust scales the MOVE, not the target.** A low-trust report moves the belief *less* toward the aversive target; it never pulls an already-more-aversive belief back. (Scaling the outcome/target would do the latter — wrong.)
- **Frozen placeholders.** Constants are frozen starting values, tunable later. The soak is nondeterministic — validated by behaviour-sense (here: "does the belief spread"), never byte-diff.

## Substrate this builds on (verified)

- **The reinforce path** (D): `MemoryReinforceSystem.Emit(self, other, outcome)` publishes `MemoryReinforceIntent { EntityId Perceiver; AtomBag Signature; Fixed Outcome; }`; `AgentMemoryRegistry` applies it `if (!cat.IsNone) Reinforce(cat, sig, outcome)`. **First-hand only — no trust/scale.** No create-on-miss (`AgentMemoryRegistry.cs:145-146`).
- **The comm pipeline** (`Communication.cs`): `Utterance { EntityId Speaker, Audience; CommChannel Channel; SpeechAct Act; int SubjectBuilding; AtomBag Content; Fixed Confidence; }` → `HearingSystem` emits `HeardUtterance { Hearer, Said }` to all in the channel's radius → `CommunicationSystem.Update` (for `Inform` + `SubjectBuilding>=0` + `Shareable` atoms) emits second-hand `PlaceObserveIntent { …, SecondHand=true, TrustScale=BeliefScale }`.
- **Trust:** `CommunicationSystem.BeliefScale(hearer, speaker, confidence)` = `clamp(0.5 + 0.5·regard, 0.1, 1) × confidence` (`regard` = hearer→speaker from `RelationsRegistry`). Stranger half-heeds, friend fully, enemy barely.
- **The danger alarm:** `PlaceDangerSystem` computes witnesses (`SensedRegistry.Of(self)` includes victim or killer), stamps first-hand place-danger, and **shouts** (emits an `Utterance`, `CommChannel.Shout`).
- **`Reinforce`** (`MeaningsStore.cs`): nudges valence toward `outcome` by `step = gap >> LearnShift` (min ±1 raw on a non-zero gap); confirming outcomes raise confidence.
- **The monster category exists** on every civilian (D's `(Kind=EnemyMonster, −0.6, 0.3)` innate prior) — the prerequisite for any reinforce to land.

---

## Locked decisions

### E1 — The trust-scaled (second-hand) reinforce primitive

- `MemoryReinforceIntent` gains `public Fixed Scale;` — the trust weight on this reinforce (`Fixed.One` = full / first-hand).
- `MeaningsStore.Reinforce` gains a scale parameter that scales the **step**: `step = ((gap >> LearnShift) × scale)`, preserving the min ±1-raw guard on a non-zero gap (so even a low-trust report nudges, but a high-trust one moves far more). Valence still only moves toward `outcome`, never past — so a weak second-hand report can't pull an already-deep belief back.
- `AgentMemoryRegistry` passes `intent.Scale` into `Reinforce`.
- **`Fixed` structs zero-initialise**, so the existing first-hand `MemoryReinforceSystem.Emit` MUST set `Scale = Fixed.One` explicitly (else `Scale` defaults to 0 = no reinforce — a silent regression of D and the social loops). This is the one backward-compat trap.

### E2 — The alarm carries the attacker's kind

- `Utterance` gains `public AtomBag SubjectKind;` — the kind the utterance is ABOUT (`null`/empty = none; place facts leave it empty, unchanged).
- The danger alarm (`PlaceDangerSystem`'s shout) sets `SubjectKind = perceivable.Signature(killer)` — i.e. `{EnemyMonster}` (D's monster Kind atom). The killer/attacker entity is known from the `DeathEvent.Killer` / `DamageEvent.Source` the system already consumes. The alarm becomes "a *monster* attacked here," not just "danger here." (L1 simplification: the alarm carries the kind from the event, not gated on the individual witness having sensed the killer specifically.)

### E3 — Hearers reinforce the kind second-hand

`CommunicationSystem.Update` gains a parallel relay (independent of the place-fact branch): for a heard `Inform` utterance whose `SubjectKind` is non-empty, emit a second-hand reinforce for the hearer:
```
MemoryReinforceIntent {
    Perceiver = hearer,
    Signature = u.SubjectKind,        // {EnemyMonster}
    Outcome   = Hurt (−1.0),          // a danger alarm conveys "this kind is bad"
    Scale     = BeliefScale(hearer, speaker, u.Confidence),
}
```
So a monster attack → witnesses shout → everyone in earshot (witnesses *and* non-witnesses) deepens their `{EnemyMonster}` belief toward −1.0, by an amount scaled by their trust in the shouter. `BeliefScale` is reused as-is. The aversive `Outcome` is fixed at the alarm semantics for L1 (conveying the speaker's *actual* category valence is a later generalization).

### E4 — Prerequisite

The `{EnemyMonster}` category must already exist for the reinforce to land (no create-on-miss). D's innate monster prior provides it on every civilian. (A hearer with no monster category — e.g. a future un-seeded kind — no-ops, as in D.)

### E5 — Deferred (explicit)

- **Witnessing as a direct source** (a bystander who *sees* the attack reinforcing without the alarm) — deferred; L1's vector is the gossip alarm.
- **Idle gossip** (`GossipSpeakSystem` periodically sharing kind-beliefs, untied to events) — deferred (avoids the innate −0.6 drifting to −1.0 from chatter alone).
- **Conveying the speaker's actual belief valence** (positive or graded, not a fixed alarm-aversive) — deferred.
- **The threat reputation cue** (learned/transmitted danger amplifying `ThreatRead`) — still deferred (D-deferred); it's the consumer that would make all this visible.

---

## Honest payoff note

As in D, the spread `{EnemyMonster}` belief feeds `Interpret`'s social valence, which the **prey-veto masks for monsters** — so no flee-behaviour change. The L1 value is the **spread itself**: where D drifted only the few direct victims (who often die), E propagates the deepened belief to the **living, in-earshot population** — so a much larger share of the town carries a strong, confident monster-belief, sourced from others' experience. Validated by the category metrics (more agents at deep `maxAbs`, faster `conf` rise), not gross behaviour. The visible bite still awaits the threat reputation cue.

## Validation

- **Unit tests.** The trust-scaled reinforce: a `Scale<1` `MemoryReinforceIntent` moves a seeded `{EnemyMonster}` category *less* than `Scale=1` for the same outcome (and `Scale=1` matches D's first-hand drift). `BeliefScale` rises with regard (friend > stranger > enemy). `CommunicationSystem`: a heard `Inform` utterance with a non-empty `SubjectKind` emits a second-hand `MemoryReinforceIntent` (Perceiver = hearer, the kind signature, aversive outcome, `Scale = BeliefScale`); a place-only utterance still emits only the `PlaceObserveIntent` (no regression). The existing first-hand `Emit` sets `Scale = Fixed.One` (D's drift unchanged).
- **Behaviour-sense soak** (Gothway + Gallotale, NOT byte-diff): the monster category deepens across **more** of the population than in D (the alarm reaches non-victims) — more agents toward `maxAbs≈1.0` / high `conf` — while population/rhythm stay in-regime and no new artifact appears (no spurious reinforcement, social mix unchanged).

## Frozen constants (all tunable)

| Constant | Value | Where |
|----------|-------|-------|
| Gossip alarm `Outcome` (aversive) | −1.0 | `CommunicationSystem` (the kind relay) |
| `BeliefScale` shape | `clamp(0.5+0.5·regard, 0.1, 1)×conf` | `CommunicationSystem` (existing, reused) |
| First-hand `Scale` | `Fixed.One` | `MemoryReinforceSystem.Emit` |

## Out of scope / future

- **E/L2:** witnessing as a direct source; idle-gossip kind-sharing; speaker-actual-valence transmission (positive reputations too).
- **The threat reputation cue** (the consumer that makes learned/transmitted danger visibly amplify fear) + the EVENTS episodic store.
- More kinds / sources where transmitted danger isn't redundant with the form read.
