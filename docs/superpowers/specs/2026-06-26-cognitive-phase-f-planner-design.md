# Cognitive Phase F / L1 — The Planner Consumes the Arc ("learning to fear what doesn't look dangerous") design

**Status:** design, ready for implementation planning
**Date:** 2026-06-26
**Predecessors:** A (atoms) · B/L1 (affect read) · C/L1 (innate priors) · D/L1 (episodic "this kind hurt me") · E/L1 (social gossip) — **all merged to master**
**Roadmap:** `docs/cognitive_framework_roadmap.md` — phase **F** ("planner consumes it; cull + goal-beacons + recall door"). F/L1 is the **threat-reputation-cue capstone** that makes the B–E arc behaviourally visible.

## Goal

Make the cognitive arc visibly change behaviour: **a town learns to fear a kind that doesn't look dangerous.** F/L1 introduces a harmless-*looking* attacker (a person-shaped, unarmed creature that still damages), wires the perceiver's *learned* kind-belief into the threat read (the long-deferred "reputation cue"), and — the spine — makes perception report a **neutral appearance, never a hostility verdict.** Agents form the *concept* of the new kind from seeing it (sleep-consolidation), learn it's dangerous from being hurt (D) and being told (E), and the reputation cue turns that learned aversion into *fear* — so they flee and avoid a kind whose menace they discovered entirely through experience.

## Governing principles (carried + sharpened)

- **No verdict atoms — including in IDENTITY.** "Enemy" is a judgment, formed in the perceiver, never perceived. Perception reports what a thing *looks like* (a neutral appearance), not the game's hostility role. F/L1 retires the verdict-named perceivable identity (`Kind(EnemyMonster)`) that D introduced.
- **Beliefs form in the perceiver, from experience.** The new kind's category is *minted* from perception (sleep-consolidation, neutral valence), drifted by damage (D) and gossip (E), and consumed as threat (F). Nothing about "danger" is stamped or seeded for it.
- **Role ≠ appearance.** A thing's internal game role (`EntityKind`, drives AI/hostility/spawning) is never perceived; only its appearance is. The Drifter *is* a hostile creature mechanically (`EntityKind.EnemyMonster`) but *looks like* an ordinary person.
- **Reuse the wired machinery.** The reputation cue is a few lines in the existing `Interpret`; the alarm generalization extends E's existing shout; the creature reuses `CreatureSystem`/`HealthSystem`.
- **Frozen placeholders; behaviour-sense soak.** Constants are frozen starting values, tunable later. The soak is nondeterministic — validated by behaviour-sense ("does the town learn to fear the drifter"), never byte-diff.

## Substrate this builds on (verified, this grounding)

- **Damage is form-independent.** `HealthSystem` applies `DamageEvent` regardless of the attacker's form atoms (`HealthSystem.cs:41-66`) — a weapon-less creature deals damage. ⇒ the Drifter can be mechanically a monster while looking unarmed.
- **Categories MINT from perception at sleep.** `MemoryEncoder.Perceive` (`MemoryEncoder.cs:36-56`) **recognizes-only (no create)**; a novel signature writes a novel THINGS record (`Surprise.Maximal`). Sleep-consolidation `Consolidation.Mint` (`Consolidation.cs:42-81`) clusters novel records (≥ `MinClusterSupport`) into a **new `MeaningsStore` category at NEUTRAL valence (`Fixed.Zero`)**. So a kind's concept forms organically from repeated perception — no seed.
- **`Interpret` reads the learned valence.** `SubjectiveSystem.Interpret` stranger branch (`SubjectiveSystem.cs:134-141`): `mem.Meanings.RecognizedValence(perceivable.Signature(other), out lv, out _)` → `baseValence`. Threat is read ONLY from form atoms (`ThreatRead`, `:122-123`); the prey-veto masks social valence when threatened (`:151`). **No learned-category threat cue yet** — the F2 insertion point is right after `:141`.
- **The perceivable Kind atom is keyed on the verdict enum.** `PerceivableAtoms.Kind(EntityKind)`; `EntityKind = { Unknown, Player, EnemyClass, EnemyMonster, CivilianNPC, StaticNPC }` (`IdentityRegistry.cs:7-15`). The monster stamps `Kind(EnemyMonster)` (`CreatureSystem.cs:236`) — the wart F0 removes.
- **`CreatureSystem.TrySpawn` is hardcoded** to Beast form (`CreatureForms.Beast = Fanged/Fast/Size`, `CreatureForms.cs:14-19`) + `Kind=EnemyMonster` (`CreatureSystem.cs:223,236`). `HumanForms.Civilian = {Size 0.4}` (`HumanForms.cs:15-18`) is a weapon-less reference form.
- **The danger alarm hardcodes the kind.** `PlaceDangerSystem` shout: `SubjectKind = {Kind(EnemyMonster)}` (`PlaceDangerSystem.cs:60`); its ctor has **NO `PerceivableRegistry`** (`PlaceDangerSystem.cs:26-30`). E's gossip relay reinforces the hearer's category for `SubjectKind`.
- **The innate prior + reinforce path** (C/D/E): civilians seed `(Kind=EnemyMonster, −0.6, 0.3)` + `(Self, +0.2)`; `DamageEvent` (D) and the gossip alarm (E) reinforce the kind category trust-scaled; no create-on-miss.

---

## Locked decisions

### F0 — Perception reports a neutral appearance (verdict-free identity)

The perceivable Kind atom is re-keyed off the `EntityKind` hostility enum onto a neutral **appearance** identity (what a thing looks like). `EntityKind` stays the internal AI/spawn/hostility role — never perceived.
- A neutral appearance domain (e.g. `enum Appearance { Person, Beast, Drifter }` — names are flavour) parameterizes `PerceivableAtoms.Kind`.
- The monster's perceived identity becomes `Kind(Beast)` — a fanged quadruped; its menace lives in its FORM atoms (Fanged/Fast) + the innate prior, **not** its label.
- **Every** current Kind-atom site maps to a neutral appearance: the monster → `Beast`; civilians → `Person` **if** they stamp a Kind atom (verified at plan-grounding — their identity may already be neutral Role/Race, in which case unchanged); the new kind → `Drifter`.
- The innate prior D keyed on `Kind(EnemyMonster)` re-keys to `Kind(Beast)` ("born wary of beast-looking predators") — **unchanged in value** (−0.6, 0.3), only its neutral prototype.
- **Nothing keys threat/fear/belief on the raw `EntityKind`** — all of it flows through perceived atoms (`ThreatRead` on the bag, the learned category on the Signature). Verify at plan-grounding; if any consumer reads `EntityKind` for a *judgment*, that's a verdict-leak to fix.

### F1 — The Drifter: a harmless-looking attacker

`CreatureSystem.TrySpawn` is parameterized to spawn, for a fraction of spawns, a second variant — the **Drifter**:
- Internal `EntityKind.EnemyMonster` — **the same creature role/AI/attack as the Beast** (so all hostility, guard-targeting, curfew, and attack logic work unchanged; damage is form-independent).
- Form atoms: a weapon-less humanoid form (Size only, like `HumanForms.Civilian`) — **NO Fanged/Fast**, so `ThreatRead` reads ≈ 0 form-threat: it *looks harmless*.
- Perceivable identity: `Kind(Appearance.Drifter)` — a neutral "person-shaped wanderer," distinct from civilians (`Person`) and from the Beast (`Beast`).
- It attacks like any creature → publishes `DamageEvent`.
- **NO innate prior** — civilians start NEUTRAL about Drifters (the innate prior matches `Kind(Beast)` only); they must *learn*.

A frozen spawn split (e.g. half Beast / half Drifter) governs the mix; tunable. The Beast and Drifter are *mechanically identical* (same role, same form-independent damage) and differ only in **appearance** (perceived threat) — that contrast is the demonstrator.

### F2 — The reputation cue: learned aversion becomes threat

In `SubjectiveSystem.Interpret`, after the learned category valence is read (`:139-141`), a **reputation threat cue** folds the perceiver's learned belief into the threat read:
- Read the learned valence AND its confidence: `RecognizedValence(Signature(other), out lv, out lconf)` (today the confidence is discarded with `_`).
- If the belief is aversive (`lv < 0`): `repThreat = lconf × (−lv) × RepWeight` — a confident, deep aversion reads as a strong threat; a tentative one barely.
- `threat = max(formThreat, repThreat)`; `threatValence = min(threatValence, lv)`.
- The existing prey-veto (`:151`) then masks the social valence as before.

So a kind you've *learned* (D) or been *told* (E) is dangerous reads as a threat — even with no weapon form atoms. For the Drifter (form-threat ≈ 0) the cue is the **only** threat signal, so fear of it scales entirely with the learned belief. For the Beast (form-threat already high) the cue is redundant (`max` with the higher form value) — **unchanged in practice**, as expected and intended.

### F3 — The alarm carries the killer's actual (neutral) kind

`PlaceDangerSystem` gains a `PerceivableRegistry` (ctor + `SimWorld` wiring). The danger shout sets `SubjectKind = _perceivable.Signature(d.Killer)` — the killer's **actual neutral kind** (`Beast` or `Drifter`), replacing E's hardcoded `{EnemyMonster}`. So a witness of a Drifter kill shouts about *Drifters*, and E's gossip relay spreads Drifter-fear second-hand, trust-scaled. The aversive outcome stays the alarm semantics (per E).

### F4 — Deferred (explicit)

- **Idle belief-gossip** (`GossipSpeakSystem` sharing the speaker's own kind-beliefs, any valence — "gossip about anything") + **conveying the speaker's actual graded valence** — **F/L2** (the channel is general; only the producer is deferred). *[User-confirmed deferral.]*
- **Place-danger avoidance** already exists (`PlaceMemoryFactor`) — unchanged.
- **Goal-beacons** and the **EVENTS recall door** — later F slices.
- **Cull-by-axis** planner feature — later.

---

## Honest payoff note

This is the slice where the arc becomes **visible**. Unlike B–E (structural, veto-masked because the only dangerous kind was form-scary), the Drifter has ≈ 0 form-threat, so the reputation cue is its *only* threat signal — fear of it is **100% learned**. The contrast is the demonstrator: the Beast is feared from spawn (innate prior + form), the Drifter only after the town has **seen** them (concept minted at sleep), been **hurt** by them (D), and **talked** about them (E). The payoff is behavioural and measurable (Drifter-directed flee/avoidance rising over days), and it validates the whole A–E investment.

## Validation

- **Unit tests.**
  - **F0:** the monster's Signature is `{Kind(Beast)}` (neutral); the innate prior recognizes it at −0.6; no Kind atom encodes an `EntityKind` verdict.
  - **F1:** a Drifter spawns with `Kind(Drifter)` + a weapon-less form; `ThreatRead(Drifter bag)` ≈ 0; a Drifter `DamageEvent` fires; the Drifter signature ≠ the Beast signature ≠ a civilian's.
  - **F2:** an `Interpret` over an entity whose learned category valence is aversive returns `Threat > 0` **even with no form atoms**, and `Threat` scales with the belief's depth × confidence; a neutral/unknown kind returns the form-only threat (no regression); a civilian (neutral belief) returns no reputation threat.
  - **F3:** the danger shout's `SubjectKind = Signature(killer)` (Drifter when a Drifter kills; Beast when a Beast kills).
- **Behaviour-sense soak (multi-day, Gothway + Gallotale, NOT byte-diff).** Across ~3 days: a `{Drifter}` category **mints** on civilians after the first sleep (`cat/agent` rises), starts neutral, and drifts aversive as Drifters attack + the alarm spreads; **Drifter-directed Flee/avoidance rises over days** (learned fear), while **Beast-fear is high from day 1** (innate+form); population stable/in-regime; **no civilian-fears-civilian** (civilians' neutral `Person` appearance never accrues aversion absent harm); no new artifact.

## Frozen constants (all tunable)

| Constant | Value | Where |
|---|---|---|
| `RepWeight` (learned-aversion → threat) | 1.0 | `SubjectiveSystem` (the cue) |
| Drifter spawn fraction | 0.5 | `CreatureSystem.TrySpawn` |
| Drifter form | `{Size 0.4}` (weapon-less) | `CreatureForms` (new) |
| (carried) monster innate prior | `(Kind=Beast, −0.6, 0.3)` | `InnatePriors` |

## Out of scope / future

- **F/L2:** idle belief-gossip + speaker-graded-valence transmission; goal-beacons; the EVENTS recall door; cull-by-axis.
- A richer appearance taxonomy; the viewer rendering the new kind (the soak is headless).
- Tuning the spawn mix / `RepWeight` / the multi-day learning curve.
- More damage sources (guard-on-civilian, agent-on-agent) where learned danger stops being redundant with the form read.
