# Cognitive Foundation Phase 0 — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the three ODD-planner foundation bugs that block goal-beacons + a drive-biased cull: chains that collapse when a shelf empties, no terminal revalidation, and a prepotency cull that doesn't cull by drive.

**Architecture:** All changes are in the ODD decision path (`OddSystem` + the `DriveCatalog`/`DriveGraph`/`ActivityCatalog` tables it reads). We extract the testable decision logic into pure static helpers (matching the existing `ShouldSwitchCommitment`/`PrepotencyGate`/`DesperationFactor` pattern) and unit-test those, then wire them in. Integration is verified by a soak chain-count + cull-count metric, because `OddSystem.Decide` has no unit harness today (the audit's finding).

**Tech Stack:** C# / .NET 8, Godot.NET.Sdk; xUnit in `Headless/Sim.MemoryTests`; the CQRS engine (`SimWorld`/`EventBus`/intent registries).

## Global Constraints

- **Spec:** `docs/superpowers/specs/2026-06-23-cognitive-foundation-phase0-design.md`. **Audit:** `docs/cognitive_layer_audit.md`.
- **F3's drive-model edit is approved** (2026-06-23): hunger/energy/fear cull only `SocialDef`, NOT `GoodsDef`/`CoinDef` (those are instrumental to the food chain).
- **TDD, frequent commits.** Each task: failing test → red → implement → green → commit.
- **Run tests:** `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj` (env `DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2` only for soak/arena tests; the unit tests here need no data).
- **Do NOT trust the existing green suite as proof** — the decision path is untested (audit §2.4). New tests must assert the *behaviour*, not helper math in isolation.
- **F4 (MEANINGS seed) is deferred to Phase 1** — see the spec; recognition-generalization is entangled with the `Interpret` rework.
- **Sequencing:** Task 1 (F1) → Task 2 (F2) → Task 3 (F3a data) → Task 4 (F3b cull). Branch `the-threshold` (already a feature branch).

---

### Task 1: F1 — Chains propagate (keep stock-gated mid-chain links in the pool)

**Files:**
- Modify: `Assets/Sim/Engine/Units/OddSystem.cs` — `GatherAds` (the `ads.RemoveAll` at ~`:427-428`); the roots-building loop (~`:254-263`); add a pure helper.
- Test: `Headless/Sim.MemoryTests/OddRootEligibilityTests.cs` (new).

**Interfaces:**
- Produces: `public static bool OddSystem.IsRootEligible(double coin, double saleCost, bool larderGated, double larder, bool inStock)` — true if an ad may be a *root* (a thing to do now). Stock/larder/coin gate root-eligibility only; the ad stays in the pool regardless (for `Enables` propagation).

- [ ] **Step 1: Write the failing test**

```csharp
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class OddRootEligibilityTests
    {
        // A stock-empty sale ad is NOT root-eligible (don't pursue it standalone)...
        [Fact]
        public void StockEmptySaleAd_NotRootEligible()
            => Assert.False(OddSystem.IsRootEligible(coin: 10, saleCost: 1, larderGated: false, larder: 0, inStock: false));

        // ...but a stocked, affordable one IS.
        [Fact]
        public void StockedAffordableSaleAd_IsRootEligible()
            => Assert.True(OddSystem.IsRootEligible(coin: 10, saleCost: 1, larderGated: false, larder: 0, inStock: true));

        // Broke agent: a coin-sink ad is not a root.
        [Fact]
        public void Broke_SaleAd_NotRootEligible()
            => Assert.False(OddSystem.IsRootEligible(coin: 0, saleCost: 1, larderGated: false, larder: 0, inStock: true));

        // Larder-empty home meal is not a root.
        [Fact]
        public void EmptyLarder_HomeMeal_NotRootEligible()
            => Assert.False(OddSystem.IsRootEligible(coin: 0, saleCost: 0, larderGated: true, larder: 0, inStock: true));

        // Non-sale, non-larder ad (e.g. Work) is always root-eligible.
        [Fact]
        public void PlainAd_IsRootEligible()
            => Assert.True(OddSystem.IsRootEligible(coin: 0, saleCost: 0, larderGated: false, larder: 0, inStock: true));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj --filter OddRootEligibilityTests`
Expected: FAIL — `OddSystem.IsRootEligible` does not exist.

- [ ] **Step 3: Add the pure helper** (in `OddSystem.cs`, beside the other pure statics like `ShouldSwitchCommitment`)

```csharp
/// A sale/larder/coin gate is ROOT-eligibility only — the ad stays in the pool for Enables
/// propagation even when it can't be pursued standalone this tick. saleCost = SaleUnits*SalePrice
/// (0 if not a coin-sink). Pure — unit-tested.
public static bool IsRootEligible(double coin, double saleCost, bool larderGated, double larder, bool inStock)
{
    if (!inStock) return false;                 // F1: stock gates root-eligibility, not pool membership
    if (saleCost > 0 && coin < saleCost) return false;
    if (larderGated && larder <= 0) return false;
    return true;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj --filter OddRootEligibilityTests`
Expected: PASS (5/5).

- [ ] **Step 5: Wire it in — stop removing stock-gated ads from the pool**

In `GatherAds`, change the terminal removal (`~:427-428`) so `SaleStockAvailable` no longer removes from the pool — only `PreconditionsMet` (hours/holiday/keeper) does:

```csharp
// BEFORE:
// ads.RemoveAll(a => !ActivityCatalog.PreconditionsMet(a.Spec, adHour, adHoliday, isKeeper)
//                    || !SaleStockAvailable(a));
// AFTER (stock no longer culls the pool — it gates roots below, F1):
ads.RemoveAll(a => !ActivityCatalog.PreconditionsMet(a.Spec, adHour, adHoliday, isKeeper));
```

Then replace the roots-building gate (`~:259-262`) to use the helper:

```csharp
// BEFORE:
// bool coinSink = spec != null && spec.SaleReliefAxis >= 0 && spec.SalePrice > 0;
// if (coinSink && coin < spec.SaleUnits * spec.SalePrice) continue;
// if (spec != null && spec.LarderGated && _larder.Get(ad.Building) <= 0) continue;
// roots.Add(vi);
// AFTER:
double saleCost = spec != null && spec.SaleReliefAxis >= 0 && spec.SalePrice > 0
    ? spec.SaleUnits * spec.SalePrice : 0;
bool larderGated = spec != null && spec.LarderGated;
double larder = larderGated ? _larder.Get(ad.Building) : 1;
if (!IsRootEligible(coin, saleCost, larderGated, larder, SaleStockAvailable(ad))) continue;
roots.Add(vi);
```

- [ ] **Step 6: Build to confirm it compiles**

Run: `dotnet build Assets/Sim/... ` (or the project that compiles `OddSystem.cs`; confirm no errors).
Expected: build succeeds.

- [ ] **Step 7: Commit**

```bash
git add Assets/Sim/Engine/Units/OddSystem.cs Headless/Sim.MemoryTests/OddRootEligibilityTests.cs
git commit -m "fix(odd): F1 keep stock-gated mid-chain links in the pool so chains propagate"
```

---

### Task 2: F2 — Terminal revalidation before commit

**Files:**
- Modify: `Assets/Sim/Engine/Units/OddSystem.cs` — `Decide`, between `Traverse` (~`:271`) and the `IntentSetIntent` publish (~`:353-366`).
- Test: extend `Headless/Sim.MemoryTests/OddRootEligibilityTests.cs` (the same `IsRootEligible` is the revalidation predicate — F2 reuses it, so the unit coverage already exists; add the wiring assertion via the soak metric in Task 5-adjacent work).

**Interfaces:**
- Consumes: `OddSystem.IsRootEligible` (Task 1).
- Produces: revalidation in `Decide` — the published winner is always a currently-root-eligible verb.

- [ ] **Step 1: Implement revalidation at selection**

After `int winner = OddTree.Traverse(buffer, n);` and before building/publishing the intent, re-check the chosen verb against the *current* tick and fall to the next-best valid root if stale. Add:

```csharp
// F2: terminal revalidation. Traverse returns a root verb; re-validate its preconditions against
// THIS tick (stock/larder/coin/hours can have changed since Build) and drop to the next-best valid
// root, else Object Zero. Reuses IsRootEligible (the same gate that built the root set).
winner = RevalidateWinner(winner, verbs, verbAd, verbScore, roots, coin);
```

Add the helper (instance method, near `Decide`):

```csharp
int RevalidateWinner(int winner, List<ActivityKind> verbs, List<Ad> verbAd, List<double> verbScore,
                     List<int> roots, double coin)
{
    if (winner < 0) return winner;
    if (RootValidNow(verbAd[winner], coin)) return winner;
    // stale — pick the highest-scoring root that is still valid now
    int best = -1; double bestScore = double.NegativeInfinity;
    for (int i = 0; i < roots.Count; i++)
    {
        int vi = roots[i];
        if (!RootValidNow(verbAd[vi], coin)) continue;
        if (verbScore[vi] > bestScore) { bestScore = verbScore[vi]; best = vi; }
    }
    return best;   // -1 => caller falls to Object Zero (Idle/Wander), already handled
}

bool RootValidNow(Ad ad, double coin)
{
    var spec = ad.Spec;
    double saleCost = spec != null && spec.SaleReliefAxis >= 0 && spec.SalePrice > 0
        ? spec.SaleUnits * spec.SalePrice : 0;
    bool larderGated = spec != null && spec.LarderGated;
    double larder = larderGated ? _larder.Get(ad.Building) : 1;
    return IsRootEligible(coin, saleCost, larderGated, larder, SaleStockAvailable(ad));
}
```

> Note for the implementer: confirm the existing winner==-1 path already falls to Object Zero / a bare return (it does per the I3 guard in commit `60fb05ea3`); RevalidateWinner returning -1 must hit that same path. If `winner` indexes `verbs`/`verbAd` differently than assumed, align the indexing — `winner` is a verb index into `verbs` (it came from `OddTree.Traverse` over the root-seeded buffer; map the buffer action back to the verb index as the existing code does at the publish site).

- [ ] **Step 2: Build + run the existing suite**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: PASS (no regressions; `IsRootEligible` already covered).

- [ ] **Step 3: Commit**

```bash
git add Assets/Sim/Engine/Units/OddSystem.cs
git commit -m "fix(odd): F2 revalidate the chosen winner before commit (drop to next-best/Object Zero if stale)"
```

---

### Task 3: F3a — Drive-model data: cull targets = social only; expose HardCullTargets

**Files:**
- Modify: `Assets/Sim/Systems/DriveCatalog.cs` — `DeficiencyGates`, `HungerGates` (~`:69-85`): drop `GoodsDef`/`CoinDef` HardCull targets.
- Modify: `Assets/Sim/Systems/DriveGraph.cs` — add `HardCullTargets`.
- Modify: `Assets/Sim/Systems/ActivityCatalog.cs` — add `Spec.ServesAxis` (`~:58`); author it on the social specs (`Socialize:243`, `Visit:279`, `Gossip:264`, `Chat:369`).
- Test: `Headless/Sim.MemoryTests/DriveGraphTargetsTests.cs` (new).

**Interfaces:**
- Produces: `DriveGraph.HardCullTargets` (`int[]`, the axes any deficiency hard-culls — `{NeedAxis.SocialDef}` after this change); `ActivityCatalog.Spec.ServesAxis` (`int`, a `NeedAxis` or −1).

- [ ] **Step 1: Write the failing test**

```csharp
using DaggerfallWorkshop.Sim;
using System.Linq;
using Xunit;

namespace Sim.MemoryTests
{
    public class DriveGraphTargetsTests
    {
        [Fact]
        public void HardCullTargets_IsSocialOnly_NotGoodsOrCoin()
        {
            Assert.Contains(NeedAxis.SocialDef, DriveGraph.HardCullTargets);
            Assert.DoesNotContain(NeedAxis.GoodsDef, DriveGraph.HardCullTargets);   // instrumental to hunger
            Assert.DoesNotContain(NeedAxis.CoinDef,  DriveGraph.HardCullTargets);   // instrumental to hunger
        }

        [Fact]
        public void HardCullSources_StillIncludeTheDeficiencyPoles()
        {
            Assert.Contains(NeedAxis.Hunger,    DriveGraph.HardCullSources);
            Assert.Contains(NeedAxis.EnergyDef, DriveGraph.HardCullSources);
            Assert.Contains(NeedAxis.Fear,      DriveGraph.HardCullSources);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj --filter DriveGraphTargetsTests`
Expected: FAIL — `DriveGraph.HardCullTargets` does not exist.

- [ ] **Step 3: Drop goods/coin from the gate edges** (`DriveCatalog.cs`)

```csharp
// DeficiencyGates: was {Social, Goods, Coin} HardCull. F3: social only (goods/coin are
// instrumental to hunger — the work->buy->eat chain needs them).
static readonly GateEdge[] DeficiencyGates =
{
    new GateEdge { Target = NeedAxis.SocialDef, Kind = GateKind.HardCull },
};

// HungerGates: social HardCull + the desperation edge to Fear (unchanged).
static readonly GateEdge[] HungerGates =
{
    new GateEdge { Target = NeedAxis.SocialDef, Kind = GateKind.HardCull },
    new GateEdge { Target = NeedAxis.Fear,      Kind = GateKind.DesperationGraded },
};
```

- [ ] **Step 4: Add `HardCullTargets` to `DriveGraph`**

In `DriveGraph`, alongside `HardCullSources`, collect the HardCull edge *targets*:

```csharp
/// Per the gate table, the axes that ANY deficiency hard-culls (the discretionary/growth set).
/// F3's by-axis cull removes ads whose ServesAxis is in here. Adding a HardCull edge auto-extends it.
public static readonly int[] HardCullTargets;
```

In the static ctor, while walking the gate edges, also accumulate a de-duplicated set of HardCull targets:

```csharp
var cullTargets = new SortedSet<int>();
// ... inside the existing edge loop, where Kind == HardCull:
//     cullTargets.Add(tgt);
// after the loop:
HardCullTargets = cullTargets.ToArray();
```

(Place `cullTargets.Add(tgt)` in the same branch that currently sets `culls = true` for a HardCull edge.)

- [ ] **Step 5: Add `ServesAxis` to the Spec + author it**

In `ActivityCatalog.Spec` (after the `Prepotent` line `~:58`):

```csharp
public int ServesAxis = -1;   // the gated drive this ad serves (a NeedAxis), or -1. F3: the by-axis prepotency key.
```

Author `ServesAxis = NeedAxis.SocialDef` on the four social-leisure specs — add it to the existing initializer lines:
- `Socialize` (`~:248` block): add `ServesAxis = NeedAxis.SocialDef,`
- `Visit` (`~:285` block): add `ServesAxis = NeedAxis.SocialDef,`
- `Gossip` (`~:264` block): add `ServesAxis = NeedAxis.SocialDef,`
- `Chat` (`~:369` block): add `ServesAxis = NeedAxis.SocialDef,`

(Leave `Buy`/`Beg`/`Steal`/`EatHome`/`Work` with the default −1 — they are not cull targets.)

- [ ] **Step 6: Run to verify it passes**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj --filter DriveGraphTargetsTests`
Expected: PASS (2/2). Then run the full file to confirm no DriveGraph regressions.

- [ ] **Step 7: Commit**

```bash
git add Assets/Sim/Systems/DriveCatalog.cs Assets/Sim/Systems/DriveGraph.cs Assets/Sim/Systems/ActivityCatalog.cs Headless/Sim.MemoryTests/DriveGraphTargetsTests.cs
git commit -m "feat(drive): F3a cull targets = social only (goods/coin instrumental); expose HardCullTargets + Spec.ServesAxis"
```

---

### Task 4: F3b — Prepotency culls by axis (gate-removal + graded weight), retire the bool

**Files:**
- Modify: `Assets/Sim/Engine/Units/OddSystem.cs` — `incumbentLeisure` (~`:217`), the V-scoring loop (~`:236-251`), the `V` gate (~`:649`); add a pure helper.
- Modify: `Assets/Sim/Systems/ActivityCatalog.cs` — remove the now-dead `Prepotent` field (and its uses) **only after** OddSystem stops reading it.
- Test: `Headless/Sim.MemoryTests/PrepotencyByAxisTests.cs` (new).

**Interfaces:**
- Consumes: `DriveGraph.HardCullTargets`, `Spec.ServesAxis` (Task 3).
- Produces: `public static bool OddSystem.IsHardCullTarget(int servesAxis)`; the by-axis cull in `Decide`.

- [ ] **Step 1: Write the failing test**

```csharp
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.MemoryTests
{
    public class PrepotencyByAxisTests
    {
        [Fact]
        public void SocialAd_IsHardCullTarget()
            => Assert.True(OddSystem.IsHardCullTarget(NeedAxis.SocialDef));

        [Fact]
        public void GoodsAd_IsNotHardCullTarget()    // Buy serves goods — must NEVER be culled (food chain)
            => Assert.False(OddSystem.IsHardCullTarget(NeedAxis.GoodsDef));

        [Fact]
        public void NonGatedAd_IsNotHardCullTarget()
            => Assert.False(OddSystem.IsHardCullTarget(-1));
    }
}
```

> The full behavioural assertion — "maxed hunger removes a Socialize ad from the market but keeps Buy" — is verified by the soak cull-count metric (Task 5 below), since `Decide`'s pool-building has no unit harness. This unit test pins the *key predicate* that decides it.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj --filter PrepotencyByAxisTests`
Expected: FAIL — `OddSystem.IsHardCullTarget` does not exist.

- [ ] **Step 3: Add the predicate**

```csharp
/// True if an ad's ServesAxis is a prepotency hard-cull target (a discretionary/growth drive a
/// deficiency suppresses). Pure — unit-tested. Drives off the DriveGraph edge table (F3).
public static bool IsHardCullTarget(int servesAxis)
{
    if (servesAxis < 0) return false;
    var t = DriveGraph.HardCullTargets;
    for (int i = 0; i < t.Length; i++) if (t[i] == servesAxis) return true;
    return false;
}
```

- [ ] **Step 4: Re-key `incumbentLeisure` and the V gate off `ServesAxis`; add the hard-cull removal**

`incumbentLeisure` (`~:217`):

```csharp
// BEFORE: bool incumbentLeisure = incumbentSpec != null && incumbentSpec.Prepotent;
bool incumbentLeisure = incumbentSpec != null && IsHardCullTarget(incumbentSpec.ServesAxis);
```

`V`'s gate (`~:649`):

```csharp
// BEFORE: * (s.Prepotent ? c.Prepotency : 1.0)
* (IsHardCullTarget(s.ServesAxis) ? c.Prepotency : 1.0)
```

In the V-scoring loop, add the **hard-cull gate-removal** — when prepotency pressure is at/over threshold (`c.Prepotency <= 0`, which `PrepotencyGate` returns past the hysteresis threshold), drop cull-target ads from the pool entirely. Insert right after `double s = V(ad, sc);` (`~:239`), before the `s <= 0 && Enables` check:

```csharp
// F3 hard cull: a loud deficiency REMOVES discretionary (social) ads from the market — not just
// down-weights them. c.Prepotency <= 0 means the loudest hard-cull source crossed the threshold.
if (c.Prepotency <= 0 && IsHardCullTarget(ad.Spec.ServesAxis)) continue;
```

- [ ] **Step 5: Run the predicate test + full suite**

Run: `dotnet test Headless/Sim.MemoryTests/Sim.MemoryTests.csproj`
Expected: `PrepotencyByAxisTests` PASS (3/3); no regressions.

- [ ] **Step 6: Remove the dead `Prepotent` field**

Now that nothing reads `Spec.Prepotent`, delete the field (`ActivityCatalog.cs:58`) and the `Prepotent = true,` initializers on `Socialize`/`Visit`. Build to confirm no remaining references.

Run: `grep -rn "Prepotent" Assets/Sim` → expect zero hits. `dotnet build` → succeeds.

- [ ] **Step 7: Commit**

```bash
git add Assets/Sim/Engine/Units/OddSystem.cs Assets/Sim/Systems/ActivityCatalog.cs Headless/Sim.MemoryTests/PrepotencyByAxisTests.cs
git commit -m "feat(odd): F3b prepotency culls by axis (hard-remove social leisure, keep instrumental goods/coin); retire Prepotent bool"
```

---

### Task 5: Soak verification — chain-count + cull-count metrics

**Files:**
- Modify: `Assets/Sim/Engine/EngineSoak.cs` — add two counters to the capture: chains-traversed (agents whose chosen action's tree had depth > 1) and social-ads-culled (hunger-loud agents with a removed social ad).
- (No new unit test; this is the integration check the audit asked for.)

**Interfaces:**
- Consumes: the live `OddSystem` decisions over a soak.

- [ ] **Step 1: Add the metrics to the soak capture**

In `EngineSoak.cs`, where per-tick/per-agent decision data is captured, increment a `chainsTraversed` counter when an agent's winning tree node had children folded into it (expose a lightweight signal from `OddSystem` — e.g. a static `ConcurrentDictionary<EntityId, bool> LastDecisionWasChain` set in `Decide` when `winner`'s buffer node had `PropagatedScore > 0`, mirroring the existing `Snapshots`/`SnapshotWatch` observability statics). Aggregate + print at the soak summary.

> Implementer: follow the existing `OddSystem.SnapshotEnabled`/`Snapshots` pattern (`OddSystem.cs:567-575`) for the observability hook — gate it behind a static bool so it's free when off.

- [ ] **Step 2: Run a short soak and record the numbers**

Run (the project's soak entrypoint, e.g.):
`DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet run --project Headless/Sim.Host -- --soak <small-walled-town> <ticks>`
Expected: **chains-traversed > 0** where shops stock (F1 working — was 0/337); **social-ads-culled > 0** under hunger while **Beg/Buy are still chosen** by hungry agents (F3 working); population stable (no F2 regression).

- [ ] **Step 3: Commit**

```bash
git add Assets/Sim/Engine/EngineSoak.cs Assets/Sim/Engine/Units/OddSystem.cs
git commit -m "test(odd): F1/F3 soak metrics — chain-count + social-cull-count"
```

---

## Self-Review

- **Spec coverage:** F1 (Task 1) ✓, F2 (Task 2) ✓, F3a+F3b (Tasks 3–4) ✓, soak metrics (Task 5) ✓. F4 explicitly deferred to Phase 1 with rationale (recognition-generalization). Phase 1/2 deferred per spec.
- **Placeholder scan:** implementation steps carry real before/after code. Two steps name the executor's judgment (F2 winner-indexing alignment; Task 5 soak entrypoint) — these are *codebase facts to confirm against the real ctor/CLI*, not vague TODOs; each says exactly what to verify.
- **Type consistency:** `IsRootEligible`/`RootValidNow`/`IsHardCullTarget` signatures are consistent across Tasks 1–4; `DriveGraph.HardCullTargets` (Task 3) is consumed by `IsHardCullTarget` (Task 4); `Spec.ServesAxis` (Task 3) is read by the V gate + cull (Task 4). `c.Prepotency` already exists (`ScoreContext.Prepotency`, set from `PrepotencyGate`).
- **Risk note:** F3b changes soak behaviour (hunger now hard-removes social leisure). Expect the begging/idle-under-hunger distribution to shift; that's intended. The F1 behaviour-change (Work valued at an empty shop) is the headline. Run the soak (Task 5) before declaring done.
