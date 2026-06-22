# What Is a Memory?

Companion to *What Is a Bunny?*, *What Is a World?*, and *What Is a Drive?*. The bunny doc owns the **concept** — the six kinds, feeling-rebuilt-never-replayed, the surprise+stakes write trigger, gist-as-delta, consolidation-as-intersection, confident false memory. The world doc owns the **slots** — the `Memory` registry, pipeline rows 1b/2/5/7, `MemoryWrite` events, open issue #4 and open (ii). This doc is the design pass between them: the data structures, the write/recall/consolidation mechanics, and the bounded-storage policy — pinned *before* a proto, the same way the drive doc had to exist before p1 meant anything.

## The short answer

A memory is not a recording. It is **a delta against a prediction** — written only when the prediction failed (surprise) or the stakes ran high (arousal), at a strength the arousal sets, decaying unless refreshed, folded into the predictions themselves during sleep, and **re-interpreted, never replayed** at recall.

The structural claim underneath, and the reason the six kinds need only a few data structures: **the semantic store is the compression dictionary, and everything else is stored as a diff against it.** A category predicts; an episode keeps only what the prediction got wrong; consolidation migrates what recurs from the diffs back into the dictionary; recall reconstructs the full picture by re-applying the diff to the (current) dictionary entry — which is exactly why a consolidated memory recalled is confident and wrong in the places the dictionary filled in.

### Carried forward, not re-decided

These are the bunny doc's commitments; this doc builds under them, never re-litigates them:

- Six kinds: spatial · entity · episodic · self · categorical · factual.
- **Feeling is never stored** — content only; emotion is rebuilt by `interpret()` at recall from the *current* agent. The emotion-at-encoding survives only as an inert fact-tag.
- Write trigger = **surprise** (primary) + **stakes/arousal** (secondary); arousal is the **write-strength dial**.
- Content = **gist/delta against the category**, not raw atoms.
- **Decay** prunes; re-encounter and recall refresh; forgetting is a feature.
- **Consolidation** = intersection across episodes → mint/update facts as running statistics; runs in **Sleeping**; confirming episodes dissolve fastest, surprising ones resist.
- **Confident false memory** is the accepted cost, not a bug.
- Decay/consolidation *rates* are timescale config, not architecture. The architecture owes the *ordering* — pinned below.

## Does it have a real name? (mostly yes)

Same exercise as the drive doc: the synthesis is ours, the parts have established names — adopt them.

| In this framework | What researchers call it | Whose |
|---|---|---|
| episodic vs factual stores | episodic vs semantic memory | Tulving |
| recall reconstructs from the category | schema-based reconstruction | Bartlett (*Remembering*, 1932) |
| episodes erode into facts during sleep | systems consolidation; complementary learning systems (fast episodic store + slow statistical learner) | Squire; McClelland, McNaughton & O'Reilly |
| confident false memory | reconstructive memory errors; misinformation effect | Loftus |
| store the delta, inherit the rest | gist vs verbatim traces | fuzzy-trace theory (Brainerd & Reyna) |
| surprise gates encoding | prediction-error-driven encoding; novelty encoding | Greve & Henson; von Restorff (isolation effect) |
| arousal etches deep | emotional modulation of consolidation; flashbulb memory | McGaugh (amygdala modulation); Brown & Kulik |
| recall is itself a write (refresh) | reconsolidation — recall renders the trace labile, re-stored | Nader |
| unrecalled memories fade | forgetting curve; adaptive/motivated forgetting | Ebbinghaus; Bjork ("desirable difficulties") |
| the spatial store | cognitive map; place representation | Tolman; O'Keefe & Nadel |
| the innate seed | prepared/unprepared learning; innate releasing mechanisms; core knowledge | Seligman (preparedness); Tinbergen & Lorenz; Spelke |
| memory replays into the percept stream | constructive episodic simulation | Schacter & Addis |

Recommendation, mirroring the drive doc: keep **memory** as the umbrella, and use *episodic/semantic* for the store split, *gist/verbatim* for the delta encoding, *consolidation* for the sleep job, and *reconsolidation* for recall-refresh. (Attributions from memory — verify against primary sources before anything formal.)

## Six kinds, four stores

The bunny doc's six kinds are *functional* distinctions. Physically they collapse to four stores, because two pairs share a shape:

| Store | Holds (kinds) | Keyed by | A record is |
|---|---|---|---|
| **PLACES** | spatial | position (tile-res), chunk-indexed | category-ref + delta-bag ("burrow, *mine*, at the oak"; "this meadow — *fooled here before*") |
| **THINGS** | entity (dossiers) | **signature** (+ optional generational `entityRef`) | category-ref + delta-bag ("*this* fox — didn't chase"; "the big tom — boxes") |
| **EVENTS** | episodic + self | (subject, verb, object, place) | category-ref + delta-bag + inert affect tag; **self-memory = the subject is me** — same store, no fourth structure |
| **MEANINGS** | categorical + factual | signature-prototype | a category node: prototype + predicted atoms (running stats) + valence + confidence (see below) |

Two collapses, both already implied by the bunny doc:

- **Categorical + factual are one store.** "Facts ARE the interpretation substrate" and "a category isn't a label, it's an expectation" are the same observation from two sides: a fact (*fox = danger*) is just a field of the category node (*predator* carries `valence: aversive, confidence: high`). One node type holds both the grouping and the rules about it.
- **Episodic + self are one store.** "What an entity did" and "what I did" differ only in the subject slot. Competence ("digging here fails") consolidates out of self-episodes by exactly the machinery that distils "foxes chase" out of fox-episodes.

Keying THINGS by **signature**, not entity id, is deliberate: recognition is signature-match (that's what a nose does), and it keeps dossiers meaningful for entities currently out of view. Where a record does hold a live reference, it's a generational `entityRef` (world doc A2) — a dead entity's ref derefs as *gone*, which the planner already absorbs as perceptual staleness. Nothing special to add.

## The unit of storage — the delta record

One record shape serves PLACES, THINGS, and EVENTS:

```
MemoryRecord {
    key          : store-specific (position | signature | event tuple)
    categoryRef  : MEANINGS node this diffs against (nullable — see novelty)
    deltaBag     : sorted Atom[] — ONLY the atoms that diverge from the category's prediction
    meta[]       : PER-ATOM, index-aligned to deltaBag — each fact carries its own
                   { strength: byte (0–255) — etch depth; flags: INNATE (decay/evict-immune)
                     · SURPRISE (decay-resistant) }
    writtenAt    : tick
    lastRefresh  : tick
}
```

> **Strength is per-atom (evolution).** Originally one `strength` byte + `flags` per *record*. That is
> right for a coherent dossier (THINGS/EVENTS: one entity, one importance) but wrong for a *bundle of
> heterogeneous facts*: a PLACES record holds `{BuildingKind, ProvisionsHere, DangerHere}` whose
> importances differ by orders of magnitude. One record-strength cannot decay them at three rates, so
> coarse decay erodes a whole building's knowledge overnight. The fix moves strength + flags onto the
> atom (the `meta[]` above) — `Atom` itself stays the clean `{type,value}` substrate. Importance is
> still *strength* (no second scoring system); the record's eviction strength is the max over its
> atoms, and a record dies when its last atom is forgotten. Grounded in "Surprise = per-atom
> prediction error" below — per-atom etch is the natural consumer of per-atom surprise.

- **`deltaBag` reuses the stored-atom-bag machinery** (sorted `Atom[]`, binary-searchable by `AtomTypeId`) from the world doc's atom backings. No new container; a memory's content is literally atoms, which is what lets recall replay it into the percept pipeline unchanged.
- **The inert affect tag is just an atom in the bag** — `(affectTag, 0.9)` is *content*: "I was terrified here," read at recall as one more piece of evidence that can raise threat-interpretation, never injected into the `Affects` register. The no-stored-feeling rule holds by construction: the register is a pipeline register, and nothing in a `MemoryRecord` writes it.
- **`strength` is the one scalar doing three jobs** (now *per atom*): write-depth at encoding (each atom etched by `scale(max(its own prediction-error, arousal))`), decay target during sleep, eviction order when the store is full. Keeping it a byte keeps the whole record fixed-point — see *Determinism*.
- **Novelty stores verbatim.** A percept with *no* category match (`categoryRef = null`) stores its full atom set — there is no prediction to diff against, so nothing is droppable. Correct and self-limiting: novel things are exactly the things worth keeping whole, and the first consolidation pass starts compressing them (mint a node, re-diff).
- **Recall = reconstruct.** `recall(record) = category.predictedAtoms ⊕ record.deltaBag` (delta wins where both speak). The reconstruction reads the **current** node — if the category has drifted since encoding, the recalled "specifics" are the *new* prediction wearing the old episode's costume. Confident false memory is this one line, not a separate mechanism.

## The MEANINGS store — categories as running statistics

```
CategoryNode {
    prototype    : signature centroid — what recognition matches against
    predicted    : per-AtomType running stats { mean (8.8 fixed-point), spread, count }
    valence      : the fact field — appetitive/aversive charge + confidence
    flags        : INNATE
}
```

- **Recognition = nearest prototype above a match threshold.** Below threshold = *novel* → maximal surprise. (The similarity metric and threshold are content/authoring, flagged in *Open knobs*.)
- **Prediction = the `predicted` stats.** Only **low-spread** atom types predict: every fox-episode contains *fox* and *chase* (low spread → enters the prediction); each has a different spot and day (high spread → never enters). This is "the semantic fact is literally the intersection of the episodes," made mechanical: **the intersection is variance-gated running statistics**, not set-intersection.
- **Surprise = per-atom prediction error; the aggregation is an open operator — likely *two*.** Surprise has two consumers asking different questions. The **attention spike** asks "is anything alarmingly off?" — that's MAX (a fox that grooms calmly violates one prediction hard; the worst signal must not be averaged away — the same reasoning as threat's MAX projection in p2/p3). The **encode gate** asks "how much here is new?" — an encoding-*volume* question, and MAX answers it badly: a familiar fox with one notched ear would score ≈1.0 and etch trivia at full strength, flooding the store the eviction section assumes stays calm. SUM-or-count-of-violated-predictions matches "store only the divergence." Proposed: **MAX for the attention spike, SUM/count for the encode gate and write-strength** — the p2 lesson again (same operator family, two sites, chosen per site). Flagged, not settled; p6 tests it (see below).
- **Mint vs update vs split.** First consolidation of clustered novel episodes *mints* a node (their variance-gated intersection). Confirming episodes *update* — count up, spread down, confidence up. A contradicting episode *weakens* (confidence down) and, past a contradiction threshold, *splits* the node ("foxes usually dangerous, but…"). The split mechanics are the one consolidation piece left deliberately under-pinned — p6's job to inform (see *Open knobs*).

### The innate seed (resolved this pass)

The registry table says `Memory` is "learned"; the interpretation table invokes "species-knowledge." Both are right: **the species ships a small seed of MEANINGS nodes** — *predator* (fox-scent prototype, `valence: aversive`, high confidence), *food*, *water*, *conspecific*, *shelter* — flagged `INNATE` (decay- and evict-immune), and **learning accretes in the same store**: new nodes minted beside the seed, and the seed's own stats updated by experience (a bunny can learn *this* fox is slow, even though *predator* is innate). "Learned" describes the accretion; the seed is what makes tick one work — without it, safety's reset-on-percept has no threat-recognition to track and surprise has no prediction to violate. The encoding machinery cannot bootstrap from an empty dictionary, so the dictionary doesn't start empty.

> **Flagged for later — the seed needn't be only genetic.** A kit's seed could be part-transmitted by parents (alarm-thump associations, taught food valences): same store, same node shape, a *social* installer instead of the species table. That's a teaching/culture mechanism riding entirely on existing machinery — social signals (already a percept source) + high-stakes encoding in a critical period. Future content, no new architecture; noted so it isn't reinvented.

## The write path — detect at 2, write at 5

The "ordering the architecture owes" (bunny doc, write-side open question), now explicit:

```
tick N   row 2  Interpretation   percept vs category prediction → SURPRISE flags (+ stakes = Affects intensity)
tick N+1 row 5  Memory           reads the flags → emits MemoryWrite events (the deltas)
tick N+2 write  gather-reduce    Memory[self] updated
```

- **Detection and writing are separate systems** because detection needs the interpretation context (which category matched, which atoms diverged) and writing needs the *whole tick's* flags plus the `Affects` register read — standard pipeline stagger, one more stage, no intra-tick cycle.
- **The encode gate**: a record is written iff `surprise > θ_s` **or** `arousal > θ_a`; its strength = `scale(max(surprise, arousal))`. Below both thresholds, the tick leaves no trace — predicted events carry no new bits, and a calm bunny's ordinary graze is never stored. (θ's are species config; whether they deserve a personality split is an open knob.)
- **Two predictors, one trigger.** Perceptual surprise (category vs percept, row 2) and **plan surprise** (the ad promised an outcome; the landed action delivered something else — a dig that hits root, a revalidation that fails) both flag for row 5. Plan surprise is what feeds the self/EVENTS store; the Planner already detects its half (revalidation), it just additionally flags it. *(Proposed addition to the world doc's systems table: row 3 gains "plan-surprise → Memory" in its emit column — sign-off owed.)*
- **`MemoryWrite` is a family of deltas** through A1's gather-reduce, sole-target `Memory[self]`: `Encode(record)` · `Refresh(key, Δstrength)` · `StatFold(node, stats)` · `Decay(batch)`. `StatFold` is the one **ungated** member — emitted for every *recognition* (consolidation step 0, p6 M1), not only on surprising ticks; the others stay behind the encode gate or in sleep. Refresh and StatFold are additive-commutative; Encode is an absolute insert resolved by A1's priority-then-min-id (in practice uncontested: row 5 is the only awake writer, row 7 the only asleep writer — the discipline is *one Memory-writing system per agent state*, so the reducer's conflict path should never fire for Memory cells; it exists as the safety net, not the mechanism).

## The recall path — two doors, no new machinery

Recall must not invent a channel. It has exactly two, both already in the systems table:

**Door 1 — supply (row 1b, Perception).** Cue-keyed records are replayed as memory-percepts into **the same capacity-limited, affect-biased top-K attention as sense-percepts**. Same K, same bias weights, no reserved budget; a memory-percept's salience = cue-match × strength × the affect channel weight, against the raw buffer's items. Cues are keyed lookups, never store scans:

- *current position* → PLACES records in this chunk,
- *perceived signatures* (including partial, low-clarity ones) → THINGS dossiers — which is how "memory of a past scare fills the low-clarity gap,"
- *active drive poles* → records whose category matches the drive's target class (hunger raises the food channel, and the food channel includes the **remembered** carrot).

What falls out free, because the slots are shared:

- **Drive-driven recall is just the existing bias.** A hungry bunny "thinks of" the remembered carrot because the food channel is up — the planner then targets it from `SubjectiveView` like any percept. Row 3's read set is untouched: **the planner never queries Memory directly**; the remembered carrot reaches the market as a percept, goes stale on arrival exactly as the world doc's perceptual-staleness section already describes.
- **Rumination crowds out perception.** Threat memories competing for the same K means a terrified bunny replaying the fox can miss the real clover — p3's tunnel vision (P4) extends to memory without re-running the proto.
- **A third spiral pathway.** Fear raises the threat channel → threat *memories* win slots → re-interpreted now, they sustain fear. The affect-completion spiral (p1) and the perceptual spiral (p3/P5) gain a memory sibling; same damping applies (act → discharge → channel drops).

> Vetoable: the alternative is a small reserved recall sub-budget (K_sense + K_memory), if shared-K starves recall too aggressively under calm-but-busy scenes. Default is shared-K — it's the p3-consistent pick and the one with the emergent predictions; p6 can stress it.

**Door 2 — lookup (row 2, Interpretation).** The MEANINGS store is not recalled *into* perception — it **is** the interpretation substrate. Recognition / valence / trust are direct semantic lookups; the matched node's prediction is what surprise is measured against; its valence feeds the pole writes (the third writer). Not capacity-limited, not a percept: you don't *attend to* knowing what a fox is.

**Recall is itself a write.** Any record reconstructed through Door 1 (or whose dossier filled a recognition gap) gets a `Refresh` emitted by row 5 — strength bumped, `lastRefresh` updated. That's reconsolidation, and it's the loop that makes "re-encounter refreshes" true for free, since a re-encounter *is* a recall-assisted recognition. *(Proposed addition to the world doc's systems table: row 5's read set gains the attended memory-percepts — it must see which recalls won attention to refresh them — sign-off owed.)*

## Consolidation — the sleep job, mechanically

Row 7, in `Sleeping` state, over the agent's own stores (private, embarrassingly parallel across agents). Per pass:

```
0  FOLD — NOT a sleep step (p6, M1; resolved this pass).  Stats fold AWAKE, at
   recognition: row 5 emits a StatFold for every percept row 2 recognized —
   count/sum/sumsq integer adds per atom type, at most K per tick (the
   attention bottleneck bounds the write volume). Sleep-fed folding cannot
   converge: the encode gate stores only the exceptions, and statistics
   trained on exceptions never learn the typical — in p6 the node never
   gated at all. The match itself costs nothing new: interpretation already
   matches every attended percept (Door 2); the fold rides along.
1  RE-DIFF   re-diff each record's deltaBag against the UPDATED prediction;
             drop atoms the category now predicts (the record shrinks — its
             information has migrated)
2  MINT      cluster unmatched novel records by signature proximity; a cluster with
             enough support mints a CategoryNode = its variance-gated intersection;
             members re-key to it and re-diff (novelty stops being verbatim)
3  DECAY     per atom: strength -= max(1, base · (255 − strength)/255),
             base = SURPRISE-flagged ? r_resist : r_normal; INNATE immune;
             an atom at 0 is forgotten; a record whose last atom is gone is dropped
4  SETTLE    contradiction stats: confidence down where contradicted; past the
             split threshold, split the node (under-pinned — see Open knobs)
```

- **"Confirming episodes dissolve fastest" is steps 1+3 composing**: a confirming record's bag re-diffs to nearly empty (the fact absorbed it), and an empty-bag record is pure redundancy — it carries no delta, so decay takes it first. A *surprising* record's bag can't be absorbed (the category refuses its atoms — high spread or contradiction), so it survives re-diff *and* decays slower. The calm fox stays vivid while a thousand ordinary chases blur into the fact — as ordered (p6: median lifetime 585 vs 1155 ticks).
- **Decay is strength-scaled, not flat (evolution; the ordering is unchanged).** The rate now falls as strength rises — `max(1, base·(255−s)/255)` — so an important atom barely erodes while a trivial one fades fast, *continuously*, rather than every non-SURPRISE atom losing a fixed amount per pass. The flag tiers (INNATE immune, SURPRISE `r_resist`) still hold; this adds graded persistence *within* a tier by the atom's own strength. Validated empirically in the DFU wedge: under flat decay a moderately-held fact (provisions, strength 160) collapses overnight when re-observation pauses (~232 → 26 over one sleep); strength-scaled retains it (~233), so an agent doesn't forget where food is every night. "Confirming dissolves faster than surprising" is preserved (a confirming atom starts low / re-diffs away; a SURPRISE atom uses the smaller base *and* starts high → plateaus). Rates remain config; this is a change to the operator's *shape*.
- **The Oak and the Tuesday** erode by the same composition: spot/day are high-spread across episodes, so they never enter the prediction (the fact stays clean — p6 confirms no fact ever inherits them), and they sit in each record's bag until step 3 takes the whole record. The fact keeps "fox + chase"; no fact ever keeps "the Tuesday."
- **Two-stage compression, located**: stage one at write (delta vs category, row 5), stage two here (re-diff against the stats the awake folds have been updating). An episode survives verbatim only while informative *as a specific*.
- **Sleep gains its second job**, as the bunny doc promised: recovery for the body, consolidation for the store. A bunny prevented from sleeping accumulates raw episodes and stops minting facts — a testable prediction, not a bug.
- **Decay is sleep-gated — awake memory never fades.** All strength loss lives in step 3; the awake path only *raises* strength (encode, refresh) — though with FOLD now awake (step 0), the MEANINGS *statistics* do drift with the day's experience; it's record *strength* that only ever falls in sleep. Three consequences. Behaviorally: forgetting requires sleep — and with the caps, that's no gift to the sleepless bunny: the store fills, the write-when-full rule bites, and learning stalls (the accumulating-raw-episodes prediction above, run to its end state). Engine-wise: memory maintenance is paid only by the **Sleeping subset**, so population-staggered sleep amortizes consolidation cost across ticks — no all-agents maintenance spike in the 100 ms budget. And structurally: the awake/asleep writer split (row 5 vs row 7) means refresh and decay can never contest the same record in the same tick — which is *why* one-writer-per-agent-state keeps A1's reducer a safety net for Memory rather than a mechanism.

### Determinism (A4/A5 compliance, stated once)

Consolidation must be replay-exact and machine-portable: records iterate in **key order** (the stores are sorted/keyed structures, never hash-iteration order); running stats are **fixed-point** (8.8 means, integer counts/spreads — no float sums, sidestepping A4's cross-machine caveat for the entire Memory subsystem); any stochastic choice (cluster seeding in MINT, tie-breaks) draws from `hash(tick, selfId, recordKey)` per A5, never a shared stream. Decay is integer subtraction. Nothing in Memory should be the reason two machines disagree.

## Bounded storage — the eviction policy (resolves open (ii) for Memory)

Decay prunes but doesn't *bound* — a burst of high-arousal weeks can outrun any rate. The bound is structural:

- **Each store has a hard per-agent cap** (species config; order-of-magnitude defaults to start: PLACES 128 · THINGS 64 · EVENTS 128 · MEANINGS 128). A `Memory[id]` is fixed-max-size by construction — flat slot arrays, SoA-friendly, snapshot-serializable for A4.
- **Eviction = lowest strength first**, tie-broken by oldest `lastRefresh`, then key order (deterministic). Strength is already the importance proxy the whole design maintains — arousal wrote it, recall refreshed it, decay eroded it — so eviction needs no second scoring system.
- **A write into a full store must beat the weakest record or it doesn't take.** A full memory under low arousal simply doesn't encode — which is the encode gate again, raised by pressure. `INNATE` records are evict-immune and the caps must exceed the seed size (assert at species-table load).
- **In steady state, eviction never fires** — decay keeps occupancy under cap and eviction is the emergency valve, not the mechanism. If a p6 run shows eviction firing routinely, the decay rate is mistuned for the timescale; that's config, not architecture.
- Cost sanity: ~448 records × ~64 B ≈ **30 KB/agent**, ~300 MB at 10⁴ agents before double-buffering — and Memory is the coldest, write-sparsest registry, so its N+1 copy should be logical (carry-forward + sparse apply), not a memcpy. Comfortably inside the budget the world doc's tile math already set the scale for.

> The acquired-drive bag (the *other* monotonic growth in open (ii)) gets the same treatment by symmetry — cap + strength-eviction on the sparse pole bag — but that's the world doc's drives-are-extensible note to amend, not this doc's to own.

## How it's built (link to the engine)

In the world doc's terms: `Memory[id]` = four bounded, key-sorted stores (PLACES · THINGS · EVENTS · MEANINGS); records are delta-records over the **stored-atom-bag** machinery; THINGS/EVENTS hold **generational** `entityRef`s (A2); all mutation flows as `MemoryWrite` deltas (`Encode` · `Refresh` · `StatFold` · `Decay`) through A1's gather-reduce, sole-target, one writing system per agent state (row 5 awake, row 7 asleep). Reads: row 1b (cue-keyed recall into shared top-K), row 2 (MEANINGS as interpretation substrate), row 7 (its own input). The species seed is one more **frozen static table** beside `Ads`/`Reactions`/`DriveDefs` — `MemorySeeds`: species → innate CategoryNodes, copied in at spawn. *(Proposed addition to the world doc's static-tables list — sign-off owed.)*

The operators it's built from recur from everywhere else: sorted atom bags · signature match · MAX aggregation · running stats · top-K attention · hash-draws · gather-reduce. One genuinely new primitive earns its place: **variance-gated running statistics** (the mechanical form of "intersection"), and even that is three integers per atom type. The abstraction keeps minting its new parts from its old parts — same signal as the drive doc, same conclusion: the level is right.

## What proto p6 should test (dynamics only, per the proto discipline)

Now that structure is pinned, p6 has named questions — *dynamics*, never architecture:

- **Convergence** — do variance-gated running stats converge MEANINGS to the world's true contingencies under noise, and does a contradicting stream weaken/split rather than corrupt?
- **The surprise aggregation** — is MAX-for-attention / SUM-for-encoding the right split, or does one operator serve both? (The notched-ear test: a familiar fox with one trivial new atom must spike attention *without* flooding the store.)
- **Compression honesty** — over a long run, does delta + re-diff + decay hold the stores bounded while recall accuracy against ground truth stays useful? Where's the knee?
- **False memory rate** — does reconstruction-from-drifted-category produce confident-wrong recalls at a *tunable* rate (a feature with a dial), not a pathological one?
- **Eviction under pressure** — does a high-arousal epoch evict load-bearing records (the home burrow) or does refresh-on-use protect them as designed?
- **The memory spiral** — does shared-K recall produce the rumination pathway (threat memories sustaining fear) and its damping by action, extending p1/p3's spiral pair?

## Open knobs

- **Similarity metric + match threshold** for signature → prototype recognition (content/authoring; the threshold doubles as the novelty line).
- **Category split mechanics** — when contradiction splits a node vs merely weakens it, and what keys the subcategory (the contradicting context?). Deliberately under-pinned; p6 informs.
- **Surprise aggregation** — MAX for the attention spike vs SUM/count for the encode gate (proposed split above); p6 tests it.
- **Encode-gate thresholds θ_s/θ_a** — species config, or a personality split (a neurotic bunny that encodes everything)?
- **Shared-K vs reserved recall sub-budget** (flagged vetoable above; default shared).
- **Caps and rates** — timescale config by construction; the defaults above are scaffolding numbers.
- **Parent-transmitted seed** — the social installer (flagged above; future content, no new architecture).

## Grounding & a caveat

The research programs this synthesis draws on: episodic/semantic (Tulving), reconstructive memory & schema (Bartlett, Loftus), complementary learning systems & systems consolidation (McClelland/McNaughton/O'Reilly, Squire), gist/verbatim (Brainerd & Reyna), prediction-error encoding (Greve & Henson; von Restorff), emotional modulation of consolidation (McGaugh; Brown & Kulik), reconsolidation (Nader), forgetting curves & adaptive forgetting (Ebbinghaus; Bjork), cognitive maps (Tolman; O'Keefe & Nadel), preparedness & innate releasing mechanisms (Seligman; Tinbergen, Lorenz), constructive episodic simulation (Schacter & Addis).

The same convergence argument as the other docs applies — a write-side reasoned out from "store the bare minimum essential" landing on gist/verbatim, prediction-error encoding, and complementary learning systems is a good sign the abstraction is sound. And the same caveat: these attributions are from memory; verify terms, originators, and dates against primary sources before anything formal. The ideas are solid; the citations are owed.
