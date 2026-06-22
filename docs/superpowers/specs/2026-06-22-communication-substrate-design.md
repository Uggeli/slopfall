# Agent Communication: One Substrate for Everything Agents Say (Design)

**Date:** 2026-06-22
**Status:** ready-for-review (fully code-mapped — social/memory/perception + sim→client transport;
ready for plan).
**Depends on:** agent memory (write/recall/consolidation), per-atom strength, PLACES (Phase 1+2),
perception (Sensed/SenseSystem), relations/affect.
**Part of:** the foundational social layer. Communication is the **substrate** under crime-reporting,
trading, quest/job handouts, gossip, rumour — *everything two agents do that isn't physical*.

## The idea

An agent acts on the world physically (move, eat, attack) **and** socially (tell, ask, offer, greet).
The social half is **one mechanism**: an agent emits an **utterance** — a typed **speech-act** carrying
an **atom-bag payload** — on a **channel** with some reach; agents who receive it **route it by act**
into a state update. Gossip, a crime report, a price haggle, a job offer, a warning shouted across the
square are all the *same* substrate with different *acts* and *content*. Build the substrate once;
every social behaviour becomes "a new content + a new handler on the router," never new plumbing.

The immediate motivator (continuing PLACES): **first-hand knowledge is sparse by design** — an agent
only learns danger it witnessed (~6/340 agents/3 days). Communication is the principled way knowledge
spreads: one witness *shouts* "danger at the mill!", neighbours hear it, and — because ODD already
scores ads on remembered danger (Phase 2) — they avoid it. Heard, not seen. That is the whole loop.

## Decisions (locked)

1. **Content is an `AtomBag`** — the same substrate as percepts, predictions, and memory records. An
   utterance about a place carries `PlaceAtoms`; about an entity, `PerceivableAtoms`; about an item,
   item atoms. This is what lets a heard fact flow **straight into memory** with no translation, and
   keeps communication vocabulary-compatible with the whole cognitive stack.
2. **A small, closed speech-act taxonomy** (Searle's families), the only thing that grows is content:
   - **Inform** (assertive) — "X is true": gossip, rumour, news, warnings, crime reports.
   - **Request** (directive) — "do/bring/tell me X": asking, hiring, quest handout.
   - **Offer** (commissive) — "I will give X for Y": trade, promises, deals.
   - **Express** (expressive) — "I feel X toward you": greet, thank, threaten, praise (relations).
   - *(Declare — institutional/authoritative "X is now so": law, ownership transfer — DEFERRED.)*
3. **Channels are HEARING-RADIUS tiers — none are private; eavesdropping is the point.**
   **Whisper** (smallest radius) < **Talk** (small) < **Shout** (largest). "Private" is only *a smaller
   radius* (fewer possible overhearers), never a guarantee — **any** agent within a channel's radius
   hears it, addressed or not. This is deliberate: a bystander who overhears "danger at the mill", a
   haggle, or "Bob stole the bread" can **correct its own rumour, act on the news, or report it** —
   emergent behaviour from leaked information. The speaker chooses a channel to trade reach against the
   chance of being overheard (whisper to a confidant; shout to alert the square). *(Deferred channels:
   written/posted notices — persistent, place-bound; relay-chains — rumour drift.)*
4. **Hearing is a new RAW SENSE feeding the perception system — the general noise channel.** Agents
   perceive by **sight only** today (`SenseSystem` → `SensedRegistry`, the raw-sight sense). Add a
   **`HearingSystem`** beside it: a raw sense that each tick computes what every agent **hears** — all
   noise within audible range — and feeds it into the **same perception pipeline** sight uses
   (attention → interpretation → memory/decision). This is bigger than communication: it **enables all
   noise-based behaviour** — eavesdropping on speech, hearing a brawl / scream / forge, danger-by-sound,
   stealth. An **utterance is just the highest-fidelity noise** (speech carrying an `AtomBag`); other
   actions emit lower-fidelity noise (a `NoiseLevel`, little/no content). Hearing is **LOS-relaxed**
   (sound rounds corners). `Audience` is *who the speaker aims at*; *hearers* are everyone in audible
   range (addressee **+ eavesdroppers**).
5. **Reception is one router keyed by act.** Inform → memory ingest; Request → an ad/intent for the
   receiver; Offer → an evaluation→accept/decline; Express → a relation update. The router is the
   single extension point; the substrate (utterance + reach + dispatch) never changes.
6. **Heard ≠ witnessed — relayed facts are SECOND-HAND.** An Inform's content enters the receiver's
   memory through the **existing encode path**, but at **reduced strength**, **source-attributed**, so
   hearsay never outweighs first-hand and a later contradiction can distrust the teller. First-hand
   danger (strength 255, SURPRISE) stays stronger than "Bob told me there's danger."
7. **Trust gates belief.** How much a heard fact is believed (its ingest strength) scales with the
   receiver's **regard for the speaker** (`Relations`/`Interpret` valence) × the speaker's stated
   **confidence**. You believe friends, discount strangers, distrust enemies. Belief is not binary.
8. **Truthfulness is content, not channel.** An agent relays its *own memory* — which may be a
   confident false memory, stale, or (later, when motivated) a deliberate lie. The substrate enforces
   no truth; rumour drift and deception are emergent, not special-cased.
9. **CQRS.** An utterance is an `IEvent`; a reception registry applies it (sole-writer into memory /
   relations / intents via existing intents). Uniform one-tick latency — you hear this tick what was
   said last tick.
10. **Utterances are OBSERVABLE, not just internal.** They are first-class records surfaced on the
   sim→client stream so the **town3d / client catches every communication** (who said what, to whom,
   on which channel, the content atoms). The client can show speech bubbles / a conversation log /
   social-graph overlays from the structured stream — the deep social state stops being invisible.
11. **Text lives at the render edge — the sim emits STRUCTURE, never prose.** Natural-language
    "rendering" of an utterance (an LLM mapping `{speaker identity, act, content atoms, relation}` → a
    line of dialogue) is a **client/render-side** concern, added later. The sim stays deterministic,
    headless, and text-free (the sim/render split); the same structured utterance can render as a
    speech bubble, a log line, or LLM prose without the sim changing. Determinism is never hostage to
    a model.
12. **Conversation is a sustained ACTIVITY, not only a one-shot utterance.** A lone warning-shout is a
    single `Utterance`; but **`Gossip`/`Negotiate`** are new **activities** — bound participants
    actively listening *and* talking, taking turns over a **duration**, each turn an utterance on the
    substrate. Duration is load-bearing: it is the speaker's **time commitment** (opportunity cost in
    ODD, competing with other drives) *and* the **window** in which eavesdroppers can overhear the
    exchange. Conversation builds on the existing co-location grouping (`SocialSystem` already groups
    co-located socialisers); the substrate is the same, the activity is the time-extended, two-way form.
13. **Noise level is a property of every action — it drives audibility.** Each `ActivitySpec` carries
    a **NoiseLevel** that sets the **audible radius** the hearing sense uses. This unifies "who hears
    speech" with "who hears any sound": Whisper/Talk/Shout are simply the speech noise tiers, while
    combat/forge are loud and sleep/sneak silent. The **eavesdrop radius of a conversation = its noise
    level**; a heated negotiation carries, a whispered plot does not. It generalises hearing to
    non-speech (a scream, a brawl, a hammer become perceptible) and is the future lever for
    stealth/loudness.

## Architecture

### The utterance

```
Utterance : IEvent {
    Speaker   : EntityId
    Audience  : EntityId   (who the speaker AIMS at; None = undirected. NOT a reach gate —
                            reception is the hearing radius; bystanders in range eavesdrop.)
    Channel   : Whisper | Talk | Shout   (hearing-radius tiers, smallest → largest)
    Act       : Inform | Request | Offer | Express
    Content   : AtomBag              (the payload — places/entities/items/events as atoms)
    Confidence: Fixed                (how sure the speaker claims to be — scales receiver belief)
    // Act-specific riders ride as atoms in Content (e.g. an Offer's price/goods atoms).
}
```

### The hearing raw-sense → perception (new), and its noise tiers

Perception is **sight-only** today: `SenseSystem` rebuilds `SensedRegistry.Of(agent)` every 5 ticks at
a **12-unit, LOS-blocked** radius (`SenseSystem.cs:23`); there is **no audio channel**. Add a
**`HearingSystem`** as a second raw sense feeding the same perception pipeline: each tick it gathers,
per agent, the **noises within audible range** — utterances and (later) any noisy action — and routes
them onward (attention → interpretation → memory/decision), exactly as sight-percepts flow. Who hears
an utterance = **every agent within its audible radius**, addressee or not — overhearers eavesdrop.
Hearing is **LOS-relaxed** (sound rounds corners) where sight is blocked. The audible radius comes
from the source's **noise level** (below); for speech that's the channel tier:

- **Whisper** — smallest radius (~conversational-adjacent); only those very close hear, so it's the
  *least* likely to leak (still not guaranteed private).
- **Talk** — small radius (a few units / room scale); nearby bystanders overhear. **Not private.**
- **Shout** — largest radius (~25u+, carries through); alerts the area, freely overheard.

Implementation: a hearing-reach helper does a coarse grid scan around the speaker at a radius derived
from the **source action's noise level** (see *Conversational activities & noise* — Whisper/Talk/Shout
are the speech noise tiers; the same helper later serves any noisy action). It uses the same bucket
structure `SenseSystem` uses, wider + LOS-relaxed, and returns the hearers. It is a **new sense path
beside sight**, so sight stays cheap and unchanged. Noise→radius mapping is species/config, tuned
later. (Indoor venue keepers/occupants are included when the speaker is inside, as `RequestSystem.cs:79`
already special-cases.)

**Audience vs. hearers:** the `Audience` is only *who the speaker aims at* (matters for directed acts —
a `Request`/`Offer` the addressee may answer). **All hearers receive the content** and may act on it;
only the addressee runs the directed-response path. Eavesdropping falls out for free: a bystander
hears the content, ingests it (second-hand), and its own ODD may then act — correct a rumour, avoid a
named danger, or (later) report what it overheard.

### Reception router — act → state update (code-mapped)

A `CommunicationSystem` consumes `Utterance` events, runs each through the hearing-reach gate, and for
**every hearer** dispatches by act onto the **existing** intent pipelines (sole-writer discipline
preserved). **All hearers ingest the content** (the eavesdrop path — they learn what was said); for
directed acts (`Request`/`Offer`) the **addressee additionally** runs the response (answer/accept),
while bystanders just learn it happened.

- **Inform → second-hand memory.**
  - *Place facts* (`PlaceAtoms` in Content) → emit `PlaceObserveIntent` for the hearer — the same hook
    `PlaceDangerSystem` uses (`AgentMemoryRegistry.cs:52`). **But relayed facts must land weaker:**
    `MergePlaceAtom` currently takes strength from `MemorySalience` (Danger → SURPRISE/255 regardless
    of value), so a hearsay danger would wrongly equal a witnessed one. Fix: `PlaceObserveIntent`
    gains a **`SecondHand`/trust-scale** rider; `MergePlaceAtom`, when relayed, caps the atom to a
    **reduced strength (salience × trust×confidence) and drops the SURPRISE/INNATE flag** — so heard
    danger is real but fades unless re-heard or witnessed, and first-hand always dominates. Then ODD's
    Phase-2 `PlaceAversion` reads it and the hearer avoids the place.
  - *Entity/episode facts* → emit `MemoryPerceiveIntent` (`AgentMemoryRegistry.cs:38`) with **low
    arousal** so the encoder writes a shallow, fade-able trace (the encoder gates on surprise OR
    arousal; low arousal → low per-atom strength). Source-attributed (see below).
- **Request → an ad/intent.** Generalises `RequestSystem` (begging = "ask for alms", with its
  `HelpGranted`/`HelpRefused` + per-pair cooldown at `RequestSystem.cs:131`). The receiver gains a
  candidate action; outcome events fold back through the affect pipeline.
- **Offer → evaluate → accept/decline.** On accept, `CoinTransferEvent` (`SimEvents.cs:54`) +
  `ItemRegistry` transfer. (Trade is a follow-on handler.)
- **Express → relation update.** Routes through the existing **`RelationImpulseEvent` →
  `AffectsSystem` → `RelationsSetIntent`** path (`SimEvents.cs:49`, `SocialSystem`) — exactly how
  greetings/gratitude already move regard. SocialSystem's `GreetingEvent`/gossip-fold IS proto-Express.

**Source attribution:** memory has no `Source` field today. Add an optional `Source` (speaker) to the
ingest intents so a hearer records *who told it* — enabling later distrust of a proven liar
(reconsolidation already weakens contradicted memories) and rumour provenance. Minimal: a flag now,
full source-tracking when deception lands.

### Reframing what already exists (not building beside it)

The substrate **subsumes** today's ad-hoc social code rather than duplicating it:

| Existing | Becomes |
|---|---|
| `SocialSystem` gossip (teller→listener folds opinion-about-a-person, `SocialSystem.cs:323`) | the **Inform** act, generalised from people-only to any `AtomBag` (places, items, events) on real channels |
| `SocialSystem` greeting + `RelationImpulseEvent` | the **Express** act |
| `RequestSystem` begging | the **Request** act (one content kind among many) |

This is the migration story: implement the substrate, then move these onto it as router handlers.

### Speaking — what makes an agent talk

Speaking is an **ODD ad** (`Inform`/`Request`/`Offer`/`Express`, each with a value), so *deciding to
talk* competes with other drives in the existing decision loop — a SocialDef-driven agent gossips, a
frightened witness **shouts** a danger warning (fear-driven `Inform`), a merchant offers. Today
`SocialSystem` fires gossip on a cadence and `RequestSystem` on the `Beg` activity; Phase 1 wires a
minimal speaker (a witness shouting danger) and the spread loop, with full speaking-as-ads as the
acts generalise. Content is drawn from the speaker's **own memory** — it relays what it knows.

### Conversational activities & noise level

Integration: `ActivityCatalog.Spec` (`Assets/Sim/Systems/ActivityCatalog.cs:10`) is the per-activity
spec (has `DurationMinutes`, `Delta[]`, `Social`, gates…); `SpecFor(ActivityKind)` (`:422`) is the
lookup. Add a **`NoiseLevel`** field there, and new `ActivityKind` values **`Gossip`/`Negotiate`** with
`SpecFor` entries (`Social=true`, a `DurationMinutes`, a mid `NoiseLevel`). Two layers of "talking":

- **One-shot utterance** — a single `Utterance` (shout a warning, hail a passer-by). No binding, no
  duration; emitted and gone.
- **Conversation activity** — new `ActivityKind`s **`Gossip`** and **`Negotiate`**: the agent *enters*
  the activity (a `BehaviorData` state) for a `DurationMinutes`, **bound** with one or more partners,
  and over that span both **listen and talk** — each turn an `Utterance` (Talk channel) whose content
  is drawn from memory (Gossip = share/compare known facts; Negotiate = `Offer`/counter toward a deal).
  Binding reuses `SocialSystem`'s co-location grouping; the conversation is just the substrate run in a
  turn-taking loop for a duration. Bystanders within the noise radius hear every turn (eavesdrop).

**`ActivitySpec.NoiseLevel`** (new field on every spec) is the single audibility knob:

- The hearing-reach helper takes the **source action's noise level**, not a fixed per-channel radius —
  `audibleRadius = f(NoiseLevel)`. Whisper/Talk/Shout are the **speech** noise tiers (low/mid/high);
  Gossip ≈ Talk, a heated Negotiate louder, a warning Shout highest. Non-speech actions get noise too
  (combat loud, forge loud, sleep silent), so the same hearing sense will later surface a brawl or a
  scream — communication and ambient sound share one mechanism.
- Duration × noise define the **eavesdrop window**: a long, loud conversation leaks far and for a
  while; a short whisper barely at all.

### Client exposure & LLM text rendering (the render edge)

The same `Utterance` events that drive reception are **collected per tick and surfaced on the
sim→client stream** as structured records — so town3d catches every communication:

```
UtteranceRecord (wire) {
    tick, speaker, audience, channel, act,
    content : [ {atomType:int, value} ]      // raw atoms — the structured meaning
    confidence
}
```

- **The client renders structure, not prose (now):** speech bubbles over speakers, a scrolling
  conversation/event log, a who-talks-to-whom overlay — all drawable from the records. The deep social
  state stops being invisible.
- **Transport (code-mapped — NOT blocked):** town3d (`Headless/Sim.Web/wwwroot/town3d.html`) is a
  Three.js spectator consuming a JSON snapshot pump over WebSocket `/ws` (`Program.cs:341`,
  `WorldRunner.Build` `WorldRunner.cs:119`). `Sim.Net`/`RenderSnapshot` is dead legacy, *not* the live
  path. Today the client gets **state only — no event channel exists**, so the utterance stream is new.
  Attach it as a `utterances[]` field on the snapshot frame.
  - **Accumulate, don't sample:** the pump publishes at **~5 Hz while the sim ticks far faster**, and
    the EventBus holds only the current tick's events — so a sim-side **utterance buffer** must collect
    every tick's utterances and `WorldRunner` drains+clears it into each frame. Reading `GetEvents`
    once per publish would drop almost everything. (Region mode already filters entities by camera
    ring; utterances can filter the same way later — send all for town mode first.)
- **Atom→word table:** the client (and the later LLM) needs `atomType:int → concept name` to verbalise
  content. The atom catalogs (`PlaceAtoms`, `PerceivableAtoms`, …) are the source; expose a name table
  on the stream so `Danger@building#5` reads as "danger at the mill."
- **LLM text rendering (later, render-edge only):** a service maps `{speaker persona/identity, act,
  content atoms, regard-to-audience} → a line of dialogue` ("Did you hear? Someone was killed by the
  mill — stay clear!"). It consumes the structured record; the **sim never produces text** and stays
  deterministic. The same record can become a bubble, a log line, or LLM prose interchangeably.

## How the use cases map (the foundational proof)

| Behaviour | Act | Content | Reception |
|---|---|---|---|
| **Gossip / rumour** | Inform | a fact from the speaker's memory | second-hand memory (trust-scaled); relay-chains drift (deferred) |
| **Danger spread** (the payoff) | Inform (Shout) | `PlaceAtoms.Danger@building` | hearer's PLACES gains DangerHere → ODD avoids (Phase 2 already reads it) |
| **Report crime** | Inform (Talk, directed→guard) | the witnessed crime as event atoms | guard's memory + a bounty/justice intent (deferred) |
| **Trade** | Offer→Accept | goods + price atoms | evaluate vs coin/need; on accept, item+coin transfer |
| **Quest / job handout** | Request (directed) + reward | task atoms + reward | receiver gains an ad to do it; completion → reward |
| **Greet / threaten / praise** | Express | affect atoms | regard update (SocialSystem reframed) |
| **Eavesdrop** (cross-cutting) | any | whatever was overheard | a non-addressed hearer in radius ingests the content → corrects a rumour, avoids a named danger, or (later) reports it |

Every row is the *same* utterance + router; only the act and the atoms differ — and **eavesdropping is
not a feature, it's a consequence**: reception is by hearing radius, so anyone in range receives any
row above. That is what "foundational" means here.

## Data flow

```
decide   -> speaking/conversing is an ODD ad (one-shot Utterance, or enter a Gossip/Negotiate activity)
speak    -> emit Utterance{speaker, audience, channel, act, content, confidence}  (per turn)
hear     -> HearingSystem (raw sense): noises within audible radius (= noise level) -> perception;
            every agent in range receives it, addressee + eavesdroppers
receive  -> router by act:
              Inform  -> second-hand memory ingest (trust×confidence strength, source-tagged)
              Request -> ad/intent for the receiver
              Offer   -> evaluate -> Accept (transfer) | decline
              Express -> relation update
act-on   -> ODD reads the now-richer memory (e.g. avoids heard-of danger — Phase 2 loop closes)
export   -> the tick's utterances are serialized onto the sim->client stream (structured records)
client   -> town3d shows bubbles/log/overlays now; an LLM renders records -> prose later (edge-only)
```

## Validation

- **Unit:** an `Inform` carrying `PlaceAtoms.Danger@b` makes the receiver's PLACES store gain
  `DangerHere@b` at a **lower** strength than first-hand witnessing, scaled by regard for the speaker;
  zero/negative regard → little/no belief. Source attribution present.
- **Integration/soak (the payoff the sparse-danger finding called for):** a witnessed danger that gets
  shouted spreads to hearers — danger-memory count rises **beyond** the first-hand witness set, and
  population danger-avoidance becomes visible in the soak (vs Phase-2 baseline where it stayed at
  ~6/340). Confirm no economy/stability regression and determinism (serial == parallel).
- **Eavesdrop:** a non-addressed agent **within the channel's radius** receives an utterance's content
  (and one **outside** the radius does not); a closer channel (whisper) reaches fewer bystanders than a
  wider one (shout). Proves reception is by hearing radius, not by `Audience`.
- **Client exposure:** the tick's utterances appear in the snapshot `utterances[]` (the buffer drains
  fully — none dropped between 5 Hz publishes); town3d shows them as a log/bubbles. Atom name table
  resolves content ids to concepts.

## Scope / deferred

- **In (foundational layer):** the `Utterance` model (atom-bag content + act + channel), the new
  **hearing sense** with **`ActivitySpec.NoiseLevel`-driven** reach (whisper/talk/shout = speech noise
  tiers, LOS-relaxed, eavesdroppable), the reception **router skeleton** (all hearers ingest; addressee
  answers directed acts), a basic **`Gossip` conversation activity** (bound partners trade `Inform`
  turns over a duration) plus the one-shot warning shout, the **Inform act → second-hand memory** path with trust-scaling + source attribution — proven end-to-end by **danger
  spread** (shout → hear → avoid) — plus the **structured utterance stream to town3d** so the client
  catches communications (bubbles/log from the records). Speaking-as-an-ad so the decision loop can
  choose to talk. *(Client exposure is buildable now — the WebSocket pump is healthy; a sim-side
  utterance buffer drained into the `utterances[]` snapshot field, plus a minimal town3d log/bubble —
  no transport rebuild.)*
- **Deferred (each a handler on the router or a content kind):** Request/Offer/Express acts in full
  (trade, quests/jobs, expressive relations — reframing SocialSystem/RequestSystem onto Express/
  Request); relay-chain **rumour drift** (fidelity/confidence loss per hop); **deception/lying**
  (motivated false content); written/**posted** notices (persistent, place-bound); **Declare**
  (law/ownership/authority); language/faction barriers to comprehension; the crime→**bounty/justice**
  consumer of directed crime-reports; **LLM text rendering** of utterances at the client/render edge
  (the structured stream is designed for it now; the model comes later); the **`Negotiate` activity**
  (needs the Offer act); **non-speech noise → perception** (combat/scream/forge emitting `NoiseLevel`
  the hearing sense surfaces — same mechanism, broader content); richer **turn-taking** protocol; and
  **stealth/loudness** modifiers on noise.
