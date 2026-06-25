# Cognitive Phase C / L1 — Innate Species Priors (design)

**Status:** design, ready for implementation planning
**Date:** 2026-06-25
**Predecessors:** Phase A (atoms refactor, merged) · Phase B/L1 (the affect layer, branch `cognitive-phase1-subjective-interpret`)
**Roadmap:** `docs/cognitive_framework_roadmap.md` — phase **C** ("innate species priors").

## Goal

A newborn agent is not a blank slate. It spawns with **evolved priors about kinds** — a small, believable starter set seeded into the *learnable* structures Phase B left, so that from its first tick an agent reads strangers and threats through an innate lens, which experience then drifts. Phase C touches **two channels**:

1. **The threat channel (disposition).** The same monster is scarier to a small unarmed civilian than to a big armed one. This emerges from the perceiver's **own form**, not a flag.
2. **The social channel (stereotype).** A civilian is born mildly warm to other townsfolk and wary of monster-kinds — innate **category beliefs** that feed `Interpret`'s stranger-read and then learn from experience.

Both are "evolved priors" but land in different code paths. This is the roadmap's canonical *kind-keyed* layer and the seed structure that **D (episodic) reinforces** and **E (social) transmits**.

## Governing principles (carried from the roadmap)

- **No verdict atoms / formed in the perceiver.** Threat and regard are *reads*, never stamped labels. Disposition is the perceiver's own makeup (here: its **form** — size/weapons), read live.
- **Never down-sample to fit a pipe; seed the structure, let it fatten.** The starter prior table is small because believable starter *content* is small today — **not** because the structure is capped. The `InnatePriors` table and the innate-node store are explicitly built to grow (more perceiver-kinds, more believed-about kinds, richer-than-valence beliefs); D and E write to the same nodes.
- **Delta wins.** An individual dossier (`relations`) overrides the category read entirely; within a category, real encounters (`Reinforce`) drift the innate node. Priors are a *lean*, not a conviction.
- **Frozen placeholders.** All numeric constants below are frozen starting values, tunable later; the engine soak is **nondeterministic run-to-run**, so it is validated by behaviour-sense (believable / in-regime), never byte-diff.

## Substrate this builds on (verified)

- `SubjectiveSystem.ThreatRead(AtomBag bag, out double threatValence)` — the prey-veto = max over aversive cues = `weaponTone × targetSize` + raw mass-menace `0.3 × targetSize`. Reads only the *target's* form; no perceiver introspection. (`Assets/Sim/Engine/Units/SubjectiveSystem.cs`)
- `SubjectiveSystem.Interpret(...)` stranger path: `baseValence = newV` from `agentMem … mem.Meanings.RecognizedValence(perceivable.Signature(other), out lv, out _)` — confidence currently ignored by the read.
- `MeaningsStore` (`Assets/Sim/Memory/MeaningsStore.cs`): `RecognizedValence(signature, out v, out c)` → `Recognize(signature)` nearest-prototype match (or `CategoryId.None`); `Reinforce(CategoryId, percept, outcome)` drifts a node; **innate nodes already learn** (CategoryNode), but there is **no seed entry point** — the store starts empty at spawn.
- `PerceivableRegistry.Signature(id)` = the entity's **identity atoms only** (Kind/Role/Race).
- `AgentMemoryRegistry`: `Seed(id)` (empty store), `SeedPlace(...)`. `AgentMemorySeeding.SeedAgent(world, agent)` (`Assets/Sim/World/AgentMemorySeeding.cs`) currently calls only `SeedPlaces`; `SeedReputations`/`SeedSocial` are stubbed as future siblings — the **SeedX slot**.
- Spawn: civilians via `TownLoader.Spawn` (stamps `Kind`/`Race`/`Role` identity atoms, `AgentMemory.Seed`); monsters via `CreatureSystem.TrySpawn` (stamps `CreatureForms.Beast` = `{Fanged, Fast, Size=0.5}` + identity `Kind=EnemyMonster`).
- `EntityKind`: `Unknown, Player, EnemyClass, EnemyMonster, CivilianNPC, StaticNPC`. `PerceivableAtoms.Kind(EntityKind)` → the Kind atom.

---

## Part 1 — the form-based threat disposition (threat channel)

### C1 — Relative-size threat read

`ThreatRead` stops scaling by the target's *absolute* size and scales by **relative size** instead:

```
relativeSize = targetSize / perceiverSize
```

- **Weapon cue:** `menace × tone.Arousal × relativeSize`. A fanged thing your own size is a fair threat (full menace at rel = 1); bigger is worse, smaller is less. A big perceiver shrugs off a fanged squirrel; a small civilian dreads the same-fanged Beast.
- **Mass-menace cue (relativized):** `SizeMassMenace(0.3) × max(0, relativeSize − 1)` — fires only when the **target is bigger** than the perceiver. Same-size or smaller unarmed → 0. This kills the latent bug that absolute mass-menace would create the moment civilians get a `Size` (they would otherwise menace each other).
- `threat` still clamps to `[0,1]`; `threatValence` to `≥ −1`. The dominant cue still carries its own tone as `threatValence`.

The perceiver's disposition is therefore **emergent**: its own size is the denominator. No temperament scalar, no flag.

### C2 — Size on all human agents

Every human agent carries an honest `Size` form atom, stamped at spawn:
- Civilians/guards (`TownLoader.Spawn`): `Size ≈ 0.4` (frozen).
- Monsters already carry `Size = 0.5` (`CreatureForms.Beast`), unchanged.

`Size` is the existing neutral `AtomCategory.Form` atom (no tone). Stamping it on humans is additive; it does not by itself make a human threatening (no weapon atoms, and relativized mass-menace is 0 against equals).

### C3 — `ThreatRead`/`Interpret` wiring

- `ThreatRead` gains the perceiver's own size as an input (e.g. a `double selfSize` parameter, or read from a passed self-bag). **Default `1.0` when the perceiver has no `Size` atom**, so un-sized agents and the existing Phase B `ThreatReadTests` revert exactly to today's absolute model until sizes are stamped.
- `Interpret` reads `self`'s `Size` from `perceivable.Bag(self)` and passes it into `ThreatRead`.
- Guard against divide-by-zero: clamp `perceiverSize` to a small floor (e.g. `≥ 0.01`).
- The Phase B `ThreatReadTests` are updated to the relative model (squirrel-vs-bear now expressed as perceiver-size, not just target-size).

**Calibration note (watch in soak):** with humans at 0.4 and Beast at 0.5, a civilian's read of a Beast rises well above the Phase B value (small prey vs big fanged predator = genuinely frightening). If the soak shows civilians too paralysed to function, the lever is the human/Beast size ratio (raise human size toward 0.5), not the formula. Frozen, soak-gated.

### C4 — Guard weapons deferred to C/L2

Arming guards (so their own form makes them bold) is **out of scope for L1**: a guard weapon atom would also fire *civilians'* `ThreatRead` (civilians reading armed guards as threatening) — a separate behaviour to design — and guards already attack monsters via the ODD guard ads regardless of their own fear read. L1 disposition = **Size-for-all + relative threat**. Armed boldness and the richer perceiver-capacity model are C/L2.

---

## Part 2 — innate social priors (social channel)

### C5 — `MeaningsStore.SeedInnate`

Add a load-time seed entry point to the store:

```
SeedInnate(AtomBag signature, Fixed valence, Fixed confidence)
```

It creates an **innate category node** whose prototype is `signature`, with the given starting `valence` and `confidence`, flagged innate. Thereafter the node behaves like any other: `RecognizedValence` returns it on a nearest-prototype match, and `Reinforce` drifts it from real outcomes. (The store already supports innate nodes that learn; this is the missing constructor path.)

### C6 — The `InnatePriors` table

A frozen, kind-keyed table: **perceiver `EntityKind` → list of `(believed-about kind, valence, confidence)`**. Open-ended by construction — rows and entries are added as the world grows; D and E write to the resulting nodes.

L1 starter content (the only perceiver that runs the social read today is the civilian; monsters are hunger-driven and do not interpret socially, so their row is empty for now):

| Perceiver | Believed-about kind | Valence | Confidence |
|-----------|--------------------|---------|------------|
| `CivilianNPC` | `CivilianNPC` (in-group) | **+0.2** | 0.3 |
| `CivilianNPC` | `EnemyMonster` | **−0.6** | 0.3 |

All other pairs: no innate node (novel → neutral, as today).

### C7 — `SeedPriors`

A new sibling in `AgentMemorySeeding.SeedAgent`, beside `SeedPlaces`:

```
SeedPriors(world, agent):
    kind = world.Identity[agent].Kind
    for (believedKind, valence, confidence) in InnatePriors.For(kind):
        signature = AtomBag.Create([ new Atom(PerceivableAtoms.Kind(believedKind), Fixed.One) ])
        world.AgentMemory.SeedInnatePrior(agent, signature, valence, confidence)
```

`AgentMemoryRegistry.SeedInnatePrior(agent, signature, valence, confidence)` is a new registry method **parallel to the existing `SeedPlace`** (a direct load-time write into the agent's `MeaningsStore.SeedInnate`, bypassing the runtime intent path — same pattern `SeedPlace` already uses). `SeedPriors` is wired at the existing spawn-seed call site (where `SeedAgent` already runs per agent at load). Idempotent per agent (seed once at spawn).

### C8 — Confidence semantics ("delta wins")

`Interpret` reads the node's **valence directly** (it ignores confidence post-B4), so the starter valence *is* the felt baseline (+0.2 / −0.6). Confidence governs how fast `Reinforce` drifts the node — **low confidence (0.3)** means a handful of real encounters move it. Independent of that, an individual dossier (`relations`) overrides the whole category read (`Interpret` checks `relations` before the category). Two override paths, both intact.

### C9 — Signature keying

Innate nodes are seeded keyed by the **Kind atom alone** (`{Civilian}`, `{EnemyMonster}`). At interpret-time the runtime signature is richer (`Kind + Role + Race`); the store's **nearest-prototype `Recognize`** is relied on to match the coarse innate prototype to the fuller runtime signature. The seed→match round-trip is verified by a unit test; if matching proves too strict, the fallback is to seed richer signatures (still kind-anchored). No change to `Signature` itself.

---

## Honest scope / payoff notes

- **Monster-wariness is mostly structural in L1.** The prey-veto (Phase B) already forces a monster's read aversive, so the −0.6 social prior is largely masked for monsters *today*. Its value is being the **seed** D reinforces ("this kind hurt me") and E transmits ("monsters hurt my friends"). The **visible** L1 social win is the **in-group warmth** (+0.2 — townsfolk reading each other warmly) and any weapon-less believed-about kinds.
- **The threat channel is the visible L1 behaviour change:** civilians read appropriately *more* afraid of monsters than the Phase B baseline, scaled by their own (small) size.

## Validation

- **Unit tests per piece.** The Memory pieces (`SeedInnate`, `InnatePriors`/`SeedPriors`) are float-free and isolated: seed → `RecognizedValence` returns the prior → `Reinforce` drifts it; a freshly-seeded civilian recognizes `{Civilian}` as mildly positive and `{EnemyMonster}` as wary. `ThreatRead` relative-size: squirrel-vs-bear expressed via perceiver size; same-size unarmed → 0; un-sized perceiver reverts to the absolute model.
- **Behaviour-sense soak** (Gothway + Gallotale, NOT byte-diff): civilians read more afraid of monsters than the Phase B reference and flee believably; **no civilian-fears-civilian artifact**; in-group warmth visible in social behaviour; population stable and in-regime. Compare regime, not bytes.

## Frozen constants (all tunable)

| Constant | Value | Where |
|----------|-------|-------|
| Human `Size` | 0.4 | `TownLoader.Spawn` |
| Beast `Size` | 0.5 (existing) | `CreatureForms.Beast` |
| `SizeMassMenace` | 0.3 (existing) | `ThreatRead` |
| Civilian→Civilian valence | +0.2 | `InnatePriors` |
| Civilian→EnemyMonster valence | −0.6 | `InnatePriors` |
| Innate confidence | 0.3 | `InnatePriors` |
| Perceiver-size floor | 0.01 | `ThreatRead` |

## Realization notes (from implementation-plan grounding, 2026-06-25)

Grounding the plan against the live code refined three points — all faithful to the design intent (C9 anticipated the matching one):

1. **`SeedInnate` wraps an existing primitive.** `MeaningsStore` already exposes `public CategoryId AddNode(AtomBag prototype, Fixed valence, Fixed confidence, bool innate)`. So C5's `SeedInnate(signature, valence, confidence)` is a one-line wrapper calling `AddNode(..., innate: true)` — a named load-time entry point, no new machinery.

2. **Recognition is strict → in-group prior keyed by `Signature(self)` (C9 fallback taken).** `MeaningsStore` matches by L1 distance ≤ `MatchThresholdRaw = 128` (0.5 in Q8) — near-exact. A coarse `{Civilian}` prototype does NOT match a `{Civilian, Role, Race}` runtime signature (each extra identity atom adds 1.0 ≫ 0.5). So the in-group prior is seeded keyed by the agent's **own** `Perceivable.Signature(self)` (`{Kind, Role, Race}`), matching same-kind-role-race kin exactly. In-group warmth is therefore **"same-signature kin"** in L1 (e.g. a resident warm to fellow same-race residents), broadenable when recognition gains a coarse kind-level path (future). `Signature(self)` is available at seed-time (identity atoms are stamped before `SeedAgent` runs).

3. **The monster *social* prior moves to Phase D; monster *wariness* is delivered by the threat channel.** Today `CreatureSystem.TrySpawn` stamps form atoms but no perceivable **Kind** atom, so `Signature(monster)` is empty and an `{EnemyMonster}` social prior could not match without first adding that atom. And the prey-veto already forces a monster's read aversive — so a social monster-prior would be **redundant and masked** in L1. The user's "wary of predators" is met by **Part 1** (relative-size disposition: small civilians read monsters as high-threat and flee). The social-category monster prior — and the monster perceivable Kind atom it needs — land naturally in **Phase D**, where episodic attribution ("this *kind* hurt me") makes them non-redundant. **L1 social content is therefore the in-group warmth only**, in a table built to grow (the `PriorTarget` seam carries `Self` today, `Kind(X)` targets with D).

**Net L1 `InnatePriors`:** `CivilianNPC → [(Self, +0.2, conf 0.3)]`. The −0.6 monster row from C6 is deferred to D per note 3.

## Out of scope / future

- **C/L2:** guard/armed-agent weapon form atoms (armed boldness + the civilian-reads-armed-guard behaviour); richer perceiver-capacity disposition.
- **D (episodic):** wire EVENTS; `Reinforce` these innate nodes from witnessed/experienced outcomes ("this kind hurt me").
- **E (social/vicarious):** transmit category beliefs between agents (reputation/culture).
- More perceiver-kinds and believed-about kinds; richer-than-valence beliefs; race/role/faction-keyed priors.
