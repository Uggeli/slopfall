# Living World — the aliveness convergence (L0–L4)

Companion to `odd_convergence.md` (the decision-loop track O0–O5) and `economy.md` (the
economy track E0–E4). This doc owns the **aliveness** track: the work that turns a
structurally-stable town into a world where things *happen*. It is grounded in the Atoms
`what_is` series (`~/omat/Atoms/docs/what_is_world.md`, `what_is_memory.md`,
`what_is_drive.md`, `what_is_bunny.md`, `what_is_conscience.md`) — the destination cognitive
architecture the DFU wedge stays vocabulary-compatible with.

## The thesis: our town is a screensaver, and Atoms named the disease

The Atoms **world doc** defines a live world as one that *multiplies feedback loops grounded
in physics* (diffusion, threshold, **decay**), and spells out the contrast explicitly. A
**screensaver** world:

| Atoms screensaver property | DFU status today | Track that fixes it |
|---|---|---|
| no memory→decision feedback (agents ignore what they learn) | **TRUE** — `MemoryRegistry` is written, never read by `OddSystem`/`ActionDiscovery` | L3 |
| no perturbation source (fields settle, agents reach equilibrium) | **TRUE** — needs reach a band by day 1; regard/familiarity climb monotonically | L1 |
| no lifecycle (things accumulate forever; births but no decay/death) | **TRUE** — `DeathSimEvent` fired only by combat, never by age; no aging, no birth | L2 |
| no subjective view (every agent sees one objective truth) | **TRUE** — omniscient town seeding + relation-blind uniform `V` | L3 |
| no Object Zero (agents deadlock with an empty market) | **FALSE** — we have `Idle`/`Wander` liveness floor | — (already good) |

We are **4-of-5**. The soak measured all four independently (saturating regard, frozen
inequality, zero deaths, inert memory). Atoms wrote the diagnosis before we ran the
experiment.

## The two user-named threads collapse into one mechanism

- **"Subjective view of different dudes"** = the Atoms **membrane** + the `interpret()` step.
  An agent is *a private process wrapped in a public molecule*; between raw senses and the
  affordance market sits `interpret()`, which reads personality + state + **memory** to build
  a **SubjectiveView** — same `scent=fox` atom, opposite reading per agent. We have
  `senses → ads → uniform V`: **no `interpret()`, no SubjectiveView, a relation/memory-blind
  scorer.** And the memory doc's load-bearing line — *"the MEANINGS store … **is** the
  interpretation substrate"* — means **subjective view and the memory→decision loop are the
  same task**. Memory is *how* interpretation becomes per-agent. (→ L3, L4)

- **"Lifecycles need turning on"** = the Atoms **entropy** half. Decay is not a feature in
  Atoms; it is the substrate: deficiency poles deplete, *aversion poles reset-on-percept*,
  fields decay toward baseline, memory decays unless refreshed, despawn frees the id. We built
  the **opposite default** — every accumulating quantity is monotonic. Lifecycle (spawn→age→
  death→despawn) is one instance of the general missing law: *nothing in our world can be
  lost, so nothing is at stake.* (→ L1, L2)

## The convergence stages

Ordered by dependency. Each stage is independently shippable, keeps the suite green, and
preserves determinism (this codebase asserts replay-exactness — see `EconomyDeterminismTests`,
`SnapshotTests`).

- **L0 — Substrate scaffolding** (tiny, prereq). A `SatisfactionModel` vocabulary + a
  liveness/death plumbing audit. Folded into L1/L2 as their first sub-step; no standalone doc.
- **L1 — Entropy & satisfaction-models** (`living_world_L1_entropy.md`). Give the monotonic
  quantities decay-toward-baseline. Regard/familiarity satisfaction-model + decay (soak's #1
  social finding). *Foundation — do first; every later mechanism saturates without it.*
- **L2 — Lifecycle & turnover** (`living_world_L2_lifecycle.md`). Aging → mortality → despawn
  with full cross-registry cleanup (incl. the *inbound-reference* problem L3 makes acute), and
  birth/replacement to hold population. *Depends on L1's decay discipline and despawn touches
  Relations/Memory.*
- **L3 — The membrane: SubjectiveView & memory-as-interpretation**
  (`living_world_L3_membrane.md`). Insert an `interpret()` step producing a per-agent
  `SubjectiveView`; fold interactions into the THINGS dossier (`RelationData`); read the
  dossier into `V` *uniformly* (preserving "ad is ad"). The big leap. *Depends on L1 (a
  non-decaying dossier hardens into a fixed point).*
- **L4 — Directed drives** (`living_world_L4_directed_drives.md`). Generalize `RequestSystem`'s
  bespoke alms-seeking (the one existing target-pole-led drive) into a directed-drive
  framework: person-directed ads (`Ad.Target`), "seek my friend / avoid the one who refused
  me." *Depends on L3 (reads the subjective dossier).*

```
L1 (decay) ──► L2 (lifecycle)          death is "hard decay"; despawn scrubs Relations/Memory
   │
   └────────► L3 (membrane) ──► L4 (directed drives)
              memory→V, SubjectiveView   person-directed ads on the dossier
```

## Relation to the other tracks

- **ODD (`odd_convergence.md`)**: we are at **O0 done** (uniform `V`, "ad is ad"). L3 is the
  natural O1+ work — it adds the `interpret()`/percept layer O1 calls for and feeds memory in.
  L4 adds the *person-directed ads* the convergence already anticipated ("persons advertise
  directed actions"). Do **not** build the O2 tree (Enables/Kahn's) for this track — these
  stages live on the depth-1 subset.
- **Economy (`economy.md`)**: the soak's economy fixes (E1 resident-jobs to lift the floor, a
  spending sink to cap the top) are the *circulation* form of aliveness and are tracked there.
  L1 notes the **larder/goods-consumption sink** as the economic instance of the same
  satisfaction-model principle, but the resident-jobs work stays in economy.md E1.

## Discipline (carried from the other tracks)

1. **No tuning until the whole L-stack lands** (extends `no_premature_tuning.md` from one
   system to the whole track). The L1–L4 numbers are **coupled** — regard decay (L1) ×
   relation-gain (L3) × directed-ad ranking (L4) × turnover (L2) jointly set the social
   graph's equilibrium — so tuning any stage alone fits an equilibrium the later stages will
   move. Every number here (decay rates, lifespans, birth rates, regard-from-interaction
   deltas, `RelationGain`, clamps, caps) is a **frozen placeholder** until L4 is in. A
   behavioral soak that looks wrong on a partial stack is **expected and explicitly not acted
   on**. Per-stage acceptance is mechanism-and-invariant only; behavioral equilibrium is a
   whole-stack concern (see *Tuning*).
2. **Pin structure before a proto.** Atoms pinned the drive doc before p1 and the memory doc
   before its proto. Each L-doc pins data structures and the system wiring *first*; behavioral
   re-tuning comes after the mechanism is whole.
3. **Behavioral testing, not mechanism testing** (`decision_architecture.md`). Acceptance is
   (1) invariants over a running town (conservation/liveness/no-stuck), (2) behavioral
   expectations with wide statistical bands, (3) scenario tests in the *full* sim, (4) a thin
   unit layer only for deterministic algorithms. Each L-doc states its acceptance in these
   tiers. Delete brittle magnitude tests rather than re-baselining them.
4. **Determinism is a hard invariant.** Per-tick stochastic choices draw from
   `hash(tick, selfId, …)` (the pattern `SocialSystem.Hash(self.Value, tick)` already uses),
   never a shared mutable stream; registries iterate in key order; new accumulators are
   fixed-bounded. No L-stage may be the reason two machines disagree.

## Tuning: one phase, after the whole stack lands

Stand the entire stack up on placeholder numbers first. **Only once L4 is in** does the
regional soak become the tuning rig — and then the coupled numbers are fit *together*, because
they interact (tuning L1's decay against today's graph is wasted once L3/L4 change what the
graph equilibrates to). Until then, partial-stack soaks are run to **observe, not to tune**; a
wrong-looking equilibrium on an incomplete stack is expected and left alone.

## Whole-stack behavioral targets (the tuning-phase goal — NOT per-stage gates)

The screensaver table, inverted — what the *finished, tuned* stack should settle into. Each is
an equilibrium target for the post-L4 tuning pass, **not** a gate any single stage must hit:

- **L1**: friend-edges and |regard|≥0.95 pair-counts settle to a **plateau** at a sensible
  level instead of climbing monotonically; neglected bonds visibly cool.
- **L2**: population is roughly **stationary** (births ≈ deaths) at the geography-appropriate
  level (recall `regional_economy.md`: decline is a valid outcome); a sane age distribution.
- **L3 / L4**: measurable **homophily** — agents co-locate with and approach friends above
  chance, the disliked below chance; the alms path sustains a healthy help rate.

Each stage's own doc lists only its **per-stage gates** (mechanism wired + invariants +
qualitative-direction scenario checks — the things provable on placeholders without tuning).
When the targets above hold *after* the single post-L4 tuning pass, the world has stopped being
a screensaver in exactly the terms the Atoms world doc defines.
