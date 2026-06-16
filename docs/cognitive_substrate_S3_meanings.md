# S3 — MEANINGS: the semantic store + consolidation

**Goal.** Give `interpret()` (S1) a real substrate: a per-agent **MEANINGS store** of
category/fact nodes (prototype + predicted atoms + valence + confidence) that recognition,
valence, and trust are *looked up* from — fed by **consolidation** of episodes into facts during
sleep. Then valence stops being the dossier scalar S1 placeholdered; the dossier becomes a
*delta* over a recognised category, and **learned categories drive behavior** (this keeper is
stingy; beggars are low-status; storms mean shelter).

**Atoms grounding.** Memory doc: *"the semantic store is the compression dictionary, everything
else is a diff against it"*; `CategoryNode {prototype, predicted (variance-gated running stats),
valence, confidence, INNATE}`; recognition = nearest prototype above threshold (else novel →
surprise); the **delta-record** (`categoryRef + deltaBag + strength`); write path = detect
surprise (row 2) → write delta (row 5); **consolidation** in sleep (FOLD/RE-DIFF/MINT/DECAY/
SETTLE); confident-false-memory as reconstruction; bounded stores; an **innate seed** so tick one
works. Determinism: fixed-point running stats, key-order, `hash` draws.

**Makes emergent.** Recognition/valence/trust become lookups (S1 real); the dossier compresses to
deltas over categories; **stigma/sympathy** (S4) has a category to attach to (a "beggar"/"keeper"
node with learned valence); "this town learned X" becomes possible.

**Scope warning (proto discipline).** The memory doc is large; **do not build all of it.** S3
builds the *minimum that makes `interpret()` real*: CategoryNode + recognition + learned valence
+ a minimal consolidation (awake stat-fold + sleep decay). The full delta-bag/eviction/split/
false-memory/two-operator-surprise machinery is **explicitly deferred** until a proto needs it.

---

## Current state (anchors)

- `MemoryRegistry` (`Registries/MemoryRegistry.cs`): a **raw episodic ring** — `MemoryData` =
  newest-first `List<MemoryEntry>` (≤32), `MemoryEntry {Tick, Kind, Other, Building}`. No
  categories, no facts, no consolidation. Written by `SocialSystem`/`RequestSystem` (Met/
  Help/Refused). **This is the episodic store; S3 adds the semantic store above it.**
- S1's `interpret()` sets `Valence = dossier.Regard` as a **placeholder** — there is no semantic
  substrate to look up, which is the gap S3 fills.
- No "signature" concept yet. In the town an entity's signature ≈ its **identity attributes**
  (`IdentityData`: Race/CareerIndex/FactionId) + role (`Residency.Role`: Keeper/Resident/Guard).
  So the first useful categories are *roles*: keeper, resident, guard, beggar (Doing Beg).

## The gap

Memory is episodes only — no compression dictionary, no generalisation. So `interpret()` can't
look anything up (valence is the per-entity dossier scalar), agents can't *generalise* ("keepers
refuse me", "beggars are X"), and S4's conscience/stigma has no category to charge.

---

## Design

### S3.1 — The MEANINGS store + recognition + interpreted valence

```csharp
// Assets/Sim/Registries/MeaningsRegistry.cs (new) — per-agent semantic store
public sealed class CategoryNode
{
    public int Signature;        // role/career/faction key the prototype matches (proto: role)
    public double Valence;       // appetitive/aversive charge, running mean (fixed-point)
    public double Confidence;    // 0..1, running stat
    public bool Innate;          // seed node, decay/evict-immune
    // predicted-atoms running stats deferred (proto: valence+confidence suffice)
}
public sealed class MeaningsData { public List<CategoryNode> Nodes; }  // bounded, key-sorted
```

`recognize(other)` = match `other`'s signature (role/career) to the nearest node above threshold
(else *novel* → S2 surprise). `interpret()` (S1) then sets `Valence = recognize(other).Valence ⊕
dossierDelta(other)` and `Trust = node.Confidence` — i.e. **what I expect of your kind, adjusted
by what I know of you specifically.** The dossier (`RelationData`) becomes the *delta* over the
category (the memory doc's delta-record idea, depth-1: a scalar delta, not a full atom bag yet).

**Innate seed** (a tiny static table, like `DriveDefs`): seed nodes for the basic roles
(person/keeper/resident) at neutral/low confidence so tick one interprets; learning accretes.

### S3.2 — Consolidation: episodes → facts

- **Awake fold (cheap):** on each recognised interaction (S2 residual / memory write), update the
  matched node's `Valence`/`Confidence` running stats (variance-gated mean). This is the memory
  doc's "FOLD is awake, at recognition." So repeated refusals-by-keepers nudge the *keeper* node
  aversive; repeated alms nudge it appetitive — a **learned generalisation**.
- **Sleep decay (bounded):** during `Sleep`, low-strength/low-confidence learned nodes decay
  toward the seed (forgetting is a feature; bounds the store). INNATE seed immune.
- **Surprise-gated encoding (uses S2):** only sufficiently *surprising/high-arousal* episodes fold
  strongly (the encode gate) — a familiar pattern barely moves the node; a shock etches it.

Deferred (note, don't build): the delta-bag verbatim store, MINT (clustering novel signatures
into new nodes), SETTLE/split on contradiction, reconstruction/false-memory, the two-operator
surprise split, eviction-under-pressure. Add when a proto demands them.

### S3.3 — Learned categories change behavior

With S1 reading MEANINGS, a learned node *changes choices*: an agent whose *keeper* node has gone
aversive (kept being refused) interprets keepers' venues as less inviting (avoids begging there);
an agent who learned a *generous* individual seeks them. This is the loop closing at the
*category* level, beyond the per-entity dossier — the thing the raw-episode ring could never do.

---

## Staged sub-steps

1. **S3.1** — `MeaningsRegistry`/`CategoryNode` + innate seed + `recognize()`; S1's `interpret()`
   valence becomes `category ⊕ dossier-delta`. (Dossier re-expressed as a delta.)
2. **S3.2** — awake stat-fold on recognised interactions + sleep decay; surprise-gated via S2.
3. **S3.3** — scenario: a learned category (e.g. "keepers are stingy" after repeated refusals)
   measurably shifts interpretation/behavior.

## What it retires

The raw-episode-*only* memory (episodes remain as the consolidation input; the *semantic* layer
is new). S1's placeholder `Valence = dossier.Regard` becomes a real lookup. Sets up S4 (conscience
nodes are MEANINGS nodes).

## Acceptance (per-stage gates: mechanism + invariants)

- **Invariants:** determinism (fixed-point running stats, key-ordered nodes, `hash` tie-breaks —
  the memory doc's A4/A5); MEANINGS single-writer; bounded store (Count stays under cap).
- **Mechanism fires (direction only):** an innate-seeded agent interprets a stranger via its role
  category (recognition works); repeated refusals push the relevant category's valence aversive
  (a fold check); a learned category changes a choice in a scenario.
- **Deferred to tuning:** match threshold/novelty line, fold rates, decay rates, encode-gate θ —
  frozen placeholders.

## Risks / open questions

- **Biggest-scope stage.** The discipline matters most here: build valence+confidence categories
  + fold + decay, *not* the full memory doc. The deferred list above is real — resist it.
- **Signature definition.** Proto signature = role/career/faction. Richer signatures (per-entity
  appearance, learned individual prototypes) are later. Note what counts as "a kind."
- **Determinism of running stats.** Use fixed-point (8.8) or carefully-ordered double folds; the
  memory doc sidesteps cross-machine float drift with fixed-point — match that for MEANINGS.
- **Sequencing.** S3 needs S2's arousal/surprise for the encode gate; don't start S3 before S2's
  `Affects` exists, or the fold has no gate and trains on everything (the memory doc's warning).
