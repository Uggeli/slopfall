# Agent Communication — Implementation Plan (multi-phase)

> REQUIRED SUB-SKILL: superpowers:subagent-driven-development / executing-plans. Steps use `- [ ]`.

**Goal:** Build the foundational communication layer — a hearing raw-sense + an `Utterance` substrate
(speech-act + atom-bag) whose reception routes by act — proven end-to-end by **danger spreading by
word of mouth** (a witness shouts → bystanders hear → ODD avoids the place), then a `Gossip` activity,
action `NoiseLevel`, and exposure to town3d.

**Spec:** `docs/superpowers/specs/2026-06-22-communication-substrate-design.md`. Branch
`agent-communication`.

## Global constraints
- **CQRS / one-tick latency:** utterances are `IEvent`s; new systems read settled state + publish;
  sole-writer registries apply. A spoken utterance is heard next tick, reception applied the tick after
  — uniform, fine for speech.
- **Determinism:** hearing uses `float` distance like `SenseSystem` (already in the deterministic
  path); any stochastic choice (who speaks, which fact, turn order) draws from `hash(tick, selfId)` per
  A5 — **never** a shared RNG stream. No floats beyond `Fixed` in memory writes.
- **Additive:** sight/`SenseSystem`, the economy, and existing decisions are untouched; comms only
  *adds* a sense + reads memory/relations + writes second-hand memory.
- **Reuse, don't duplicate** (per the substrate map): `PlaceObserveIntent`/`MergePlaceAtom`,
  `MemoryPerceiveIntent`, `Interpret().Valence` (trust), `RelationImpulseEvent`→`AffectsSystem`,
  `SocialSystem` grouping, `ActivityCatalog`.
- Test: `dotnet test Headless/Sim.MemoryTests` (unit) + `Sim.Host --soak`/`--engineworld` (behaviour,
  determinism). *(Sim.Tests is transport-broken by the in-progress Sim.Net deletion — not used.)*

---

## Phase 1 — the hearing raw-sense + utterance plumbing

**Outcome:** an `Utterance` emitted by a speaker is *heard* by every agent within its channel's audible
radius (addressee + eavesdroppers), and nobody outside it. The new sense, with nothing yet routed.

**New types** (`Assets/Sim/Engine/Units/CommunicationTypes.cs` or beside `AgentMemoryRegistry`):
- `enum CommChannel { Whisper, Talk, Shout }`, `enum SpeechAct { Inform, Request, Offer, Express }`.
- `struct Utterance : IEvent { EntityId Speaker, Audience; CommChannel Channel; SpeechAct Act; AtomBag Content; Fixed Confidence; }`.
- `struct HeardUtterance : IEvent { EntityId Hearer; Utterance Said; }` — the hearing sense's output.
- `HearingSystem : SimSystem(EventBus, PositionRegistry, TownGridRegistry)` — per `Utterance`, scan
  agents within `Radius(Channel)` of the speaker's position (coarse grid buckets like `SenseSystem`,
  **LOS-relaxed**), emit a `HeardUtterance` for each (including the speaker? no — exclude self).
  Radii are config consts (`Whisper≈3, Talk≈8, Shout≈25`), tuned later.

- [ ] **Step 1: failing test** `Headless/Sim.MemoryTests/HearingTests.cs` — rig 3 agents at known
  positions + an `Utterance(Talk)`; assert the in-radius non-addressee **and** the addressee get a
  `HeardUtterance`, the out-of-radius one does not; `Shout` reaches a farther agent that `Talk` didn't;
  speaker doesn't hear itself. (Rig pattern from `PlaceDangerTests`.)
- [ ] **Step 2: run, verify FAIL.**
- [ ] **Step 3: implement** the types + `HearingSystem`; wire into `SimWorld` systems[] (after
  `SenseSystem`).
- [ ] **Step 4: run, verify PASS;** `--engineworld` determinism still holds (no speakers yet → inert).
- [ ] **Step 5: commit** `feat(comms): hearing raw-sense — utterances heard by audible radius`.

---

## Phase 2 — Inform reception → second-hand memory + the danger payoff

**Outcome:** a witness who just saw a creature kill **shouts** it; bystanders who weren't witnesses
gain `DangerHere` for that building **second-hand** (weaker, trust-scaled), and ODD's Phase-2
`PlaceAversion` makes them avoid it. Danger spreads beyond the first-hand few — the loop closes.

**Changes:**
- `PlaceObserveIntent` gains a **`Fixed TrustScale`** (default `One` = first-hand). `MergePlaceAtom`:
  when `TrustScale < One`, cap the atom to `salience.Strength × TrustScale` and **drop SURPRISE/INNATE**
  (hearsay fades unless re-heard/witnessed; first-hand always dominates). First-hand path unchanged.
- `CommunicationSystem : SimSystem(EventBus, AgentMemoryRegistry, RelationsRegistry, SubjectiveView…)`
  consumes `HeardUtterance`; for `Act==Inform`, route Content:
  - place atoms (`PlaceAtoms` range) → `PlaceObserveIntent{ Agent=hearer, …, TrustScale = clamp(Interpret(hearer,speaker).Valence) × Confidence }`.
  - entity/episode atoms → `MemoryPerceiveIntent` with **low arousal** (shallow, fade-able trace).
  - (Request/Offer/Express → no-op stubs this phase.)
- **Minimal speaker:** in `PlaceDangerSystem`, the witnesses already computed each also publish one
  `Utterance{ Inform, Shout, Content=Danger@building, Confidence=One }` (fear-driven warning). Hearers
  who weren't witnesses learn it second-hand; witnesses already know it first-hand (value-wins keeps
  the stronger).

- [ ] **Step 1: failing tests** `Headless/Sim.MemoryTests/CommReceptionTests.cs` — (a) an `Inform`
  carrying `Danger@b` makes a hearer's PLACES gain `DangerHere@b` at **lower** strength than first-hand
  and **non-SURPRISE**, scaled by regard (≤0 regard → ~no belief); (b) eavesdropper (in radius, not
  addressee) gets it too; (c) `MergePlaceAtom` first-hand path unchanged (regression).
- [ ] **Step 2: run, verify FAIL.**
- [ ] **Step 3: implement** the `TrustScale` rider + merge cap, `CommunicationSystem` Inform routing,
  the witness-shout in `PlaceDangerSystem`; wire `CommunicationSystem` after `HearingSystem`.
- [ ] **Step 4: run, verify PASS;** **soak (3 day):** danger-memory count rises **beyond** the
  first-hand witness set (vs the ~6/340 Phase-2 baseline); economy/pop stable; determinism holds.
- [ ] **Step 5: commit** `feat(comms): Inform reception → second-hand danger; witness shout spreads it`.

---

## Phase 3 — Gossip activity + action NoiseLevel

**Outcome:** agents *choose* to gossip (an ODD activity), bound partners trade known facts over a
duration, and audibility is driven by a per-action `NoiseLevel` (whisper/talk/shout become its speech
tiers).

**Changes:**
- `ActivityCatalog.Spec` gains **`public double NoiseLevel`** (`ActivityCatalog.cs:10`); `HearingSystem`
  radius reads the **source action's `NoiseLevel`** (utterance channel maps to a noise tier), not a
  fixed per-channel const.
- New `ActivityKind.Gossip` (+ `Negotiate` reserved) in the enum (`Registries/BehaviorRegistry.cs`);
  `SpecFor` entries (`Social=true`, a `DurationMinutes`, mid `NoiseLevel`). ODD generates a Gossip ad
  (Social-gated) so agents pick it.
- `Gossip` execution: bound co-located partners (reuse `SocialSystem` grouping) each emit, on a turn
  cadence over the duration, an `Inform(Talk)` whose Content is a fact drawn from the speaker's memory
  (`hash(tick,id)` picks which). Reframe `SocialSystem`'s opinion-gossip onto emitting `Inform`
  utterances (so it spreads through the hearing sense, not a private fold).

- [ ] **Step 1: failing tests** — `NoiseLevel` drives audible radius (a louder spec reaches farther);
  a `Gossip`-activity agent emits `Inform` turns over its duration; a bystander overhears a gossiped
  fact and gains it second-hand.
- [ ] **Step 2: run, verify FAIL.**
- [ ] **Step 3: implement** `NoiseLevel` + reach-from-noise; `Gossip` kind/spec/ad; gossip-as-Inform.
- [ ] **Step 4: run, verify PASS;** soak: a fact known to a few spreads through gossip; behaviour/econ
  stable; determinism holds.
- [ ] **Step 5: commit** `feat(comms): Gossip activity + ActivitySpec NoiseLevel-driven hearing`.

---

## Phase 4 — expose utterances to town3d

**Outcome:** the client catches every communication — structured records on the snapshot stream,
rendered as a log/bubbles. (No LLM; that's the later render edge.)

**Changes** (`Headless/Sim.Web`, `Sim.Host`):
- **Sim-side buffer** (`UtteranceLogRegistry` or a ring on `WorldRunner`): append each tick's
  `Utterance`s (the pump is ~5 Hz but the sim ticks faster and the bus flips each tick, so
  **accumulate, don't sample**). `WorldRunner.Build` (`WorldRunner.cs:119`) drains+clears it into the
  frame as `utterances:[ {speaker, audience, channel, act, content:[{atomId,value}], confidence} ]`.
- **Atom name table:** a `/asset/atoms` endpoint mapping `atomType:int → name` (from `PlaceAtoms`/
  `PerceivableAtoms`), so the client (and later LLM) verbalise content.
- **town3d.html:** parse `msg.utterances` in `onSnap`, buffer to a time-windowed **log panel** (and/or
  speech bubbles over speakers); resolve atom ids via the name table.

- [ ] **Step 1: failing test** `Headless/Sim.MemoryTests/UtteranceWireTests.cs` — the buffer collects
  every tick's utterances across N ticks and drains fully (none dropped between publishes); AtomBag →
  flat `[{atomId,value}]` round-trips.
- [ ] **Step 2: run, verify FAIL.**
- [ ] **Step 3: implement** the buffer + drain into `WorldRunner.Build`; `/asset/atoms`; client log UI.
- [ ] **Step 4: run, verify PASS;** manual: open town3d, confirm utterances appear (danger shouts /
  gossip) in the log with resolved names.
- [ ] **Step 5: commit** `feat(comms): expose utterances on the town3d snapshot stream + atom name table`.

---

## Self-Review
Coverage: hearing sense (P1) → Inform second-hand + danger spread (P2) → Gossip + noise (P3) → client
exposure (P4). Each phase is independently green and additive; the payoff (danger by word of mouth)
lands in P2. Reuses the mapped machinery (PlaceObserve/Merge, MemoryPerceive, Interpret-trust, Social
grouping, ActivityCatalog, WorldRunner stream). Determinism preserved (float hearing in the existing
path; hash-based stochastic choices; one-tick latency). **Deferred** (each a router handler / content
kind on the same substrate): `Request`/`Offer`/`Express` in full (trade, quests, relations-reframe),
`Negotiate`, relay-chain rumour drift, deception/lying, posted notices, `Declare`, crime→bounty/justice,
non-speech noise→perception, and **LLM text rendering** at the client edge.
