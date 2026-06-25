# Cognitive Framework Roadmap — From Atoms to Agents

> **Date:** 2026-06-24. **Status:** roadmap (living). The strategic spine that sequences the
> `what_is_*` canon into a build order. Distilled from `what_is_a_bunny.md` / `what_is_an_atom.md` /
> `what_is_a_memory.md` / `what_is_drive.md` / `what_is_world.md` (intent), the 2026-06-23
> `cognitive_layer_audit.md` (implementation truth), and the 2026-06-24 threat-classification design
> session.
>
> **How to read this.** Each phase is **What / How / Why**, with *Why* carrying the weight — the
> mechanisms are secondary to the reasons. No code: this maps the arc, not the keystrokes; each phase
> spawns its own spec → plan when it comes up. The invariants below hold at *every* phase; if a phase
> seems to violate one, the phase is wrong, not the invariant.

---

## Why this roadmap exists (the north star)

An agent is a **private process wrapped in a public molecule** (`what_is_a_bunny.md`). Everything it
perceives, values, fears, and learns is read **through atoms**, and the resulting verdict — *threat*,
*value*, *friend*, *danger* — is **always the perceiver's**, never a property of the thing.

The framework keeps tripping over one failure mode: **labeling.** Reaching for a `Predator`,
`Aggressive`, or `Danger` atom — collapsing a thing's rich properties into a verdict-tag so a pipe
(the recognizer, the planner) has something cheap to match. Every such tag is a lie about where
meaning lives. This whole roadmap is the apparatus for *refusing* it: make the substrate honest
(atoms = properties), then build the **reading** (affect), the **priors** (innate), the **memory**
(episodic → semantic, direct and vicarious), and finally let the **planner** consume it. Each phase
adds a *source* of the verdict; not one of them ever *stamps* the verdict.

We found this shape by asking a single concrete question — *how do we classify a thing as a threat,
without a label?* — and discovering the answer needs every faculty at once. **Threat is the whole
cognitive model in miniature.** This roadmap is that model, unfolded into buildable order.

```
  A           B          C           D            E            F
substrate → reading → innate     → memory     → society    → planner
 atoms      affect     priors       episodic     vicarious    cull +
 honest     (verdict   (species     "this kind   "hurt my     beacons +
            in the     recognition) hurt me"     friends"     recall door
            perceiver)
  └─ each phase adds a SOURCE of the verdict ──────────────┘   └─ consumes it ─┘
```

---

## The invariants (must hold at every phase)

1. **Atoms describe; they never conclude.** A thing carries form (`Fanged`, `Fast`, `Long`) and raw
   behaviour (its motion). There is no `Predator`, `Aggressive`, `Threat`, `Danger`, or `Value` atom.
2. **The verdict lives in the perceiver.** One fox broadcasts one bag; a rabbit reads terror, a wolf
   reads kin, a guard reads target. Same atoms, three verdicts.
3. **Disposition is my own makeup, not a flag.** It is my *hardware* (form — a rabbit is prey because
   it lacks predator hardware), my *evolved priors*, my *personality*, my *current emotion*. "Prey"
   and "predator" are no more labels than `Predator` was.
4. **Affect and cognition are separate faculties, never merged.** *Affect* = innate, memory-free,
   computed fresh each perception (tone × disposition × behaviour). *Cognition* = learned, in memory.
   Installing innate tone into the learned store (or vice-versa) is a category error.
5. **Perception-gated, never omniscient.** An agent reacts to, and learns from, only what it *sensed*.
   No `_creatures.Contains`-style oracles in the agent's path. Ground-truth checks are bookkeeping
   (metrics, place-danger), not the agent's read.
6. **Never mangle to fit a pipe.** If a consumer can't take the rich data, extend the consumer — don't
   down-sample the data into a tag.

---

## Where we are (the honest baseline)

The 2026-06-23 audit's verdict: **the substrate is built; the live wiring is missing or wrong.** Real
and working — the `AtomBag`, the memory core (best-built part of the codebase: bounded stores,
delta-records, surprise/arousal write-gate, consolidation), the affect rebuild, the drive engine, the
ODD planner's tree, and a genuine CQRS event bus. Inert or wrong — atom identity is hand-allocated
**id-bands** (collision-prone); the innate MEANINGS **seed is empty in production** (and, even if
installed, lives in a different atom-space than perception emits, so recognition can't fire);
**EVENTS is allocated but never written**; threat comes from an **omniscient oracle**; `Interpret` is
**duplicated**; a redundant **role-scalar stereotype stack** runs alongside the per-agent store; and
two percept bugs (stale somatic atoms, stale indoor sensed-lists) corrupt exactly what `Interpret`
consumes. **Phase 0** (planner foundation — chains propagate, a self-state cull socket exists,
terminals revalidate, prepotency culls by axis) **landed on master 2026-06-24.** That is the floor
this roadmap builds from.

---

## Phase A — The honest substrate (the atoms refactor)

**What.** Replace the id-band int scheme (`KindBase=1000`, `RoleBase=2000`, `RaceBase=3000`, …) with
one `AtomName` enum, and stand up the **four-faces catalog** keyed by that enum: *descriptor* (what it
physically asserts), *affordance* (atom-pattern → verb), *reaction* (physics — engine deferred), and
*affective tone* (innate valence + arousal). Identity atoms (`Civilian`, `Tavern`) and property atoms
(`Long`, `Fanged`) share one vocabulary. Affordances start emerging from atom-pattern matching instead
of a `BuildingKind` switch.

**How.** The enum *is* the identity — compiler-enforced uniqueness, no arithmetic, no band, no ceiling
to collide with. One frozen catalog replaces three scattered things today (the band-comment, the
salience-by-band switch, the affordance-by-building switch). The `AtomBag` storage is **unchanged** —
this renames and enriches type-metadata, it does not touch the backings. Migrate `PerceivableAtoms` /
`PlaceAtoms` / `SomaticAtoms` / `DoorAtoms` to enum members.

**Why.** Three reasons, escalating in importance:
1. *Hygiene.* The bands already silently collided once (doors landed on the place-kind base and had to
   be relocated); nothing guards the next one.
2. *De-risking cognition.* Recognition matches a perceived signature against seeded prototypes by
   distance over atom ids. On magic-number bands, a prototype and a real percept can sit in different
   ranges and **never match** — which is *precisely* why today's MEANINGS seed is dead (prototypes at
   1–4, perception at 1000–3999). A stable enum makes "what an agent can recognize" namable and
   testable. The atom doc says it outright: this refactor *de-risks* the seed install.
3. *The deep reason.* **This is where invariant #1 becomes structural.** The catalog has a *tone* face,
   not a *verdict* face — there is, by construction, nowhere on an atom to write "threat." The refactor
   isn't cosmetic; it is the substrate encoding the no-label rule. Build it **first** because every
   later phase names atoms, and they must name honest ones.

**Maps to.** `what_is_an_atom.md` (canon); **spec'd** at
`docs/superpowers/specs/2026-06-24-atoms-refactor-atomname-design.md` (full blast-radius mapped there).
*Decision #1 — resolved: A before B*; the M1 spec is reframed to depend on it.

---

## Phase B — The affect layer (the reading)

**What.** `Interpret` produces the verdict — valence, threat — from **tone × disposition × behaviour**
and nothing else: no role-scalar stereotype, no `_creatures` oracle, no label. The **prey-veto**: one
sufficiently aversive atom, read through a vulnerable disposition, dominates the whole molecule (a
meadow of clover still reads *flee* under one fox). Threat moves from an omniscient type-check to an
earned, perceived read.

**How.**
- Things carry real form atoms bearing affective **tone** (`Fanged` −/high, `Blood` −/high, `Coin`
  +/high).
- **Disposition = my own makeup:** *vulnerability* (my form vs the other's harm-capacity — the bunny
  reads fangs as terror because it lacks matching hardware; the wolf reads kin), *personality*
  (HarmAvoidance), *emotion* (arousal).
- **Behaviour = engagement**, read from substrate kinematics (distance, closing, orientation) — the
  "is it coming for me *now*" term that a static atom can't carry. A sleeping fox ≠ a charging fox.
- Aggregate non-linearly (veto) → `EntityRead`. **Delete** the oracle from `Interpret`, **delete** the
  role-scalar `MeaningsSystem`/`Registry`, **de-duplicate** the two `Interpret`s into one.
- **Division of labour** with the existing fear stack: `Interpret` decides *is there a cue, how
  strong* (capacity/engagement/vulnerability); the fear controller decides *how loud*
  (vigilance/arousal/ambiguity); the planner decides *fight or flight* (courage/capacity).

**Why.** This is where invariant #2 becomes real: the hard-coded `Interpret` special-cases
(creature → threat, beggar → stigma) dissolve into tone+disposition rules instead of `if`s. Dropping
the oracle is **safe and verified** — the whole downstream (fear, flee, guard targeting) already reads
`EntityRead.Threat`, so only the *producer* changes; and the oracle was never spatially omniscient
(creatures are already sense-gated), so it's a type-check → atom-check swap behind the same gate. Ship
it in honest rungs: **L1** (capacity tone + proximity — off the oracle) → **L2** (real kinematics).

**Maps to.** Reframes the **M1 spec** as this phase's first slice (*Open decision #3*). Audit §2.3.
The two percept bugs ride here as prerequisites — they corrupt exactly what `Interpret` consumes.

---

## Phase C — Innate priors (evolved specificity)

**What.** Per-perceiver-**species** priors that add a *bonus* to the read for specific cue-patterns —
innate predator recognition. The rabbit fears foxes on first sight; the fox sees the rabbit as food.

**How.** An authored prior table on the perceiver's species (frozen, beside `DriveDefs`), keyed on the
target's **real** kind/form atoms, feeding a bonus into the affect read — **never** a tag stamped on
the target. The verdict still forms in the perceiver; the asymmetry (predator-sees-food /
prey-sees-threat) falls out of two perceiver-priors over one encounter, with no relationship-table on
the pair.

**Why.** Pure emergent affect treats all fanged-fast-larger things alike, and only *after* perceiving
them — it can't explain *first-sight* fear, or why a rabbit fears a fox more than an equally-armed
animal it was never primed for. Evolution writes the specificity. Crucially, **this is the correct
home for the seed table we deleted**: relocated from "every agent: predator = −1 in *cognition*" to
"rabbit-species: fox-cue → +threat in *affect*" — innate, perceiver-side, keyed on real atoms. Same
data instinct, honestly placed; all six invariants intact.

**The shared structure — the stereotype.** A kind-keyed prior *is a stereotype*: a **category belief**,
applied when individual evidence is absent and **overridden the moment it arrives** — the memory core
already does exactly this (recall = predicted ⊕ delta, *delta wins*): a *new* canid gets the stereotype;
the known individual *Rex* gets his own dossier. So **Phases C, D, and E are three writers of one
structure** — the kind-keyed category-belief store: innate seed (C), learned from experience (D),
transmitted by gossip/culture (E). This also sharpens the C/B line — the stereotype is a *belief*
(cognition, overridable), **not** an atom's affective tone (B, memory-free) — and it is still no label:
the belief lives in the perceiver while the fox only ever broadcasts `Canid`. Cost accepted:
kind-stereotypes over-generalize (the friendly dog is feared too) and resist disconfirmation under
avoidance — both realistic, with the category-prior **decay rate** as the knob for how lasting a
prejudice is. The hard-coded `beggar → stigma` special-case is simply the first stereotype this turns
into data.

**Maps to.** *Decision #2 — resolved: kind-keyed* (see Open Decisions). Form-keyed
resemblance-generalization stays a later enrichment as the form vocabulary fills (the atom doc's
identity-slider, OQ#3).

---

## Phase D — Cognition: episodic → semantic memory ("this kind hurt me")

**What.** Wire the **EVENTS** pillar. Agents perceive **actions** (`A did X to B`), not just forms, and
consolidate witnessed episodes into **category priors** (MEANINGS, over the actor's signature) and
**instance dossiers** (THINGS). Learning that *generalizes*: not "entity #42 bit me" but "*foxes* bite."

**How.** A new perception channel — **witness-gate** action events (damage, death, help) by
sense-range (you only learn what you saw; no omniscience). **Episodic encode**: (actor-signature,
action, victim, arousal); the write-gate (surprise *or* arousal) already exists, so high-arousal harm
flashbulbs. **Consolidation** (sleep-gated, already built) generalizes the episode's charge onto the
actor's kind/form signature and sharpens the dossier. Recognition recalls predicted ⊕ delta; the
learned charge enters `Interpret` as a **third source** alongside the innate prior and live affect.

**Why.** Innate + affect give a reactive *animal*; memory gives an *agent that learns*. This is the
audit's **missing pillar** (EVENTS allocated, never written), and "this kind hurt me" is its exact
motive. It is also the *legitimate, earned* cousin of the deleted seed — category charge from
experience, not authored verdict. Most of the machine already exists (the memory core is the
best-built part of the codebase); Phase D is mainly **wiring** — the witness-gate and feeding
consolidation — not new substrate.

**Maps to.** Audit §2.2 (EVENTS gap; THINGS arousal hard-zeroed; MEANINGS lacks decay);
`what_is_a_memory.md`.

---

## Phase E — The social layer (vicarious learning; reputation; culture) ("hurt my friends")

**What.** Learn from harm and help done to **others**, weighted by **regard**: a friend mauled teaches
strongly, a stranger weakly, an enemy mauled maybe teaches nothing — or relief. Out of this comes
collective danger-knowledge, **reputation**, and the seed of **culture**.

**How.** The witnessed-action episode from Phase D already carries the victim; scale the update by my
*regard-to-victim* (from the relations registry). Same observed action, perceiver-weighted update —
invariant #2 applied to *learning*, not just to the instantaneous read. Later, **gossip** (knowledge
*communicated*, not witnessed) becomes a further channel via the communication substrate.

**Why.** A whole community comes to fear foxes though most were never bitten — because they *watched*,
and weighted what they saw by who it happened to. Reputation and culture therefore fall out of
perception + memory + relations, not a faction table. Distinct from Phase D because it adds the social
weighting and is where collective phenomena emerge; depends on D's EVENTS wiring plus the regard graph
(which exists).

**Maps to.** The relations registry; the communication-substrate spec (gossip, later).

---

## Phase F — The planner consumes it (the payoff)

**What.** ODD's **drive-biased cull** (now it has the self-state socket from Phase 0), **goal-beacons**
(general `(MatchFn, BeaconAction)` goals), and the **memory-recall door** (an active drive raises a
channel so the *remembered* satisfier reaches the market as a percept). Attention finally
need-weighted *and* consumed.

**How (brief).** The cull removes ads by the *axis a drive's gate-edges name*; beacons inject
self-advertised goals into Object Zero; recall surfaces remembered satisfiers as percepts so the
planner never queries memory directly.

**Why.** The audit's operative constraint: *do not* layer the cull and beacons until the foundation is
sound — chains must propagate (Phase 0) and the reads must be honest and subjective (B–E). With the
verdict now sourced from affect + innate prior + learned + social, the planner steers over real
subjective truth, and "self as a first-class source" finally pays off in behaviour. It is last because
it is the **consumer**; every phase before it exists to make its inputs honest.

**Maps to.** Audit Phase 2; `odd_spec.md` / `odd_convergence.md` (O3 goals + beacons).

---

## Cross-cutting threads (carried through every phase)

- **Behavioural tests on the live path.** The unit suite has repeatedly given *false confidence* (a
  tree test that culls nothing while the live town produced zero chains; an EVENTS-capacity test for a
  store never written). Every phase ships a behavioural proof, not just helpers.
- **Replay-determinism.** There are zero replay assertions today and real ordering hazards. Add a
  same-seed-twice-equal test as the substrate hardens.
- **Tune after, not during.** Every constant — tones, priors, fear gains, creature pressure, seed
  valences — is a frozen placeholder until *one* tuning pass on a survivable economy. Build the
  mechanism correct; make it frequent later.
- **No new oracles.** Each new faculty is perception-gated. If something genuinely needs ground truth,
  it is bookkeeping (metrics, place-danger), never the agent's read.

---

## The through-line

Every phase adds a **source** of the verdict — atom tone, then innate prior, then episodic memory,
then semantic generalization, then social weighting — and the final phase adds the **consumer**, the
planner. At no point does the verdict get stamped on a thing. *Threat*, *value*, *friend*, *enemy*,
*dangerous place*, *reputation* are revealed to be **one shape**: a perceiver reading atoms through its
disposition and its memory. That is why each new faculty is a new *input to one machine*, not a new
special case — and why "how do we classify a threat" turned out to be, in full, "how does cognition
work."

---

## Open decisions (flagged, with recommendations)

1. **AtomName migration vs in-flight band work.** ✅ **Resolved 2026-06-24 — Phase A first**, spec'd at
   `2026-06-24-atoms-refactor-atomname-design.md`. The M1 spec is reframed to depend on it.
2. **Innate prior keying (Phase C).** ✅ **Resolved 2026-06-24 — kind-keyed:** the prior keys on the
   target's *kind* atom (`Canid`) — a **stereotype** (category belief), the one structure Phases C/D/E
   all write (innate seed / learned / transmitted), individual dossiers overriding it (delta wins).
   Form-keyed resemblance-generalization is a later enrichment (the identity slider).
3. **M1's cut.** ✅ **Resolved 2026-06-24 — reframed to Phase B / L1** (capacity tone + proximity, off
   the oracle; L2 kinematics fast-following). The predator-atom approach is retired; the M1 spec now
   carries a Reframe banner, and a fresh Phase-B/L1 spec follows once Phase A lands.
