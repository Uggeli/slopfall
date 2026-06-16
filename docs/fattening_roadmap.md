# Fattening roadmap — deepening the 8 systems

The L-stack (`living_world*.md`, L1–L4) made the world **alive**; the cognitive substrate
(`cognitive_substrate*.md`, S1–S4) made it **mindful**. Both landed deliberately *thin* —
depth-1 / proto-subset / "deferred" sections — to get a coherent, tested whole fast. This doc
maps the next chunk: **fattening** those systems toward the full Atoms architecture
(`~/omat/Atoms/docs/what_is_*`) and richer behavior.

It is a map for a design discussion, not a build order yet.

## The organizing principle: enablers, not a flat backlog

Each system has its own "deferred" list, but **fattening is a dependency graph** — a few
**enablers** unlock most of the rest. Map (and sequence) around those, not box-by-box. And keep
the discipline that got us here: **proto behavior-pull** — fatten when a *target behavior*
demands it (pick "a feud that escalates to a killing" or "an apprentice saving to open a shop"
and let it pull exactly the depth it needs across systems), not by exhaustively deepening every
box. Tuning stays a separate pass *after* a coherent chunk lands (don't tune a moving target).

## The four enabler verticals

### V1 — Mind depth (fatten S3 → then S4)
The keystone for cognitive depth. **S3 full MEANINGS** is the gate: episodic→semantic
consolidation during sleep, delta-records + deltaBag, surprise-gated encoding (MAX-for-attention
/ SUM-for-encode), MINT (cluster novel signatures → new categories), SETTLE/split on
contradiction, confident-false-memory (reconstruction), bounded eviction, and richer signatures
(individual-entity prototypes, the four stores PLACES/THINGS/EVENTS/MEANINGS) — the deferred list
in `cognitive_substrate_S3_meanings.md`. That unlocks **S4 learned conscience**: the
altruistic-punishment *installer* (a shunning is a high-arousal episode that consolidates into a
self-referential aversive node — external enforcement → internal voice), plus the deferred S4
pieces: taboo hard-cull + hysteresis, the *sacred upward edge* (martyrs, personality-gated), the
**guilt pole** (deplete, reparative target) + moral injury, the social-standing pole proper, the
shame↔guilt split, moral spirals (scrupulosity).
*Dependency: S4-learned NEEDS S3-consolidation. S1 individual-recognition also wants S3.*

### V2 — Drama (the threat / agency layer)
One new layer that unlocks a whole swath of conflict. **Mobile hostile creatures** (the
wilderness/caravan layer `RegionLoader` defers to v3) + **activating the reserved action verbs**
(`Take`/`Attack`/`Use`/`Open` — already stubbed in `ActionCatalog`). This gives:
- **S2 fear-of-X** — the directed drive built dormant in `cognitive_substrate_S2_emotion.md`
  finally has a target (project via MAX); the affect-spiral pair (panic) becomes testable.
- **S4 crime-as-tag** — `(self, Take, not-mine)` / `(self, Attack, innocent)` charge guilt; the
  action is normal, the *tag* is the conscience valence. Needs the verbs live.
- **Combat** — `HealthSystem` already fires `DeathSimEvent` from `DamageEvent`, and L2 despawn
  handles the aftermath; this is the emitter side.
*Dependency: S2-fear and S4-crime both NEED this layer.*

### V3 — Agency (multi-step planning)
The ODD tree (O2/O3 in `odd_convergence.md` / `decision_architecture.md`): Enables edges,
Kahn's topological order, Propagate, goals/beacons — replacing today's depth-1 argmax. Unlocks
agents with **intentions** (saving up, "meet you at the tavern tonight," chained plans) and the
L4-deferred **person-directed ads** (`Ad.Target`: Visit-a-friend, Ask-a-specific-person) +
reciprocity/obligation ("owes a favor back" — ties to S4 guilt).
*Dependency: goals/intentions NEED the tree; person-directed ads ride on it + S1 valence.*

### V4 — World sanity (the economy, E-track)
The one structural soak `[FAIL]` throughout. A working economy — resident jobs (E1 in
`economy.md`), the **larder/goods-consumption sink** (deferred from `living_world_L1_entropy.md`
§L1.4), price discovery (prices respond to shortage/glut), and the money-supply-collapse fix —
changes *everything downstream*: right now nearly everyone is poor → everyone begs → it distorts
the S2/S3/S4 social and conscience equilibria. **Arguably highest-leverage**, because the other
three verticals are currently measured against a broken baseline.
*Dependency: a healthy social/begging/conscience equilibrium NEEDS this.*

## Per-system depth (behavior-pull; no enabler required)

| System | Thin spot → fatten | Vertical / note |
|---|---|---|
| **L1** decay | flat rates → asymmetric (grudges linger slower than fondness fades) + personality-rest baseline (a warm soul rests slightly positive) | standalone |
| **L2** lifecycle | immigration-only → **reproduction** (libido drive → gestation → kits aging to adulthood → inherited seed); aging effects (skill/personality drift); inheritance-to-heir vs escheat; **generational culture transmission** (parent-seeded memory/conscience — the memory doc's social installer) | transmission uses V1 (S3/S4) |
| **S1** membrane | sense-cadence + agents-only + shallow read → **per-tick full SubjectiveView for all**; **individual recognition** (signature prototypes); sense THINGS/places (affordances) not just agents; trust/credibility; perceptual staleness / false-perception | recognition uses V1 (S3) |
| **S2** emotion | social affects only → **full two-pole `NeedsData` rework** (level pole + target field properly); **affect spirals** (maintenance/runaway + damping); mood vs emotion; arousal as a global; emotion→attention bias into S1's top-K | fear needs V2 |
| **S3** MEANINGS | proto-subset (valence+confidence+fold+decay) → the full memory-doc machinery (above) | = V1 |
| **S4** conscience | seeded begging-shame only → learned installer, taboos, guilt, standing pole, crime | = V1 + V2 |

## Dependency graph

```
V4 economy ───────────────────────────► sane baseline for ALL measurement
V1: S3-full ──► S4-learned (installer needs consolidation)
            └─► S1 individual recognition (richer interpret)
V2: creatures + verbs ──► S2 fear ─┐
                          S4 crime ─┴─► drama/conflict, combat
V3: ODD tree ──► goals/intentions ──► L4 person-directed ads (+ S1 valence)
L2 generational transmission ──► consumes V1 (seeds memory/conscience across generations)
```

## Recommended sequence (a lean, to argue with)

1. **V4 economy first** — unblock the baseline; it's the only standing `[FAIL]` and everything
   else is being judged against it broken.
2. **V1 Mind depth (S3-full → S4-learned)** — the natural continuation of what we just built; the
   consolidation keystone, and it makes conscience *learned* rather than seeded.
3. **V2 Drama (threats + verbs → fear/crime/combat)** — the biggest behavioral expansion; turns a
   peaceful town into one with stakes.
4. **V3 Agency (ODD tree → goals/intentions)** — depth of intention; best once agents have rich
   enough inner state (V1/V2) to plan *around*.

Per-system depth gets pulled in as target behaviors demand, not as a separate phase.

## Discipline (carried forward)

- **Proto behavior-pull.** Don't rebuild the full memory doc / conscience doc at once; pick the
  next target behavior and let it define the next proto-subset (the same way S3/S4 were scoped).
- **Tune after, not during.** All current numbers are frozen placeholders, now coupled across
  L+S; fatten a coherent chunk, *then* one tuning pass on the regional soak.
- **Determinism grows with the systems.** As MEANINGS gains running statistics, adopt the memory
  doc's fixed-point (8.8) discipline (A4/A5) so consolidation stays replay-exact/machine-portable.
- **Test what matters** (the refactor steer): systems wired in + entities use them, not brittle
  magnitude units.

## Open questions for the next-session discussion

1. **Lead vertical** — economy (unblock baseline) vs Mind depth (continue the build)? My lean is
   economy, but Mind is the more exciting continuation.
2. **Scope of the threat layer** — do we introduce creatures/combat soon (unlocks fear + crime),
   or stay civilian-only longer and lean on social conflict for "drama"?
3. **L2 turnover** — is reproduction worth its complexity (decades of aging), or is immigration
   enough until generational culture transmission becomes a goal?
4. **The next S3 proto line** — the full memory doc is large; where do we cut the *next* subset
   (consolidation + MINT first? individual prototypes? false-memory last)?
5. **A driving behavior** — picking one concrete target ("a feud → a killing", "an apprentice
   saves to open a shop", "a famine drives migration") would pull a coherent cross-system slice
   and keep the fattening honest.
