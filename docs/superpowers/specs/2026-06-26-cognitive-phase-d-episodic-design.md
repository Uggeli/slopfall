# Cognitive Phase D / L1 — Episodic Reinforcement ("this kind hurt me") design

**Status:** design, ready for implementation planning
**Date:** 2026-06-26
**Predecessors:** Phase A (atoms) · B/L1 (the affect read) · C/L1 (innate priors) — all on branch `cognitive-phase1-subjective-interpret`
**Roadmap:** `docs/cognitive_framework_roadmap.md` — phase **D** ("episodic memory / EVENTS; perceive actions; this kind hurt me").

## Goal

An agent's belief about a *kind* updates from real experience: when a monster attacks a civilian, the civilian's `{EnemyMonster}` category drifts more aversive — **"this kind hurt me."** Phase D/L1 is the **semantic** route: it extends the already-wired category-reinforce path to damage outcomes, lands the monster perceivable Kind atom + the monster innate prior (both deferred from C), and closes the experience→belief loop. The discrete-episode EVENTS store and the threat reputation cue are explicitly deferred (no consumer yet); the loop's *visible* behavioural payoff arrives in Phase E (vicarious) and for kinds that hurt you without looking dangerous.

## Governing principles (carried)

- **Beliefs are formed/learned, never stamped verdicts.** The kind-belief lives in the agent's `MeaningsStore` category nodes (innate-seeded in C, reinforced from episodes here). No `Danger`/`Predator` atom.
- **Reuse the wired machinery; don't build a parallel path.** The reinforce loop (`MemoryReinforceSystem` → `MemoryReinforceIntent` → `Recognize` + `Reinforce`) already exists for social events; D adds `DamageEvent` as a source, nothing more.
- **Seed the structure, let it fatten.** The monster prior is the C-deferred row in the same kind-keyed `InnatePriors` table; D fills the `PriorTarget.Kind` seam C stubbed.
- **Frozen placeholders.** All constants are frozen starting values, tunable later. The soak is nondeterministic run-to-run — validated by behaviour-sense (here: "does the belief learn from experience"), never byte-diff.

## Substrate this builds on (verified)

- **The reinforce path is wired for social events.** `MemoryReinforceSystem.Update` (`Assets/Sim/Engine/Units/MemoryReinforceSystem.cs`) loops `HelpGrantedEvent`/`HelpRefusedEvent`/`GreetingEvent`/`DislikeNearbyEvent` → `Emit(self, other, outcome)`; `Emit` does `sig = _perceivable.Signature(other); if (sig.Count==0) return; publish MemoryReinforceIntent { Perceiver, Signature, Outcome }`. Outcomes: `Grant +1.0, Refuse −1.0, Greet +0.5, Dislike −0.5`.
- **Reinforce DROPS on an unrecognized signature** (`AgentMemoryRegistry.cs:141-147`): `cat = rm.Meanings.Recognize(sig); if (!cat.IsNone) rm.Meanings.Reinforce(cat, sig, outcome);`. **No create-on-miss** — the category must already exist (categories are created by the THINGS *perception* path, not reinforce). ⇒ the innate monster prior is the prerequisite that makes `{EnemyMonster}` exist to reinforce.
- **`DamageEvent` already carries the attacker.** `CreatureSystem.Update` attack resolution publishes `DamageEvent { Target, Source, Amount, Type }` (`SimEvents.cs:32`); `Source` = the attacking creature's `EntityId`. `HealthSystem` consumes it (and fires `DeathEvent { Entity, Killer, … }` on a fatal hit).
- **Monsters are not yet categorizable.** `CreatureSystem.TrySpawn` stamps `CreatureForms.Beast` form atoms (`Fanged/Fast/Size`) + `IdentitySetIntent (Kind=EnemyMonster)`, but **no perceivable Kind atom** — so `Signature(monster)` is empty.
- **`InnatePriors` has the seam.** C left `enum PriorTarget { Self }` with a documented note that `Kind(X)` out-group targets arrive in D; `SeedPriors` builds `Self → Signature(self)`. `MeaningsStore.Recognize(AtomBag)→CategoryId`, `Reinforce(CategoryId, AtomBag percept, Fixed outcome)`, `RecognizedValence(...)` confirmed.
- **The threat-gate prevents a social feedback loop.** `SubjectiveSystem.InterpretPercepts` skips greet/dislike processing when `r.Threat > 0`. A monster reads `Threat > 0` (form atoms), so no `GreetingEvent`/`DislikeNearbyEvent` fires toward it — the *only* reinforce source for a monster is `DamageEvent`.

---

## Locked decisions

### D1 — Monster perceivable Kind atom

`CreatureSystem.TrySpawn` stamps a perceivable **Kind atom** alongside the form atoms:
`Events.Publish(new StampAtomIntent { Entity = id, Type = PerceivableAtoms.Kind(EntityKind.EnemyMonster), Value = Fixed.One });`
So `Signature(monster) = {EnemyMonster}` (the Kind atom is identity; the form atoms are not). Monsters become recognizable as "a monster." This makes the C-deferred monster prior matchable and is the key D-prerequisite.

### D2 — `PriorTarget.Kind` seam

`InnatePriors` (`Assets/Sim/Engine/Units/InnatePriors.cs`) gains the out-group target the seam was reserved for:
- `enum PriorTarget { Self, Kind }` + the believed-about `EntityKind` carried on the `InnatePrior` (e.g. an `EntityKind Kind` field, used only when `Target == PriorTarget.Kind`).
- `SeedPriors` (`AgentMemorySeeding.cs`) handles the new case: `PriorTarget.Kind → AtomBag.Create([ new Atom(PerceivableAtoms.Kind(prior.Kind), Fixed.One) ])` (the `{EnemyMonster}` prototype), else `Self → Signature(self)` (unchanged).

### D3 — The monster innate prior (the prerequisite)

`InnatePriors.For(CivilianNPC)` gains a second entry: `(PriorTarget.Kind=EnemyMonster, valence −0.6, confidence 0.3)`. So at spawn a civilian seeds a `{EnemyMonster}` category (prototype = the monster Kind atom, valence −0.6) — innate prey-of-predator wariness, AND the existing node that `DamageEvent` reinforces. Without it the reinforce would drop (no category). The in-group `(Self, +0.2)` row is unchanged.

### D4 — Damage reinforces the kind

`MemoryReinforceSystem.Update` gains one source, mirroring the social loops:
`foreach (ref readonly var d in Events.GetEvents<DamageEvent>()) Emit(d.Target, d.Source, Hurt);` with `const double Hurt = −1.0`.
So a monster attacking a civilian → `Emit(victim, monster, −1.0)` → `Signature(monster) = {EnemyMonster}` (D1) → `Recognize` matches the seeded category (D3) → `Reinforce` nudges its valence toward −1.0 and (confirming, same sign) raises confidence. The civilian's monster-belief deepens from the innate −0.6 toward −1.0 with each attack — **this kind hurt me**. A fatal hit reinforces a dying agent (harmless, no consumer); non-fatal hits on survivors are the meaningful ones.

### D5 — Deferred consumers (explicit)

- **Threat reputation cue** — wiring the learned kind-danger into `ThreatRead` (the roadmap's "Phase-C/D reputation cue") is **deferred**: redundant for monsters (already maximally feared via form), and its value needs a kind that hurts you without looking scary. The drifted category feeds `Interpret`'s social valence only (existing path).
- **EVENTS episodic store** — recording discrete episodes to the dormant `Stores.Events` is **deferred**: nothing recalls episodes until the planner's recall door (Phase F).
- **Action perception / witnessing** — broadcasting behaviour atoms and vicarious ("hurt my friend") learning is **Phase E**.

---

## Honest payoff note

In L1 the only attacker is the monster, which is **already maximally feared via the threat channel** (relative-size form read), and the prey-veto **masks** the social valence the drifted category feeds. So the experience→belief loop is **structural in L1** — its output is correct and measurable but behaviourally subtle. That is expected and consistent with B/C (structure ahead of full payoff). The loop is the foundation Phase E (vicarious transmission) and the reputation cue build on; its visible bite arrives with kinds that hurt you without looking dangerous.

## Validation

- **Unit tests.** A `DamageEvent` from a recognized attacker reinforces the victim's matching category toward the outcome (seed a `{EnemyMonster}` category, run the reinforce path with a `DamageEvent`, assert the category's valence drifted negative + confidence rose). The monster Kind atom makes `Signature(monster)` non-empty. `InnatePriors.For(CivilianNPC)` now has the in-group + monster entries; `SeedPriors` with a `PriorTarget.Kind` entry seeds a `{EnemyMonster}` category a civilian then recognizes at −0.6. A reinforce on an un-seeded kind is dropped (documents the no-create-on-miss prerequisite).
- **Behaviour-sense soak** (Gothway + Gallotale, NOT byte-diff): every civilian spawns with **two** categories now (in-group +0.2 AND monster −0.6 — `cat/agent ≈ 2`, vs C's 1); over the day the **monster category drifts more negative** as attacks land (`maxAbs` toward −1.0, monster nodes among `reinforcedNodes`); population stable and in-regime; no new artifact (no spurious social events toward monsters, fleeing/kills unchanged in character).

## Frozen constants (all tunable)

| Constant | Value | Where |
|----------|-------|-------|
| `Hurt` outcome | −1.0 | `MemoryReinforceSystem` |
| Monster innate prior valence | −0.6 | `InnatePriors` |
| Monster innate prior confidence | 0.3 | `InnatePriors` |

## Out of scope / future

- **D/L2 or E:** the threat **reputation cue** (learned danger amplifies `ThreatRead`); behaviour-atom broadcast + **witnessing** (perceive another's attack); **vicarious** reinforcement ("monsters hurt my friend").
- **EVENTS episodic store** (discrete episode records + recall) — when the planner (F) recalls episodes.
- More attacker kinds / damage sources (guard-on-civilian, agent-on-agent) — where learned danger stops being redundant with the form read.
- `DeathEvent`-based learning (witnesses of a kill) — a witnessing/vicarious concern (E).
