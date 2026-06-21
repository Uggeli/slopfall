# What Is a Bunny?

## Core Behaviors

### Survival
- **Eating** — herbivores; graze on hay, grass, leafy greens, pellets
- **Drinking** — regular water intake, especially after feeding
- **Breeding** — highly prolific; does can birth multiple litters per year
- **Cecotrophy** — eating soft cecotropes (nutrient-rich droppings) directly from their bottom; essential for nutrition
- **Foraging** — searching and selecting food, even in domestic settings

### Communication
- **Thumping** — stomping hind legs to signal danger or displeasure
- **Tooth grinding (bruxism)** — soft grinding = contentment; loud grinding = pain
- **Grunting / honking** — annoyance, excitement, or mating interest
- **Screaming** — extreme fear or pain (rare)
- **Nose twitching** — constant; sampling air for scent information

### Mood & Emotion
- **Binkying** — leaping and twisting mid-air; a sign of pure joy
- **Flopping** — dramatically dropping on their side; means very relaxed and safe
- **Circling** — running loops around a person or object; excitement or mating behavior
- **Boxing** — swatting with front paws; asserting boundaries or playing
- **Freezing** — going completely still when startled or sensing threat

### Territorial & Social
- **Chin rubbing** — marking objects with scent glands under the chin
- **Urine spraying** — territorial marking, especially in unaltered rabbits
- **Grooming (allogrooming)** — licking other rabbits (or humans) to bond and show affection
- **Dominance behavior** — mounting, chasing, nudging to establish hierarchy
- **Periscoping** — standing on hind legs to survey surroundings

### Physical Maintenance
- **Self-grooming** — frequent washing of face, ears, and body like a cat
- **Digging / burrowing** — instinct to excavate; done on any soft surface
- **Chewing** — constant gnawing to wear down continuously growing teeth
- **Molting** — seasonal heavy shedding of fur
- **Sprinting & zoomies** — bursts of high-speed running to burn energy

### Rest
- **Sleeping** — in short cycles, often with eyes open; most active at dawn and dusk (crepuscular)
- **Loafing** — sitting tucked in a round loaf shape; calm but alert

---

## Actions vs. Activities

A model for decomposing the behaviors above into composable units.

- **Action** — atomic. One motion or one sensory/motor primitive. Not meaningfully divisible (a *bite*, a *step*, a *sniff*). Indivisible, instantaneous-ish, no internal goal of its own.
- **Reflex** — an action (or fixed pattern) fired **involuntarily** by a stimulus. Stimulus → action, with **no goal** and no decision. Sits beside actions, not above them (e.g. *screaming* in pain, startle-*freeze*).
- **Activity** — a set of actions run toward a **goal** under **rules** (ordering, conditions, stop criteria). Has state (start, in-progress, done, interrupted).
- **Chaining** — activities compose into larger routines the same way actions compose into activities. The hierarchy is recursive: today's activity is tomorrow's action when zoomed out.

### Atomic Actions (the primitives)

| Action | What it is |
|--------|-----------|
| `sniff` | one nose-twitch sample of the air |
| `step` / `hop` | one unit of locomotion |
| `stride` | one sprint-speed bound |
| `turn` / `pivot` | change facing |
| `rear-up` | rise onto hind legs |
| `leap` | push off into the air |
| `twist` | mid-air body rotation |
| `flop` | drop onto side |
| `freeze` | hold all motion |
| `bite` / `nibble` | one jaw close on food/object |
| `chew` | one grinding jaw cycle |
| `swallow` | one ingestion |
| `lick` | one tongue stroke |
| `lap` | one tongue scoop of water |
| `dig-scratch` | one paw rake of the ground |
| `chin-rub` | one scent-gland swipe on a surface |
| `spray` | one urine mark |
| `paw-swat` | one boxing strike |
| `mount` | clasp partner/rival |
| `nudge` | one push with the nose |
| `thump` | one hind-leg stomp |
| `grind` | one tooth-grind (brux) |
| `vocalize` | one grunt / honk |

### Reflexes (involuntary, no goal)

| Reflex | Stimulus → response |
|--------|---------------------|
| **Scream** | sudden pain / terror → single involuntary `vocalize` (a cry, not a call for help) |
| **Startle-freeze** | abrupt motion/sound → instant `freeze` before any decision |

### Activities (actions + goal + rules)

| Activity | Goal | Component actions | Rules / stop condition |
|----------|------|-------------------|------------------------|
| **Grazing / Eating** | nutrition | `sniff → bite → chew × n → swallow` (loop) | continue while food present & not full; stop when sated or startled |
| **Drinking** | hydration | approach → `lap × n → swallow` | usually follows eating; stop when no longer thirsty |
| **Foraging** | find food | `sniff → step → sniff → select` (loop) | move toward strongest food scent; ends by entering Grazing |
| **Cecotrophy** | re-digest nutrients | curl → `bite` (from anus) → `chew → swallow` | triggered by cecotrope availability, often during rest |
| **Self-grooming** | hygiene | `lick` paws → wipe face → `lick` body | runs head-to-tail; interruptible |
| **Allogrooming** | bond / status | `lick` partner (repeat) | reciprocal; subordinate often grooms dominant |
| **Digging / Burrowing** | shelter, escape | `dig-scratch × n` + clear soil | continue until cavity sufficient; instinctive on soft surfaces |
| **Periscoping** | survey | `rear-up → sniff → scan` | hold while assessing; drops on resolution |
| **Boxing** | assert boundary | `rear-up → paw-swat × n` | escalates if rival doesn't yield |
| **Chin-marking** | claim territory | move-to-object → `chin-rub` | sweep perimeter objects |
| **Circling** | court / signal excitement | `stride + turn` loops around a target | tight loops around a mate or person; may hum; precedes courtship |
| **Nursing** | feed kits | position over nest → kits latch → milk letdown | doe nurses very briefly (~once or twice a day, a few minutes); then leaves the nest |
| **Binkying** | express joy | `stride → leap → twist → land` | only when safe & energized; spontaneous |
| **Zoomies** | burn energy | `stride` loops + `turn` | bursts; self-terminating |
| **Loafing** | rest, stay alert | tuck → hold, `sniff` periodically | low-alert resting state |
| **Sleeping** | recover | flop/loaf → rest cycles | crepuscular; light, easily broken |
| **Alarm signaling** | warn / deter | `freeze → thump × n` | repeats while threat perceived |

### Chained Activities (routines)

Activities chain into higher-order behaviors, with the same goal+rules logic one level up:

- **Crepuscular cycle** — `wake → Periscoping → Foraging → Grazing → Drinking → Self-grooming → Binkying → Loafing → Sleeping`
- **Predator response** — `freeze → Alarm signaling → Zoomies (to burrow) → Digging (deeper) → Loafing (hidden)`
- **Courtship** — `Circling → chasing → mount → (mate)`
- **Territory establishment** — `Periscoping → Chin-marking (perimeter) → spray → Alarm signaling (at intruders)`
- **Feeding bout** — `Foraging → Grazing → Drinking → Cecotrophy (later) → Self-grooming`

> Note the recursion: in *Predator response*, "Zoomies" is one step — an action from the routine's point of view — yet it is itself an activity of `stride`/`turn` actions. Same composition rule at every level.

---

## Physical & Mental Things

Actions and activities are *verbs*. They operate on **things** — *nouns*. Every action/activity uses some physical machinery and reads/writes some mental state. Mapping the things tells us what the behaviors actually touch.

### Physical Things

Split into **body parts** (the bunny's own machinery — effectors that do, sensors that detect) and **world objects** (things in the environment the behavior acts on).

**Body parts**

| Part | Role | Used by |
|------|------|---------|
| Nose | sensor (smell) + effector (nudge) | `sniff`, `nudge` |
| Ears | sensor (hearing), large & rotating | threat detection, alertness |
| Eyes | sensor (sight, wide-angle motion) | scan, `freeze` triggers |
| Whiskers | sensor (touch, close-range) | navigation in burrow / dark |
| Teeth (incisors) | effector, ever-growing | `bite`, `nibble`, `grind` |
| Jaw + molars | effector (grinding) | `chew` |
| Tongue | effector | `lick`, `lap` |
| Throat | effector | `swallow` |
| Front paws | effector (fine) | `dig-scratch`, `paw-swat`, `mount`, grooming |
| Hind legs | effector (power) | `step`, `hop`, `stride`, `leap`, `thump` |
| Body/spine | effector (whole-body) | `turn`, `twist`, `flop`, `freeze`, periscope |
| Chin scent gland | effector (marking) | `chin-rub` |
| Genitourinary | effector | `spray`, `mount` |
| Gut + cecum | organ (digestion, fermentation) | produces cecotropes, satiety |
| Mammary glands | organ (lactation) | `Nursing` |
| Fur | covering (insulation, signal) | molting, flop comfort |

**World objects**

| Object | What it is | Used by |
|--------|-----------|---------|
| Food | hay, grass, greens, pellets | Foraging, Grazing |
| Water | drinking source | Drinking |
| Soil / ground | dig medium | Digging |
| Burrow | shelter, escape goal | Predator response, Sleeping |
| Nest | lined chamber for kits | Nursing |
| Cecotropes | re-ingestible droppings | Cecotrophy |
| Conspecific | mate / rival / bonded partner | Courtship, Boxing, Allogrooming |
| Kits | offspring | Nursing |
| Territory surfaces | objects to mark | Chin-marking, spray |
| Scent marks | deposited claims (read & written) | marking, recognition |
| Air | scent/sound carrier | `sniff`, hearing |

### Mental Things

Split into **drives** (start behaviors), **percepts** (sensory inputs read), **affects** (emotional states, usually expressed), and **knowledge** (stored info).

| Kind | Things |
|------|--------|
| **Personality** (stable traits) | bold ↔ timid, curious ↔ neurotic, social ↔ solitary — lifelong weights, slowest to change |
| **Drives / needs** | hunger, thirst, libido, energy ↔ fatigue, safety/fear, social/bonding — *comfort* is not on the list: it's the derived null-state when no drive presses (see *What Is a Drive?*) |
| **Percepts** (read) | scent, sound, sight/motion, touch (whiskers), taste, balance/proprioception |
| **Affects** (expressed) | joy, contentment, fear/alarm, annoyance, excitement, curiosity, trust |
| **Knowledge** (stored) | spatial map (food + burrow locations), territory boundaries, social rank, recognition (mate / owner / threat), satiety level, alertness level |

> Ordered by timescale: **Personality** (lifelong) → **Knowledge** (learned) → **Drives** (slow shifts) → **Percepts** (instant). **Affects have no rung of their own** — they're rebuilt every tick from the drives' residuals; a *mood* lingers only because the unresolved drive does. The slower layers weight how the faster ones are read — this is the machinery the subjective-truth layer below runs on.

### Worked Mappings (verb → things it touches)

Each behavior reads (`←`) and writes (`→`) things:

- **Grazing** — *body:* nose, teeth, jaw, tongue, throat, gut · *world:* food · *mental:* `←` hunger, scent · `→` satiety (lowers hunger), energy
- **Alarm signaling** — *body:* ears, eyes, hind legs · *world:* air (sound), conspecifics (receivers) · *mental:* `←` sound/sight, fear · `→` alertness, others' fear
- **Nursing** — *body:* mammary glands, body · *world:* nest, kits · *mental:* `←` reproductive drive, recognition (own kits) · `→` (kits' hunger lowered)
- **Chin-marking** — *body:* chin gland · *world:* territory surfaces, scent marks · *mental:* `←` territory map · `→` scent mark, territory boundary
- **Binkying** — *body:* hind legs, body · *world:* (open safe space) · *mental:* `←` joy, energy, safety · `→` energy spent
- **Cecotrophy** — *body:* teeth, gut/cecum, body (curl) · *world:* cecotropes · *mental:* `←` (digestive cue) · `→` nutrients absorbed

---

## Atoms & Molecules

The same composition pattern, now on the **noun** side. Where actions/activities are *verbs* (things that happen), atoms/molecules are *descriptions* (how physical reality is).

- **Atom** — one indivisible description of physical reality. A single property bound to a value: *is orange*, *weighs 60 g*, *is at (3,4)*, *is wet*. Not further divisible.
- **Molecule** — a set of atoms describing one thing, held together by **relations** (part-of, adjacent-to, contains). A *carrot* is the bundle {orange, tapered, crunchy, 60 g, …}.
- **Chaining** — molecules compose into bigger molecules (a nest contains soil + lining + kits) exactly as atoms compose into molecules. Recursive, same as activities.

| Verb side | Noun side |
|-----------|-----------|
| Action (atomic deed) | **Atom** (atomic description) |
| Activity (actions + goal + rules) | **Molecule** (atoms + relations) |
| Chained activities (routines) | Chained molecules (scenes / structures) |

### Atom Types (the primitive descriptors)

| Group | Atoms |
|-------|-------|
| **Spatial** | position, orientation/facing, distance-to |
| **Form** | size, shape, material/substance, mass, density |
| **Surface** | texture, hardness, color, brightness |
| **Condition** | temperature, moisture, freshness/age, integrity (intact↔broken), phase (solid/liquid/gas) |
| **Quantity** | count, amount/volume |
| **Motion** | velocity, direction-of-travel |
| **Chemical** | scent, taste |

> An atom *type* is the property (`color`); an atom *instance* is the value-bound description (`color = orange`) — the same type/instance split as `bite` (the action) vs. a specific bite.

### Molecules (atoms + relations → a described thing)

The physical things from the previous section **are molecules**. Each world object and body part is a bundle of atoms:

| Molecule | Atoms (sample) |
|----------|----------------|
| **Carrot** (food) | shape: tapered · color: orange · material: plant-fiber · texture: crunchy · mass: 60 g · moisture: high · taste: sweet · count: 1 |
| **Water** (puddle) | phase: liquid · color: clear · temperature: cool · amount: 200 ml · position: … |
| **Soil patch** | material: earth · hardness: soft · moisture: damp · texture: loose |
| **Scent mark** | scent: self · freshness: new · position: (on object) |
| **Hind leg** (body part) | shape: limb · material: muscle/bone · strength: high · position: (under body) |

### Chained Molecules (composites / scenes)

Molecules nest into larger molecules via relations:

- **Nest** = soil-chamber molecule *contains* lining (fur/hay) molecule *contains* kits molecules
- **Burrow** = tunnel molecules *connect* chamber molecules → a spatial structure
- **Conspecific** = a whole rabbit = composite of body-part molecules (the bunny itself is one big molecule)
- **Meadow** (scene) = many food + soil + water molecules arranged in space

> Same recursion as *Predator response* on the verb side: a "kit" is one atom-bundle from the nest's point of view, yet itself a full composite molecule. Composition rule holds at every level.

### Where the two sides meet

Verbs operate on nouns: **actions/activities read and write the atoms of molecules.** This is the whole engine.

- `bite` — reads *carrot* {hardness, position} → writes {count −1, mass ↓, integrity: broken}
- **Grazing** — decrements *food* molecule {mass, count} → increments *gut* molecule {fullness}
- `chin-rub` — writes a new *scent mark* molecule onto a *territory-surface* molecule
- **Digging** — rewrites *soil* {integrity, shape} → produces a new *burrow* molecule
- **Nursing** — reads *kits* {hunger atom} → writes {hunger ↓}

One activity's written atom is another activity's read atom — the atoms are the shared state the whole behavior graph runs on.

---

## Affordances — molecules reveal what can be done

This is what the noun side is *for*. A molecule's atoms tell the bunny which verbs are possible **to / at / with** that entity, or **in** the world around it. You don't ask "what can I do?" in the abstract — you read a thing's atoms and they *afford* a set of actions and activities.

- **Precondition** — each action/activity declares an atom-pattern it requires (on the target, on an instrument, on the locus, and on the bunny's own self-molecule).
- **Affordance** — when a molecule's atoms **match** a precondition, that verb becomes possible on/with/in that molecule. Match = afforded.
- **Discovery** — perceive an entity, read its atoms, test them against the precondition library; the matches are the verbs available right now.

### How the entity participates (the prepositions)

| Relation | Entity's role | Example |
|----------|---------------|---------|
| **to / at** (target) | the verb acts *on* it | `bite` the carrot · `thump` at a threat · `chin-rub` the post · `mount` the rival |
| **with** (instrument) | used as means/material | line the nest *with* fur · push *with* the nose |
| **in / on** (locus) | space that enables it | `binky` *in* open ground · `dig` *in* soft earth · sleep *in* the burrow |

### Affordance rules (atom pattern → afforded verb)

| If a molecule's atoms include… | …it affords | role |
|-------------------------------|-------------|------|
| `phase: liquid` | Drinking (`lap`) | to |
| `material: plant-fiber`, `taste: sweet/green` | Grazing (`bite`, `chew`) | to |
| `material: earth`, `hardness: soft` | Digging (`dig-scratch`) | in |
| `shape: surface`, reachable | Chin-marking (`chin-rub`) | at |
| `conspecific`, `scent: unfamiliar`, `motion: approaching` | Boxing / Alarm / flee | at |
| `conspecific`, `scent: mate` | Courtship (Circling) | with |
| `conspecific`, bonded | Allogrooming | with |
| `kits present`, in nest | Nursing | to |
| `cecotrope: available` | Cecotrophy | to |
| `space: open`, `safety: high` | Binkying, Zoomies | in |

### The discovery loop (where everything connects)

```
perceive entity
      │
      ▼
read its MOLECULE (atoms)        ◄── nouns
      │
      ▼
match atoms vs. PRECONDITIONS
      │
      ▼
AFFORDANCES = verbs this entity allows   (the "CAN")
      │
      ▼
DRIVE / AFFECT selects one               (the "DO")  ◄── mental things
      │
      ▼
run ACTION / ACTIVITY            ◄── verbs
      │
      ▼
writes ATOMS  →  world changes  →  new affordances appear
```

> **CAN vs. DO.** Affordance is object-side: water affords Drinking whether or not the bunny is thirsty. Selection is subject-side: the **thirst** drive (a mental thing) picks Drinking out of the afforded set. Atoms say what's *possible*; drives say what's *chosen*. The loop closes because executed verbs rewrite atoms, which changes what's afforded next — a meadow with a fresh-dug burrow now affords *hiding* that it didn't a minute ago. *(The next section subjectivises this: affordances actually match against the **perceived** molecule, not the objective one.)*

---

## Subjective Truth — how the agent interprets atoms & molecules

Subjective truth is nothing more than **the agent's interpretation of the objective atoms and molecules.** The atoms don't change; the reading does. Everything above is objective ground truth, shared by every observer — but a bunny never acts on it directly, only on what it makes of it. Two bunnies face the same fox: one flees, one keeps grazing. Nothing in the objective atoms differs. **Danger and fear were never in the atoms** — the bunny puts them there.

Three parts, and the split is the whole point:

1. **Atom + metadata — read-only, objective, shared.** The atom is the raw fact (`scent = fox`); its metadata are raw *physical* descriptors of the signal — which sense, how strong, how clear, from where. **No meaning lives here.** The fox-scent atom carries no "danger"; danger is not a property of a scent.
2. **Interpretation — the agent's, private.** How an agent reads that metadata is entirely up to the agent. A fox smells its own kind and shrugs; a bunny smells the *same read-only atom* and reads *predator*. Same atom, opposite meaning. This is where personality, state, and memory enter — and where everything subjective is produced.
3. **Subjective molecule** — the aggregate of the agent's interpreted atoms (non-linearly; see below). This, not the objective molecule, is what affordances match against.

```
       ┌─ external senses ─┐
       │                   │   read the OBJECTIVE atom + metadata
       ├─ memory ──────────┤   (read-only, raw, carries no meaning)
       └─ social signals ──┘
                │
                ▼   INTERPRETED by the agent …
   personality · physical state · emotional state · memory   ◄── private, per-bunny
                │
                ▼
        subjective atom  →  aggregate (non-linear)  →  subjective MOLECULE
                │
                ▼
        affordances match on the SUBJECTIVE molecule
```

### Atom metadata — raw physical descriptors only (read-only)

No semantic tags. Just the physical character of the signal — everything an instrument could measure without knowing what it *means*.

| Metadata | What it is |
|----------|-----------|
| **signature** | the raw identity of the signal — this *is* fox-scent (an objective chemical fact). Recognising it *as* "fox" is the agent's job, not the atom's. |
| **channel** | sense it arrives by (scent / sound / sight / touch / taste) |
| **intensity** | raw magnitude (faint … strong) |
| **clarity** | physical degradation — distance, occlusion, background noise |
| **origin** | direction / source the signal comes from |
| **conspicuousness** | physical attention-grab — contrast, motion, loudness (object-side; whether the agent *attends* is its own call) |

### Interpretation — assigned by the agent (private, the variable part)

The agent reads the raw metadata above and *assigns* meaning. The fox and the bunny run different interpretation functions over the identical atom.

| Interpretation | What the agent assigns | Driven by |
|----------------|------------------------|-----------|
| **recognition** | signature → concept (`fox-scent` → *fox* → *predator*) | memory, species-knowledge |
| **relevance** | which of my drives this speaks to (or none) | active drives |
| **valence** | good / bad / neutral *to me* — fox-scent is neutral to a fox, aversive to a bunny | species-nature, drives |
| **attention** | whether I notice it at all | arousal, hypervigilance |
| **trust** | how much I believe it | personality, state, memory, clarity |

### The weights (what the agent brings to interpretation)

- **Personality** (lifelong) — a timid bunny assigns aversive valence to ambiguous signals by default; a bold one stays neutral.
- **Physical state** — hunger raises the relevance & valence the bunny assigns to food signatures; pain raises threat-interpretation globally.
- **Emotional state** — fear is a global bias: more signatures recognised *as* predator, sound/sight attended harder, food/play down-weighted.
- **Memory** — what a signature *means*, and whether to trust it, is learned. (Memory is also a percept *source* — see below.)

> `interpreted_atom = interpret(objective_atom, metadata | personality, state, memory)` — the atom is the same for all; `interpret` is what differs.

### Aggregation lives in the agent, not the molecule

The **objective molecule** *is* a flat set of atoms — neutral, no good or bad in it (your original "sum of descriptions" holds here). Danger and fear are **not** in the atoms or the molecule. They appear only when an agent interprets, and they reflect the *agent's nature*, not the world's:

- A **prey** agent (the bunny) interprets non-linearly: one atom it reads as sufficiently aversive **dominates** the whole reading (veto, not vote — a meadow of sweet clover still reads "flee" under one fox-signature), and under fear it *completes* low-clarity atoms **as** threat (survival-favouring false positives).
- A **predator** agent reading the identical molecule would aggregate the other way — that same fox-signature is neutral or kin.

So "the molecule is not a flat sum" was the wrong place to put it. The molecule is a flat set; the **bunny's interpretation function** is what's non-linear. Different species, different aggregation, same atoms.

### Subjectivity reaches into affordances

Because the affordance match (previous section) runs on the **subjective** molecule, the "CAN" itself becomes subjective. Three layers now:

| Layer | Whose view | Example |
|-------|-----------|---------|
| **objective-CAN** | physics | the open meadow *can* be binkied in |
| **perceived-CAN** | subjective molecule | a frightened bunny reads `safety: high` as low → Binkying **not afforded to it** |
| **DO** | drive selection | among perceived affordances, the active drive picks one |

Fear can **hide** real affordances and **conjure** false threats; hunger can make marginal food read as edible. The bunny acts on perceived-CAN, never objective-CAN.

### Memory is just another percept source

A percept need not come from the senses. **Memory replays atoms into perception the same way the nose delivers them** — recalled atoms enter the identical interpretation pipeline. Note: sense-percepts read the *shared objective* atom; memory-percepts replay the agent's own *private stored* atom. Both are read-only to interpretation; they differ in provenance and in whether they still match the world. Consequences:

- A remembered *fox-here-at-dusk* injects a fox-atom even when nothing is currently smelled — the bunny can flee a predator that isn't there.
- Memory-percepts can be **stale or wrong** (the fox left an hour ago), so perception ≠ current reality — the same gap as "fear fabricates," from a different source.
- Memory also feeds *interpretation*, not just supply: **recognition** (this signature means *fox*) and **trust** (I've been fooled here before) are memory-driven. So memory is both a *source of* atoms and an *input to* their interpretation.

### Worked example — same fox, two bunnies

Objective scene (read-only, identical for both): `scent = fox · intensity 0.3 · clarity low · origin downwind` · `space = open, no cover` · `food = clover, 1 m`. Note there is no `danger` and no `safety` atom — those are *interpreted*, not present in the world.

| | **Bold + content** bunny | **Timid + anxious** bunny |
|---|---|---|
| recognition | "fox — faint, far off" | "FOX — near and closing" (memory of a past scare fills the low-clarity gap) |
| valence assigned | ~neutral | strong aversive |
| attention | barely notes it; clover dominates | fox signature seizes all attention |
| molecule reads as | *safe foraging ground* | *exposed, predator near* |
| afforded | Grazing, Binkying | Freeze, flee, Digging |

Same read-only atoms, opposite interpretation, opposite behavior. **That gap is the subjective truth** — and notice "safe" vs "exposed" was never in the world; both bunnies *authored* it.

### Open questions (flagged, not yet decided)

- **Metadata per-type or per-instance?** Is `signature` a type-level fact (all fox-scent shares it) with instance-level `intensity`/`clarity`/`origin`? Most likely: signature + identity at the type, magnitude at the instance.
- **The write side of memory.** Covered as a *read* source here; the kinds of memory, the recall→emotion rule, the **write side** (store surprise + stakes as deltas), and **consolidation** (decay erodes specifics → commonalities mint a fact) are in the Memory section. The decay/consolidation *rate* is **not** an architectural gap — it's config set by the sim's timescale (see *The General Model → Timescale*). The architecture only owes the *ordering*, and it has it.
- **Social signals are a percept source too.** One bunny's `thump` is an external signal the *receiver* interprets as danger — fear spreading through the warren is just shared perception, no atom-injection needed. Already covered by "social signals" in the pipeline; worth formalising later.

---

## Memory — what's stored, and how recall makes feeling

Memory is a **percept source** (previous section). Here: *what kinds* of memory there are, and the rule that **feeling is never stored — it's rebuilt on recall.**

### The kinds of memory

| Kind | Stores | Example | What it feeds |
|------|--------|---------|---------------|
| **Spatial** | positions & relations of molecules — a map | burrow at the oak · water past the fence · open ground = exposed | navigation; "where can I do X" |
| **Entity** | a remembered molecule keyed by signature — a per-thing dossier | *this* fox · my bonded doe · the big tom · the owner | recognition (signature → which entity) |
| **Episodic** ("what an entity did") | events: entity + action/activity + place + (time) | the tom boxed me at the feeder yesterday · a fox chased me across the meadow | expectation, trust, threat-reading |
| **Self / autobiographical** ("what I did") | the agent's *own* past actions/activities + their outcomes | I dug here and hit root · I fled and escaped · I tried that gap, too narrow | knowing what I've tried; predicting my *own* action outcomes; competence |
| **Categorical** (concepts) | groupings of instances into a type | *this* fox + *that* fox → the category **predator**; hay + clover + pellet → **food** | generalising to never-seen instances; **prediction** |
| **Factual / semantic** | abstracted rules over categories: signature → meaning, pattern → value | foxes are danger · clover is food · soft earth digs | **the whole interpretation layer** |

Two observations that tie back:

- **Facts ARE the interpretation substrate.** "fox-signature → predator → aversive" is a stored fact. So the `recognition` / `valence` / `trust` columns from the interpretation table are *memory lookups*. Interpretation is largely factual memory in action.
- **Episodes generalise into facts.** One "a fox chased me here" is episodic. The same event many times distils into "foxes are danger" + "this place is dangerous" — semantic. That distillation is the bridge between the episodic and factual stores (and part of the open write-side question).

### Categories generalise — and set up surprise

A **category** is a learned grouping of instances into a type (this fox + that fox → *predator*). It does two jobs:

- **Generalisation.** A never-seen fox still recognises *as* predator, because its signature matches the category — no prior episode with *this* fox required. Without categories, every new instance would be meaningless until individually learned.
- **Prediction.** A category isn't just a label, it's an **expectation**: matched to *predator*, the bunny predicts fox-like behaviour (it will chase, it is danger) *before* anything happens. Recognition is prediction.

**Surprise = prediction error.** When an instance violates its category's prediction, the gap *is* the surprise — and it's a signal, not noise:

```
category / memory  →  PREDICTION  ──┐
                                    ├──►  mismatch  =  SURPRISE
current perception ─────────────────┘        │
                                             ▼
                                   attention spikes · belief updates · (write-side: store this)
```

A fox that *doesn't* chase — that grooms calmly — violates *predator*. The surprise spikes attention and flags the episode as worth storing (it's exactly the high-surprise events that should get encoded — a candidate answer to the write-side trigger). Surprise is where prediction meets perception and the model corrects itself. *(Flagged as a later mechanism, per your note — but this is its hook: categories predict, perception checks, surprise updates.)*

### Feeling is rebuilt, never replayed (your rule, formalised)

This is the load-bearing constraint:

- **Stored:** the *content* — atoms, molecules, facts, events. Descriptions only.
- **Not stored as a live value:** the emotion. Recalling does not play back a saved feeling.
- **On recall:** the recalled content enters the same `interpret()` as live perception, and the **current** personality + state builds a **fresh** emotional state. Same memory → different feeling depending on *now*.
- **Optional & inert:** the emotion-at-encoding may be kept, but only as a *fact about the episode* ("I was terrified here") — evidence that can *raise* threat-interpretation, never a trigger that *forces* the feeling. It's read like any other fact, not re-injected as emotion.

> The point of the rule: emotion stays a pure function of the **present** agent. A scare recalled while calm and fed barely stings; the same scare recalled at dusk while hungry floods back. Nothing about the stored memory changed — the interpreter did. If feelings were stored-and-replayed, every recall would feel identical forever. They're not, so it doesn't.

### Recall, end to end

```
cue (a place, a signature, a current need)
        │
        ▼
recall CONTENT from memory        ← spatial / entity / episodic / factual
        │  (descriptions only — no feeling attached)
        ▼
interpret(content | personality, CURRENT state, other memory)
        │
        ▼
fresh emotional state  +  updated subjective molecule
        │
        ▼
affords / suppresses behavior now
```

Worked: recalling *"a fox chased me across this meadow."* Content retrieved = meadow (spatial) + fox (entity) + chase (episodic), plus an inert "felt terror then" tag.

- **Bold, fed, midday now** → interprets as "old news, fox long gone" → mild caution, forages anyway.
- **Timid, hungry, dusk now** → interprets as "this place = predator" → won't enter, or enters hypervigilant.

Same stored memory, opposite feeling and behavior — because the feeling was *built at recall*, not retrieved with the memory.

### The write side — store the residual, not the record

You can't record everything, and shouldn't. The "bare minimum" isn't a vague ideal — it has a precise form: **the part the model couldn't already predict.** Anything predictable is recoverable from categories at recall, so storing it again is wasted space. The write side is *compression*: keep the surprise, drop the expected.

**When to encode (the trigger).** You can't know at the time what will prove essential, so the system bets on two cheap proxies:

- **Surprise (prediction error)** — primary. Matched the prediction? Discard — the category already covers it. Violated it? Store — that's the model being wrong, the one thing worth keeping. (Surprise = information, in the literal sense: predicted events carry ~no new bits.)
- **Arousal / stakes** — secondary. High-intensity events (near-death, strong reward) etch hard even when *not* surprising; survival can't afford to forget a predator just because it behaved as expected.

**What to encode (minimal content).** The **gist / delta against the category**, not raw atoms. Store "*this* fox — but it didn't chase"; inherit the rest of *fox* from the category. Categories are the compression dictionary — you save only the divergence. Just enough to (a) recognise it again and (b) correct the prediction.

**Staying minimal over time.** Encoding is only half; minimalism is also maintained downstream:

- **Decay** — unrecalled memories fade; re-encounter refreshes. Forgetting is a feature, not a failure.
- **Consolidation** — repeated episodes distil into a category/fact, then the individual episodes drop. You keep "foxes chase"; you forget each particular chase. *(Its own mechanism below.)*

**Reconciling with the no-stored-feeling rule.** Emotion is still never stored as content — but the *intensity* of the feeling at encoding is the **write-strength dial**: how deep the etch. Arousal decides how strongly the content is written; the feeling itself isn't saved, and is still rebuilt fresh on recall. That's why a terrifying moment burns in — high arousal → strong write — yet what's burned in is the *content*; the dread on re-reading it is reconstructed now, not played back.

> So **"store bare minimum essential" sharpens to: store the surprising and the high-stakes, as deltas against categories, and let decay undo the over-storage.** You never store "the essential" — you can't see the future — you store cheap proxies for it and prune later. Minimalism by compression at write, by decay over time.

### Consolidation — episodic erodes into semantic

Decay isn't uniform fading to nothing. It's a **separate background process** that erodes an episode's *specifics* while its *gist* survives — and the surviving gist, aggregated across episodes, mints or updates a semantic fact.

```
[Fresh episode] ──► [Decay erodes specifics] ──► [Commonalities aggregate] ──► [Mint / update fact]
 Fox chased me        the Oak, the Tuesday          "fox + chase" recurs;        Foxes = dangerous
 at the Oak,          fade — each is                 the spot & day, being        (confidence ↑ each
 on Tuesday           idiosyncratic to one           variable, cancel out         time it repeats)
```

**Why those details and not others.** What survives is what's **shared across episodes**; what erodes is what's **idiosyncratic to one.** Every fox-chase contains "fox" and "chase"; each has a *different* spot and day — so spot and day are noise that cancels, while fox+chase is signal that accumulates. The semantic fact is literally the **intersection** of the episodes. (Relevance reinforces it: nothing keys off "which Tuesday," so nothing protects it from erosion.)

**Mint vs update = running statistics.** The first chase *mints* "fox = dangerous." Each later one *updates* it — confidence up. The fact carries a strength the episodes vote on; a *contradicting* episode (a fox that groomed calmly) updates by weakening or splitting it ("foxes usually dangerous, but…").

**Surprise resists decay** — the catch that ties back to the write side. A *confirming* episode dissolves fastest: once its lesson is in the fact, the specific is redundant. A *surprising* episode **resists erosion**, because the category can't absorb it. So consolidation erodes the expected and preserves the exceptional — the calm fox stays a vivid specific while a thousand ordinary chases blur into one fact.

**Two-stage compression.** This is the *second* squeeze. At write you stored only the delta against the category; over time, decay strips even that delta once it's folded into semantic. An episode is kept verbatim only while it's still informative *as a specific* — consolidation deletes it once its information has migrated to the fact.

**The cost: confident false memory.** Consolidation is lossy and one-way. Once specifics erode, recall *reconstructs* them from the fact — the category fills the gaps back in, exactly as perception does. So a consolidated memory recalled is a **category-prediction wearing the costume of a specific episode**: it can be detailed, confident, and wrong. (Why eyewitnesses misremember — schema dressed as recollection.)

**When it runs.** A background process, decoupled from the live perceive-act loop — plausibly during **rest / sleep**. That hands the *Sleeping* activity a second job beyond recovery: it's when episodes are consolidated into facts.

### Where this comes from (grounding)

This whole subjective + memory layer lines up with **Lisa Feldman Barrett, *How Emotions Are Made* (2017)** — worth naming, since the model arrived at it independently:

| In this doc | Barrett's term |
|-------------|----------------|
| feeling is rebuilt on recall, never replayed | **theory of constructed emotion** — emotions are constructed in the moment, not stored & triggered |
| interpretation produces the subjective molecule | **the predicting brain** — perception is prediction, corrected by sensory input |
| both bunnies *authored* "safe" vs "exposed" | **affective realism** — we experience our predictions *as* objective reality |
| categories generalise & predict | **concepts** — the brain makes meaning by categorising |
| surprise = prediction error | **prediction error** — the signal that updates the model |

The convergence is a good sign the abstraction is sound rather than arbitrary. (Citation from memory — the author and ideas are solid; double-check the exact wording against the book if it ends up in anything formal.)

---

## The General Model — what the bunny was a probe for

The bunny was never the point. It was a **probe** — a concrete thing you start taking apart to recover the architecture that holds *any* agent together. With the bunny set aside, the architecture is:

> **An agent is a private process wrapped in a public molecule, coupled to a world of molecules.**

### Two faces of an agent

| | **Molecule** (public) | **Process** (private) |
|---|---|---|
| view | third-person — what others perceive | first-person — what it is to itself |
| substance | atoms + relations: body, signals, observable behaviour | the perceive → interpret → act → remember loop |
| access | objective, read-only, **shared** — any agent can perceive it | **sealed** — no agent can read another's |
| role | the agent's interface to the world | the agent's interior |

To every other bunny, a bunny **is** a molecule — one big composite of atoms. The process that constructs its experience is private and never leaves it. *This is why there is no "self-molecule": the self is the **process**; the molecule is only how others encode the agent.* An agent can partly perceive its own molecule (proprioception, seeing its paws), but any sense of self is the **process modelling itself** — a private interpretation, not a stored object.

### The world is molecules; agents are molecules in it

Everything an agent knows — of the world and of other agents — arrives as **perceived, then privately interpreted, molecules.** There is no privileged channel: a bunny reads another bunny the same way it reads a rock or a carrot — perceive the molecule, interpret it privately. Agents are just molecules that happen to contain a process.

### The loop (the process)

```
   world molecules ─┐
   memory ──────────┼─►  PERCEIVE  ─►  INTERPRET (private)  ─►  subjective molecules
   signals ─────────┘                                              │
                                                                   ▼
                                                          AFFORD  ─►  SELECT (drives)
                                                                   │
   change world molecules  ◄──────────  ACT (write atoms)  ◄───────┘
   (incl. the agent's own public molecule)                         │
                                                                   ▼
                                                        REMEMBER (encode surprise / stakes)
                                                                   │
                                                                   └─►  retunes INTERPRET …
```

The **membrane** between public and private has exactly two crossings: **perception** carries world → process (molecules in), **action** carries process → world (molecules out). Everything inside is private; everything outside is shared.

### Agents couple through molecules only

Two processes never touch directly. The only channel between them is molecular:

```
A's process → A acts → A's molecule changes (a thump, a scent, a groom)
                                   │
                                   ▼
              B perceives A's molecule → B's process interprets it → B acts → …
```

A thump, a fox's scent, a doe grooming her kit — each is a **molecule-change one process emits and another perceives.** "Reading another's mind" is *inference from its molecule*, never access to its process — so theory-of-mind is interpretation, fully subjective, and can be wrong. B's model of A is B's private construction.

### Where every layer of this doc sits

| Layer | Side of the membrane |
|-------|----------------------|
| atoms · molecules · the world | **public** substance |
| actions · activities · affordances | the **membrane** — process reading & changing molecules |
| interpretation · subjective truth | **private** process |
| drives · affects · personality | **private** — what biases interpretation & selection |
| memory (read & write) · surprise | **private** store that tunes the process |

That is the whole answer the bunny was probing for:

> **A private constructing process, exposed to the world only as a molecule, interacting with other such processes only through molecules.**

The bunny is one instance — prey-tuned drives, a lagomorph body, a particular Umwelt. Swap those parameters and the same architecture is a fox, a magpie, a person. The model holds them all; the bunny just happened to be the one we took apart.

### Timescale is a free parameter

The architecture fixes the **ordering** of timescales — perception ≪ drives ≪ learning ≪ personality, with consolidation gated to rest (the *Mental Things* table already lays this out; mood is not a rung — a lingering mood is the felt read of a slow unresolved drive, running at the drive's clock). It does **not** fix their **absolute rates**: how many ticks an episode survives before eroding, how fast a mood settles, how slowly a personality drifts. Those are config of the sim instantiation — set the tick rate and everything scales against it.

This is why "the decay curve" was never a hole in the design. Rates are a property of *how fast you run the sim*, not of *what the agent is*. The same architecture runs at a mayfly's tempo or a tortoise's; only the ordering is invariant, and the ordering is the part the model owes. Pin the order, leave the clock to config.
