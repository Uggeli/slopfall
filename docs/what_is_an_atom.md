# What Is an Atom?

> Companion to **What Is a Bunny?** (the entity), **What Is a World?** (the substrate),
> **What Is a Drive?** (motivation) and **What Is a Memory?** (recall). This one describes the
> **smallest unit** — the atom — and the **catalog of metadata** that turns a bag of atoms into a
> smart object. The bunny doc already fixes the *concepts* (atoms & molecules, affordance rules,
> subjective truth); this doc fixes the *engineering*: how an atom is named, what its type-level
> metadata holds, and how that replaces the current id-band scheme.

## The one idea

An atom is the **smallest unit of description** in the sim. A thing is a **bag of atoms** (a
*molecule*). Everything an agent can perceive, use, or react to is read through atoms.

The move this doc rests on: **affordances and reactions are not authored per thing — they emerge
from the atoms a thing carries.** Give a thing atoms and it becomes a smart object automatically.

> A `Stick` is `{Long, Wooden}`. `Long` affords use as a lever or a weapon; `Wooden` (⇒ `Flammable`)
> affords firewood. Nobody authored "stick." The same `Flammable` atom on a thatch roof makes *it*
> burn too. Atoms are the shared vocabulary; behaviour falls out of which atoms a thing has.

And the boundary, stated once so the rest of the doc can lean on it — **atoms describe what a thing
*is*, how it is *used*, and how it *reacts*. They do not carry:**

- **ownership** — *who* a thing belongs to is a **relation**, not a property (`OwnerId`, already a
  first-class concept; theft is the owner perceiving an unpermitted pickup — see the items design).
- **position** — *where* a thing is, is the **substrate** (a `Vector2`); `What Is a World?` says it
  outright: *Position is not an atom.*
- **instance-identity** — *which specific* entity this is, is the `EntityId` and its memory dossier.
  Atoms describe **kinds and properties** ("a civilian", "edible", "sharp"), never "entity #42."
- **the verdict** — *danger*, *value*, *fear* as the agent's final reading. The atom carries an innate
  affective **tone** (see *Perception*), but the verdict is the agent's (tone × disposition) plus what
  it has **learned** (cognition, in memory) — fixed nowhere on the atom.

---

## Named by an enum, not an id range

**The fix.** Today an atom's identity is an `int` packed into hand-allocated **bands** — `1000–1999`
entity kinds, `2000–2999` roles, `5000–5260` building kinds, `8000–8099` doors, and so on. The bands
are magic numbers spread across four files; the only registry is a comment; the salience seeds live
in a *separate* switch. The scheme already produced a silent collision (doors landed on the
`PlaceAtoms.Kind` base and had to be relocated), and nothing guards the next one.

**New model: one `AtomName` enum.** The name *is* the identity. The compiler enforces uniqueness;
there is no arithmetic, no band, no ceiling to collide with, and one obvious place to add an atom.

> **Identity atoms and property atoms are members of the same enum.** `Tavern`, `Civilian`, `Door`
> sit beside `Long`, `Wooden`, `Edible`, `Flammable`, `Liquid`. This is the deliberate choice (over
> "atoms are only a physical layer beside the identity registries"): one vocabulary, one catalog, one
> matching rule. Identity atoms stay so agents can **recognise and remember** what they saw; the
> **uses and reactions** come from the property atoms. (We start here and can let identity *emerge*
> from properties over time — see *Open questions*.)

**Type vs instance.** The enum value is the atom *type*. On a molecule an atom is a
**`(type, value)`** pair — the *instance*. Type-level facts (what `Flammable` means, what it affords,
how it reacts, its default reading) live in the **catalog** below; instance-level magnitude
(intensity, freshness, count) is the **value**. This is the bunny doc's open question 427 answered:
*signature/identity at the type, magnitude at the instance.* In code it is already
`Atom(AtomTypeId Type, Value)` — we are only replacing the `int` band with an `AtomName`.

---

## A molecule is a bag of atoms (unchanged)

Recapped from `What Is a World?`, because the storage does **not** change here:

- An entity **is** a bag of atoms; the molecule is its cross-section across the registries.
- The bag is the existing **`AtomBag`** — sorted, unique by type, binary-searchable.
- Atoms are a **read-interface over three backings** — a dedicated SoA component (hot, universal),
  a stored per-entity `Atom[]` bag (the cold descriptive tail), or a virtual atom derived from packed
  tile/field storage. The catalog renames and enriches the *type metadata*; it leaves the *backings*
  alone.

---

## The catalog — what each atom name carries

This is `What Is a World?`'s frozen **`AtomTypes`** table, enriched from "type → value domain" to
**four faces**. It is **one frozen catalog keyed by `AtomName`** — the single source of truth that
replaces three scattered things today: the band-map comment, `MemorySalience`'s by-band switch, and
`AffordanceCatalog.Public(BuildingKind)`'s building switch.

The faces split along the bunny doc's one hard line — **three are objective (shared, read-only); the
fourth is the atom's *affective tone*, which the agent's disposition turns into feeling** (it is *not*
learned meaning — that is cognition, in memory):

| # | Face | Kind | Holds |
|---|------|------|-------|
| 1 | **Descriptor / value domain** | objective | what the atom physically asserts + how to read its value: *presence* (have it or not), *graded scalar* (freshness 0.3), or *state* |
| 2 | **Affordance rules** | objective | the verbs a bearer affords — atom-pattern → verb, optionally gated by **co-requisite atoms** and the agent's own state |
| 3 | **Reaction rules** | objective *(engine deferred)* | the physics — pattern + field-threshold → transform (`Flammable` + heat → ignite; `Wet` resists) |
| 4 | **Affective tone** | affective (atom-owned) | a default *valence + arousal*; the agent's **personality + emotion** (its disposition) turn it into a felt reading — memory-free. Learned meaning is a *separate* faculty (cognition, in memory) |

Worked rows (illustrative):

| `AtomName` | domain | affords (pattern → verb) | reacts | affective tone (valence / arousal) |
|---|---|---|---|---|
| `Edible` | presence | `{Edible}` → Eat | — | + / med |
| `Flammable` | presence | `{Flammable}` → Kindle | + heat ≥ ignite → burns, emits heat | neutral / low |
| `Long` | presence | `{Long, Hard}` → wield as weapon; `{Long}` → lever | — | neutral / low |
| `Liquid` | presence | `{Liquid, Potable}` → Drink | + cold → freezes | neutral / low |
| `Tavern` | presence | `{SellsProvisions}` → Buy; `{SocialVenue}` → Socialize | — | + / med |
| `Coin` | graded | — | — | + / high *(amplified by greed)* |
| `Blood` | graded | — | — | − / **high** *(amplified by fear)* |
| `Danger` | graded | — | — | − / **high** |
| `Hunger` (somatic) | graded | — | — | − / rises with level |

> **Why affect lives on the atom but *meaning* does not.** The bunny doc is emphatic: *danger and fear
> were never in the atoms — the bunny puts them there.* So the atom cannot carry a *verdict*. What it
> carries is a **default affective tone** — an innate valence + arousal (`Blood` reads alarming, `Coin`
> appealing) — and the agent's **disposition** (personality + current emotion) decides what that tone
> *does*: an aggressive agent is *drawn* to a threat-tone where a timid one flees; fear amplifies it.
> Two bunnies, same tone, opposite behaviour — the divergence is the disposition, not the atom. This is
> **affect**, and it is **memory-free**: computed fresh every perception, never installed, never
> learned. It is *not* the same faculty as **cognition** — what the agent has *learned* the thing is
> (recognition, past harm), which lives in the memory system (`What Is a Memory?`) and is deliberately
> **not** specified here. An earlier draft conflated the two by installing this tone into `MEANINGS`;
> it does not — `MEANINGS` is cognition, the tone is affect.

---

## Affordances emerge — the smart-object engine

The bunny doc already states the rule (*Affordance rules: atom pattern → afforded verb*) and that the
match runs on the **subjective** molecule. This is how it becomes the engineering reality:

- An **affordance** = `(verb, precondition atom-pattern, [agent-state gate])`.
- **Gather** = match each affordance's pattern against the bearer's **perceived** atom bag; the
  matches become **ads**. A tavern advertises `Buy` because it carries `{SellsProvisions}`, **not**
  because it is labelled `Tavern`.
- **Co-requisite combinations** are first-class: `wield-as-weapon` requires `{Long, Hard}`, so a long
  feather does not qualify. This is the stick nuance — an atom's contribution can name the company it
  needs.

> This **replaces `AffordanceCatalog.Public(BuildingKind)`** — the candidate set stops coming from a
> `switch` on building kind and starts coming from atom-pattern matching. What it does **not** replace
> is `ActivityCatalog.Spec`: the verb's authored payload (duration, served-Δ, gates, preconditions)
> stays exactly as it is. Only *where the candidates come from* changes — from a kind-switch to the
> molecule. Selection among the afforded set is still the drives' job (`What Is a Drive?`), unchanged.

> **Atom-centric and pattern-centric are one relation, two indexes.** Authoring is natural per atom
> ("`Edible` enables Eat"); resolution is natural per pattern (match the bag). Pick the authoring home
> deliberately (see *Open questions*); the runtime is the match either way.

---

## Perception — how an atom becomes a feeling

A perceived atom is read by **two independent faculties**, and keeping them apart is the point:

- **Affect (disposition).** The atom's **affective tone** (face 4) × the agent's **personality** and
  **current emotion**. Innate, immediate, **memory-free**: "`Blood` is alarming (tone); I'm aggressive,
  so I close in rather than flee (personality); I'm already afraid, so it spikes (emotion)." This is
  what *the atom* contributes to feeling.
- **Cognition (recognition).** What the agent has **learned** the thing is and what it has meant before
  ("that's a fox; it chased me here last week"). This lives in the **memory system** (`What Is a
  Memory?`, currently being rewritten) — *not* in atom metadata. The atom layer names the seam and
  stops there.

The felt read is the two **combined**, then aggregated into the subjective molecule:

1. The **objective atom** carries the raw descriptor + physical metadata (channel, intensity, clarity,
   origin) and its affective **tone**. No verdict.
2. **Affect** turns tone into felt valence/arousal via disposition; **cognition** adds learned
   recognition and any learned charge. Together → a *subjective* atom.
3. The subjective atoms **aggregate** non-linearly — a prey agent lets one aversive atom *veto* the
   whole reading — into the **subjective molecule**, and **affordances match on that**, not the
   objective bag.

> This **generalises** the hard-coded special cases in today's `Interpret` — *creature → innate
> threat*, *beggar → stigma-scaled-by-warmth* — into **affective tone + disposition rules**, not `if`s.
> *Valence* therefore has two sources: an **innate** one (tone × disposition — the atom's contribution)
> and a **learned** one (cognition's charge — memory's contribution); the atom owns only the innate
> tone. **Arousal/salience** likewise feeds two consumers — affect's attention weight, and how strongly
> `Memory` encodes the atom (`What Is a Memory?`).

---

## Reactions — physics by atom (engine deferred)

Atoms carry the physics tags; **reactions** are `pattern + field-threshold → transform` rules over
atoms and fields, exactly as `What Is a World?` lays out — and **field-mediated**, so fire spreads
because heat diffuses through the temperature field and a `Flammable`+dry neighbour crosses its
ignition threshold, *not* because a "spread" rule was written.

> **The engine is deferred; the metadata is designed now.** The town sim has **zero** reaction/fire
> code today and no field layer to host it. Face 3 exists so that *physics is data* the moment the
> field substrate from `What Is a World?` lands — a new phenomenon (rot, rust, freezing) is a new
> *row*, not new code — without it being a present obligation.

---

## What an atom is **not** (boundaries restated)

| Not… | …because it is | Lives in |
|---|---|---|
| ownership | a relation | `OwnerId` (+ the owner's perception of a pickup) |
| position | the substrate | `Vector2` / the spatial index |
| instance-identity | a specific thing | `EntityId` + the memory dossier |
| the *verdict* & learned meaning | the agent's reading | affect (tone × disposition) + cognition (memory) — the atom carries only the tone |

---

## From the old model — migration map

| Today | Becomes |
|---|---|
| `PerceivableAtoms` / `PlaceAtoms` / `SomaticAtoms` / `DoorAtoms` band helpers | members of the `AtomName` enum |
| `MemorySalience.For(type)` switch | the atom's **salience/arousal** (one tone feeding affect-attention *and* memory's encode/decay strength) |
| `AffordanceCatalog.Public(BuildingKind)` + the item-gates | catalog **face 2**, matched against molecules |
| `AtomBag`, `MemoryRecord.deltaBag`, `AtomMeta(Strength, Flags)` | **unchanged** |

> **Two "metadatas," kept distinct.** The **catalog** is *per-type* (frozen, shared: what `Blood`
> means, affords, reacts, seeds). `AtomMeta(Strength, Flags)` is *per-instance, in a memory record*
> (how strong *this remembered* atom is, whether it is innate/surprise). The redesign touches the
> first and leaves the second alone.

**Value domain** stays the existing scalar (`presence` / graded) for now; the world doc's richer
tagged `AtomValue` (symbol · count · entityRef) remains **deferred** until a concrete need appears.

**Sequencing.** This lands **before** resuming the Phase-0 cognitive work. F4 ("install the innate
`MEANINGS` seed") is a **cognition** task — it seeds *recognition* prototypes for the `AtomName`s an
agent meets — so giving it a stable enum to name (instead of int-bands) **de-risks** it. The atom's
**affective tone** is a *separate* faculty (atom-owned, disposition-read) and is *not* what F4
installs; the two were conflated in the first draft.

---

## Open questions (flagged, not yet decided)

1. **Exclusive-state representation.** A door's `Open` / `Closed` / `Locked` — three distinct atoms
   (true to "smallest unit", but perception must enforce "exactly one of the set"), or one stateful
   atom with a small symbol value? Lean: separate atoms; revisit if the one-of-set rule gets noisy.
2. **Where affordance preconditions are authored.** A per-atom contribution list ("`Edible` enables
   Eat") vs. a standalone affordance table keyed by pattern. Two indexes of one relation; pick the
   authoring home in the plan.
3. **The identity slider (C → A).** How much identity stays an explicit atom vs. *emerges* from
   properties. We start with identity atoms kept (for recognition); we can retire them toward "a
   tavern is just `{SellsProvisions, SocialVenue, Shelter}`" as the property vocabulary fills in.
4. **Sensory-channel face.** Which sense detects an atom, at what range — deferred until a real
   multi-sense raw-sense layer exists (sensing is presence + line-of-sight today).
5. **(Resolved) Affect vs cognition.** The atom owns an **affective tone** (valence + arousal), read
   at perception and turned into feeling by personality + emotion — memory-free. **Cognition** (learned
   recognition / associations) is a separate faculty in the memory system, not atom metadata. The two
   are distinct; the tone is not a `MEANINGS` install.
6. **Reaction engine + its fields.** Timing and the field substrate (temperature/moisture) it needs;
   tied to the `What Is a World?` field layer, not the present town sim.
7. **When to introduce the tagged `AtomValue` union** (entityRef / count) — only on demonstrated need.
