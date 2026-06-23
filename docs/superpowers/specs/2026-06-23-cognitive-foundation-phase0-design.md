# Cognitive Foundation — Phase 0 (Design)

**Date:** 2026-06-23
**Status:** approved (2026-06-23, incl. F3's drive-model correction), ready for plan.
**Depends on:** the 2026-06-23 substrate audit (`docs/cognitive_layer_audit.md`), [[cognitive-substrate-audit]].
**Part of:** completing the cognitive model. Phase 0 = the foundation blockers that must precede the
goal-beacons + drive-biased cull build (Phase 2). Phase 1 (the subjective layer) and Phase 2 are out
of scope here.

## The idea

The audit found the cognitive substrate is built as clean, unit-tested data structures, but the live
wiring is missing or wrong where the architecture's intelligence lives. Before adding "self as a
source" (goal-beacons) and the drive-biased cull, five foundation fixes must land — because **the
cull we want to add has nowhere to live, and the chains it would steer don't propagate.** Each fix is
small, contained, independently testable, and behaviour-affecting in a way we can assert.

The guiding correction throughout: the audit proved the *unit tests don't guard the live decision
path*, so **every fix here ships with a behavioural test that would have caught the bug** — not just
a helper unit test.

## Decisions (locked)

### F1 — Chains propagate: stop culling stock-gated mid-chain links pre-Build

1. **Relocate the stock gate, don't keep removing from the pool.** `GatherAds`'s terminal
   `ads.RemoveAll(... || !SaleStockAvailable(a))` (`OddSystem.cs:427-428`) removes `Buy`/`EatTavern`
   from the pool when a shelf is empty, so `EnabledIndices` (`:556`) can't wire `Work→Buy` and the
   chain collapses (the verified 0/337 root). **Fix:** `SaleStockAvailable` no longer removes from
   the pool. Stock-gated ads stay in `verbs`/`verbIndex` so their `Enables` edges form and propagate.
2. **Stock is enforced at root-eligibility instead.** Extend the existing root filter
   (`OddSystem.cs:259-262`, which already drops coin-unaffordable and larder-empty *roots*) to also
   drop stock-empty *roots*. So a stock-empty `Buy` is never chosen *as a thing to do now*, but
   remains in the tree *as `Work`'s child for propagation* — exactly `what_is_world.md:375` ("the
   buffer includes Enables-descendants not yet afforded… for propagation only").
3. **`PreconditionsMet` (hours/holiday/keeper) still removes from the pool.** Those are structural
   "can't ever do this here," not "temporarily out of stock." (Whether *temporal* gates like shop
   hours should also move to root-only is a noted Phase-1 question, not done here.)

This is a behaviour change: a hungry, employed, broke agent at a shop with empty shelves now values
`Work` (inheriting the discounted meal) instead of idling. The chain *propagates* even when the shelf
is momentarily empty — which is the whole point of lookahead, and the half the economy-supply fix
([[odd-is-the-planner-depth1]]) can't deliver alone.

### F2 — Terminal revalidation before commit

1. **Re-check the winner against the current tick before publishing its intent.** `Decide` currently
   publishes `IntentSetIntent` for `winner` (`OddSystem.cs:271,353-366`) with no re-check, against
   `odd_spec.md:101-109`. **Fix:** before committing, revalidate the chosen root's preconditions
   (hours / stock / larder / affordability) against this tick; if stale, drop it and take the
   next-best valid root, falling through to Object Zero (`Idle`/`Wander`) if none survive.
2. **Scope:** revalidation *at selection* only. Full mid-walk perceptual-staleness (the agent walks
   to a remembered target that's since gone, discovers it on arrival) is the existing re-decide-on-
   arrival behaviour and a Phase-1 refinement — not rebuilt here. F2 is the safety net that pairs
   with F1 so a chain can't commit to a step that's invalid *this* tick.

### F3 — The prepotency cull culls genuine leisure by axis (and surfaces a drive-model fork)

Combines audit fixes 0.2 (a self-state cull *layer*) and 0.4 (gate by axis). **Self-review surfaced a
real tension that needs your call** (⚠️ in decision 2).

1. **The tension.** The naive reading of the audit — "cull `{social, goods, coin}` when a deficiency
   is loud" — **would re-break F1's chain.** `Buy`/`Beg` serve `GoodsDef`, but in this economy
   goods=provisions and coin are **instrumental to hunger** (the `CoinDef`-retirement comment: "the
   honest coin-seeking is hunger → provisions → coin… ODD-tree propagation"). Culling them when
   hungry removes the very `Buy`/`Beg` the food chain needs — and root-gating them instead means a
   starving-but-moneyed agent can never *execute* `Buy`. So hunger must **not** cull the instrumental
   goods/coin drives.
2. **Decision (confirmed 2026-06-23 — edits the drive model).** Hard-cull targets = the
   genuinely **discretionary/growth** drives only: `SocialDef` today (curiosity/play later), **not**
   `GoodsDef`/`CoinDef`. Correct `DriveCatalog`'s `DeficiencyGates`/`HungerGates` to drop the
   `GoodsDef`/`CoinDef` hard-cull targets — those are gated by the chain's affordability + larder/stock
   roots (F1), not by prepotency. This reframes the audit's "starving agent shops/begs freely":
   buying provisions and begging for alms *when starving* are **instrumental and correct** — the real
   cull target is social *leisure*.
3. **Retire the `Prepotent` bool; cull by axis.** Add `Spec.ServesAxis : int` (a `NeedAxis`,
   default −1). Author `ServesAxis = SocialDef` on `Socialize`/`Visit`/`Chat`/`Gossip` (the bool
   currently tags only `Socialize`/`Visit`, missing `Chat`/`Gossip`). `Idle`/`Wander` stay uncalled —
   they're the Object-Zero liveness floor.
4. **`DriveGraph` exposes the hard-cull *targets*** — add `DriveGraph.HardCullTargets` (the union of
   `HardCull` edge targets = `{SocialDef}` after the correction). Adding a gate edge auto-extends the
   cull with no engine edit.
5. **Two-regime prepotency, keyed by `ServesAxis ∈ HardCullTargets`:**
   - **Hard cull (gate-removal, in `GatherAds`):** loudest hard-cull source (`max` over
     `HardCullSources`) ≥ enter-threshold (hysteresis 0.7 enter / 0.6 exit via the incumbent) →
     **remove** ads whose `ServesAxis ∈ HardCullTargets`. A frightened/starving agent stops
     *socializing*, not eating.
   - **Graded weight (soft, in `V`):** below threshold, keep the `1 − loudest/threshold` multiplier,
     applied by `ServesAxis ∈ HardCullTargets` instead of the `Prepotent` bool.

This enforces "every deficiency gates every growth directly" *correctly* (growth = social, not the
instrumental provisioning drives), and gives Phase 2's drive-biased cull a real socket.

### F4 — Install the innate MEANINGS seed at spawn

1. **Call `MemorySeeds.Install` in the live spawn path.** Today it's called only in tests;
   `AgentMemory` constructs an empty `MeaningsStore` (`AgentMemoryRegistry.cs:33,70`), so every agent
   recognizes nothing and writes every percept as novel/255. **Fix:** `AgentMemoryRegistry.Seed(id)`
   installs the species seed into the agent's `MeaningsStore` at spawn.
2. **Seed the kinds agents actually perceive.** Extend `MemorySeeds` from scaffolding to the real
   slopfall percept vocabulary: the `Kind`/`Role`/`Race` signature prototypes an agent meets
   (civilian roles, keeper, guard, the creature kinds) as `INNATE` category nodes with
   role-appropriate baseline valence (neutral civilians, aversive monsters). Content stays a frozen
   table beside `Ads`/`DriveDefs` (the doc's `MemorySeeds` static).
3. **Scope:** wiring + a minimal-but-real seed. Tuning the seed valences and adding learned-category
   accretion behaviour is downstream; F4 only makes recognition non-empty on tick one.

## Architecture

```
F1  GatherAds: RemoveAll(!PreconditionsMet)            // stock no longer removes here
    roots filter (:259-262): + skip stock-empty roots   // stock gated as ROOT, kept in pool
       -> Buy stays as Work's child -> Propagate lifts Work -> chain works

F2  Decide: winner = Traverse(...)
       -> Revalidate(winner, now)  ? publish IntentSetIntent : next-best / ObjectZero

F3  ActivityCatalog.Spec += ServesAxis
    DriveGraph += HardCullTargets
    GatherAds: pressure = max(needs[HardCullSources])
       if pressure >= cullThreshold(incumbent): RemoveAll(ServesAxis in HardCullTargets)   // HARD cull
    V(): prepotency weight applies when ServesAxis in HardCullTargets                       // GRADED
    (delete Spec.Prepotent + the bool-keyed gate)

F4  AgentMemoryRegistry.Seed(id): MemorySeeds.Install(mem.Meanings, species)
    MemorySeeds: scaffolding -> real Kind/Role/Race prototypes + INNATE valence
```

## Data flow (the chain, post-F1/F2/F3)

```
hungry + employed + broke agent, shop shelves empty:
  GatherAds -> {Work(root), Buy(in pool, NOT a root: stock-empty), EatHome(in pool), Idle, Wander, ...}
            -> prepotency: Socialize/Visit/Chat/Gossip REMOVED (social leisure); Beg/Buy KEPT (instrumental)
  Build     -> Work -> Buy -> EatHome   (edges form: Buy is in the pool, F1)
  Propagate -> Work.Total += decay*(Buy + decay*EatHome)
  Traverse  -> Work (lifted by the meal it funds)
  F2        -> revalidate Work (employed, in hours) -> commit Work
  ... later, coin earned + a shelf restocked -> Buy becomes a root -> buy -> larder -> EatHome
```

## Validation

Each fix ships a **behavioural** test (the kind the audit proved missing), plus helper units:

- **F1 — chain regression (the test that would have caught 0/337).** A `Decide`-level scenario:
  hungry+employed+broke agent; workplace reachable; shop stock = 0. Assert `Work` is chosen **and**
  its tree `Total` > its bare `V(Work)` (propagation lifted it). Flip stock > 0 and afford coin →
  `Buy` becomes a selectable root. (Replace/augment `OddSnapshotTests`' `precond: _ => true` with a
  culling predicate so the tree test exercises the collapse path.)
- **F2 — revalidation.** Winner whose stock drains on the commit tick → next-best chosen, never the
  stale winner; nothing valid → Object Zero.
- **F3 — prepotency by axis (the keystone property).** Maxed hunger → a `Socialize`/`Gossip` ad is
  **removed from the market** (not merely down-weighted), while a fed agent keeps it; **a `Buy`/`Beg`
  ad is NOT culled** (instrumental to the food chain — the regression that guards against re-breaking
  F1); hysteresis boundary (0.7/0.6); `EatHome` never culled. Unit: `ServesAxis ∈ HardCullTargets`
  lookup; `DriveGraph.HardCullTargets = {SocialDef}`.
- **F4 — seed installed.** A freshly-spawned agent `Recognize`s a seeded `Kind`/`Role` signature
  (non-`None` category) on tick one; an unseeded novel signature is still novel. Integration: after a
  few ticks, THINGS writes are not *all* novel/255.
- **Soak sanity (ARENA2):** a short Betony/Gallotale soak still passes (population stable), and the
  begging/idle rate under hunger drops (F3 now suppresses discretionary ads); chain count rises above
  0 where shelves stock (F1). Wire a chain-count + cull-count metric into the soak capture.

## Scope / deferred

- **In:** F1–F4 + their tests + the soak metrics.
- **Deferred (Phase 1):** `Interpret` reading the perceivable atom-bag (prey veto); drive-biased
  *attention* that's actually consumed; fear-completion in interpretation; de-duplicating `Interpret`
  (the `OddSystem` inline copy — a *known* deferral per the learned-valence spec, not accidental);
  reconciling the two meanings stacks; clearing the indoor stale-sensed list; somatic clear-on-zero;
  EVENTS store; THINGS full-percept + arousal.
- **Deferred (Phase 2):** O3 goal-beacons (`AgentContext.Goals`/`GoalBonus`/`BeaconAction`), the
  drive-biased cull, the memory-recall door — "self as a first-class source."
- **Deferred (cross-cutting):** replay-determinism test; the `FearLevel` compositional-smoothing
  decision; re-establishing the deleted drives/fear unit tests; `V` null-guard + `Panic`/`Rest`
  naming (do the null-guard opportunistically when F-work touches `V`).
- **Future drive (noted, user 2026-06-23):** a distinct **wealth / greed** drive — genuinely
  *discretionary* (unlike the instrumental goods/coin), so it *would* be a hard-cull target. When it
  lands, author its `ServesAxis` + add its gate edge; F3's by-axis cull picks it up with no engine
  change. This is *why* F3 keys the cull on the edge table rather than a per-ad bool.

## Sequencing

F4 (independent, smallest) → F1 → F2 (pairs with F1) → F3. F1+F2 are the headline chain fix; F3 is
the keystone cull; F4 unblocks the Phase-1 subjective layer. Each lands as its own committable,
tested change.
