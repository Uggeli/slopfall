# Cognitive substrate — the Atoms mind, made real (S1–S4)

Companion to `living_world.md` (the L-stack: entropy, lifecycle, a subjectivity *slice*,
begging) and `odd_convergence.md` (the decision-loop convergence O0–O5). This track builds the
**composable cognitive primitives** of the Atoms architecture (`~/omat/Atoms/docs/what_is_*`),
so the behaviors we have been hand-coding — grudges, gratitude, stigma, shame, fear-of-X,
taboos — stop being bespoke systems and start **emerging from the interplay**.

## The thesis (why this, why now)

The L-stack worked, but it is **Atoms-light**: each behavior is its own hand-tuned system —
`RequestSystem`'s literal `+0.3/−0.2` regard impulses, `SocialSystem`'s affinity hash, L3's
`RelationFactor` lens jammed into the decider, `PerceptionSystem`'s bespoke greet/dislike. That
is *more code for less world*: the pieces don't compose, and you still don't get the emergent
richness (a beggar feels no shame; nobody stigmatises begging; a grudge is a magic number).

In the real framework you build **~4 primitives once, author a little seed data, and the rest
falls out**: the same `interpret()` that recognises a friend also reads a beggar as pitiable or
contemptible; the same emotion residual that is gratitude is also resentment and fear; the same
MEANINGS valence that says "fox = danger" says "(self, beg) = shame." Composition becomes free.
(The honest caveat: emergence is not *literally* free — someone still authors `DriveDefs`, the
MEANINGS seed, the conscience nodes. The win is that the **work moves from coding each behavior
to building the primitives + seeding them**, and the behaviors then multiply for free.)

**Why now:** Atoms' own discipline is *don't build the machinery until a behavior demands it*.
The L-stack was that proto — and begging demanded perception *and* conscience, loudly. So this
is precisely the moment to stop bolting stages and build the substrate the wedge was always
meant to converge to (`docs/odd_spec.md` is the north star; see [[atoms-mind-model]]).

## The four primitives (membrane → emotion → MEANINGS → conscience)

- **S1 — The membrane: `interpret()` → `SubjectiveView`** (`cognitive_substrate_S1_membrane.md`).
  A real per-agent interpretation register between raw senses and the marketplace: recognition
  + valence + trust + attention for each perceived thing. Atoms: bunny doc (private process /
  public molecule), world doc (rows 1a/1b/2, the `SubjectiveView`).
- **S2 — Emotion: two-pole drives + `Affects` + emotion-as-controller**
  (`cognitive_substrate_S2_emotion.md`). A drive becomes (level pole, target field); emotion is
  the residual; it biases the membrane's attention and prunes the marketplace. Atoms: drive doc.
- **S3 — MEANINGS: the semantic store + consolidation** (`cognitive_substrate_S3_meanings.md`).
  Category/fact nodes (prototype + predicted atoms + valence + confidence); episodic→semantic
  consolidation in sleep. This is the substrate `interpret()` reads, so valence stops being a
  hand-set scalar. Atoms: memory doc.
- **S4 — Conscience: a valence overlay → shame / guilt / stigma**
  (`cognitive_substrate_S4_conscience.md`). MEANINGS nodes over `(self, verb, object)` charge
  anticipated valence, read through emotion-as-controller, biasing `V` at the marketplace join.
  Shame, stigma, taboos, crime-as-tag, the proud-agent-who-starves — all fall out. Atoms:
  conscience doc.

```
S1 membrane ──► S2 emotion ──► S3 MEANINGS ──► S4 conscience
(where valence    (writes        (gives interpret    (pure composition of
 is read/written)  affect, prunes  a real substrate)   S1+S2+S3 + a seed)
```

**The order is forced by dependency**, not preference:
1. S1 first — the membrane is the shared place every later stage deposits and reads
   interpretation; without it emotion/MEANINGS/conscience have nowhere to land.
2. S2 next — emotion is the pole residual; it points at *perceived* entities (needs S1) and is
   what prunes the marketplace (the controller rung).
3. S3 next — MEANINGS gives `interpret()` (S1) a semantic substrate so valence is a lookup, and
   consolidation is gated by arousal/surprise (needs S2).
4. S4 last — conscience is *literally* MEANINGS nodes (S3) over self-actions, read through
   emotion-as-controller (S2) and the membrane (S1). It is a composition, not new machinery.

## What each stage RETIRES (the payoff — less code, more world)

| Stage | Hand-coded special-case it deletes/absorbs |
|---|---|
| **S1** | L3's `RelationFactor` lens collapsed into `OddSystem.Decide` → reads `SubjectiveView`; `PerceptionSystem`'s bespoke greet/dislike → falls out of `interpret()` valence |
| **S2** | `RequestSystem`'s literal `+0.3/−0.2` impulses; `SocialSystem`'s affinity-hash + manual regard growth; the greet/dislike `RelationImpulseEvent`s → all become **emotion residuals** + memory writes |
| **S3** | the raw-episode-only `MemoryRegistry` → delta-records over a semantic store; makes `interpret()`'s valence a real MEANINGS lookup instead of the dossier scalar |
| **S4** | nothing *exists* to delete (conscience is net-new) — but begging-shame, stigma, taboos, "proud agent starves" become **authored/learned nodes**, not the systems they'd otherwise each need |

The dossier (`RelationsRegistry`) and the marketplace (`ads`, uniform `V`) are **not** retired —
they are genuine Atoms pieces (the THINGS store and the ODD core). The "light" debt is the
*hand-tuned numbers and the collapsed interpret*, and that is what dissolves.

## Relationship to the other tracks

- **`odd_convergence.md`**: S1 *is* O1 (the senses→percepts→subjective layer the convergence
  calls for). The substrate track is the **cognitive half** of the convergence; the ODD tree
  (O2/O3: Enables/goals) is the *planning* half and can proceed in parallel once S1–S2 land.
- **`living_world.md` (L1–L4)**: built on top. L1 decay and L2 lifecycle are substrate and stay;
  L3's lens and L4/`RequestSystem`'s impulses get **re-expressed on S1/S2 and deleted**. The
  begging social-souring finding (mass hand-coded grudges) is resolved *by construction* once
  refusals are emotion residuals (S2) and begging carries shame (S4).

## Discipline (carried from the other tracks)

1. **Proto-first.** Build each primitive because a behavior now demands it (we have them). Don't
   over-build ahead of need — e.g. don't add MEANINGS *split* mechanics or the full affect-spiral
   set until a proto shows the need (per `what_is_memory.md`'s own open-knobs).
2. **No premature tuning** ([[no-premature-tuning]]). Every threshold (θ_surprise, decay rates,
   charity/shame bars, ignition floors) is a frozen placeholder; the regional soak is the rig,
   tuned once the substrate is whole. A behavioral soak that looks wrong mid-substrate is
   expected and not acted on.
3. **Determinism is a hard invariant.** Pure-read interpretation; key-ordered passes;
   `hash(tick, selfId, …)` for any stochastic draw; fixed-point running stats in MEANINGS (the
   memory doc's A4/A5). No stage may be the reason two machines disagree.
4. **Single-writer registries.** Each new register (`SubjectiveView`, `Affects`, `Meanings`,
   conscience nodes) has one writing system; cross-system effects flow as events (the
   `RelationImpulseEvent` pattern), so the gather-reduce stays clean.

## Acceptance for the whole track (what the finished substrate should show)

- **S1**: two agents interpret the same entity/place differently by history; the decider *and*
  the percept layer both read one `SubjectiveView`; "ad is ad" preserved.
- **S2**: a grudge/gratitude is an emergent emotion residual (no literal `±0.x` in any system);
  emotion prunes the marketplace (a strongly-felt directed emotion narrows options).
- **S3**: MEANINGS nodes form by consolidation; recognition/valence become lookups; a *learned*
  category (e.g. "this keeper is stingy") changes interpretation and therefore behavior.
- **S4**: a conscience node makes a proud agent avoid begging (shame); a cold agent's
  `interpret()` stigmatises a beggar while a warm one pities — same beggar, opposite read; the
  begging social dynamics are now principled, not hand-tuned.

When these hold, the world is not just **alive** (the L-stack) but **mindful** — and the next
behavior is a node or a `DriveDef`, not a new system.
