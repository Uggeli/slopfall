# Cognitive Phase C / L1 — Innate Species Priors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A newborn agent spawns with evolved priors — its **own form** sets how threatened it feels (relative-size `ThreatRead`), and an **innate in-group warmth** is seeded into its learnable category store — so it reads strangers and threats through an innate lens that experience then drifts.

**Architecture:** Two channels. **Threat:** every human carries an honest `Size` atom; `ThreatRead` scales menace by `targetSize / perceiverSize`, so a small civilian dreads a fanged Beast while a big one shrugs it off — disposition emerges from form, no flag. **Social:** `MeaningsStore.SeedInnate` (a wrapper over the existing `AddNode(innate:true)`) + a kind-keyed `InnatePriors` table seeded at spawn via a `SeedPriors` sibling in `AgentMemorySeeding`, feeding `Interpret`'s stranger-read (`RecognizedValence → baseValence`) and learning via `Reinforce`.

**Tech Stack:** C# on **.NET 10** (`net10.0`, modern C#), xUnit, fixed-point `Fixed` math (the Memory subsystem is float-free).

## Global Constraints

- **.NET 10 / modern C#**; `Nullable` disable; match each file's surrounding style. Keep the build at **0 warnings**.
- **Memory subsystem is float-free** — valences/sizes stored as `Fixed`; convert to `double` only in the Engine read (`ThreatRead`).
- **No verdict atoms / formed in the perceiver.** Threat and regard are *reads*. Disposition is the perceiver's own **form** (its `Size`), read live — never a stamped temperament flag.
- **Recognition is strict** (`MeaningsConfig.Default.MatchThresholdRaw = 128` = L1 ≤ 0.5, near-exact). Seed prior prototypes that the runtime signature actually matches — the in-group prior uses the agent's **own** `Perceivable.Signature(self)`.
- **`Size` is the existing neutral `AtomCategory.Form` atom** (`AtomName.Size`); it is non-identity, so stamping it does NOT change `Signature(self)`.
- **The threat veto stays; proximity stays in `NeedsSystem`.** Phase C does NOT touch the fear controller; `Interpret`/`ThreatRead` produce the intrinsic, proximity-free capacity.
- **Soak validates behaviour-SENSE, not byte-determinism** — the parallel soak is nondeterministic run-to-run (memory `validate-sim-by-behavior-sense`). Never diff for equality; judge believable / in-regime.
- **Build/test from `/home/uggeli/slopfall/Headless`.** Confirm the green baseline at execution start (post-Phase-B it is **243** in `Sim.MemoryTests`); each task reports the new count, `Failed: 0`.
- **Don't sweep WIP.** Two pre-existing WIP docs (`docs/what_is_an_atom.md`, the 2026-06-24 M1 spec) are uncommitted in the tree — **`git add` only your task's files**, never `git add -A`/`commit -am`.
- **Commit style:** conventional commits, scope `priors`. End every body with:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

## Dependency map (execute SEQUENTIALLY in the main tree)

```
T1 (Size on humans)      T3 (SeedInnate)          ← independent pure additions
        │                      │
T2 (relative ThreatRead) │     │
        │                      ▼
        │                T4 (InnatePriors + SeedPriors)   ← needs T3
        ▼                      ▼
        └──────────── T5 (behaviour-sense soak) ──────────┘
```

The two channels (T1→T2, T3→T4) are independent, but per the worktree-base lesson from Phase B (`isolation: worktree` branched from the wrong base), **run all tasks sequentially in the main checkout** — do not fan out into isolated worktrees. Order: T1 · T2 · T3 · T4 · T5.

---

## File Structure

**New files:**
- `Assets/Sim/Engine/Units/HumanForms.cs` — per-human-kind form atoms (Size today) — T1.
- `Assets/Sim/Engine/Units/InnatePriors.cs` — the kind-keyed innate-prior table + `PriorTarget` — T4.
- `Headless/Sim.MemoryTests/HumanFormsTests.cs` — T1.
- `Headless/Sim.MemoryTests/SeedInnateTests.cs` — T3.
- `Headless/Sim.MemoryTests/InnatePriorsTests.cs` — T4.

**Modified:**
- `Assets/Sim/Engine/Units/SubjectiveSystem.cs` — `ThreatRead` + `Interpret` (T2).
- `Assets/Sim/Memory/MeaningsStore.cs` — `SeedInnate` (T3).
- `Assets/Sim/Engine/Units/AgentMemoryRegistry.cs` — `SeedInnatePrior` (T4).
- `Assets/Sim/World/AgentMemorySeeding.cs` — `SeedPriors` + the `SeedAgent` call (T4).
- `Assets/Sim/World/TownLoader.cs` — `Spawn` stamps the Size atom (T1).
- `Headless/Sim.MemoryTests/ThreatReadTests.cs` — rewritten for the relative model (T2).

---

## Task 1 (C2): `Size` on human agents

**Files:**
- Create: `Assets/Sim/Engine/Units/HumanForms.cs`
- Modify: `Assets/Sim/World/TownLoader.cs` (`Spawn`, after the Role seed ~line 546)
- Test: Create `Headless/Sim.MemoryTests/HumanFormsTests.cs`

**Interfaces:**
- Produces: `HumanForms.Civilian : (AtomTypeId Type, Fixed Value)[]` = `{ (Size, 0.4) }`. Consumed by `TownLoader.Spawn` (stamp) and, behaviourally, by T2 (`ThreatRead` reads the perceiver's own Size).

- [ ] **Step 1: Write the failing test** — `HumanFormsTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class HumanFormsTests
    {
        [Fact]
        public void Civilian_CarriesAHumanBodySize_SmallerThanBeast()
        {
            var size = HumanForms.Civilian.Single(f => f.Type == AtomName.Size.ToId());
            double s = size.Value.ToDouble();
            Assert.True(s > 0.0 && s < 0.5);   // a human body, smaller than the generic Beast (0.5)
        }
    }
}
```

- [ ] **Step 2: Run it — expect FAIL** (compile: `HumanForms` undefined)

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~HumanFormsTests"`
Expected: FAIL (does not compile).

- [ ] **Step 3a: Create `HumanForms.cs`:**

```csharp
using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>
    /// Per-human-kind FORM atoms a human agent broadcasts — its body Size today (weapons for armed
    /// roles arrive in C/L2). A perceiver reads threat RELATIVE to its own Size (SubjectiveSystem),
    /// so this is also what makes a small civilian dread a Beast. Mirrors CreatureForms. Frozen,
    /// soak-tuned; humans are unarmed (Size only) in L1.
    /// </summary>
    public static class HumanForms
    {
        // A human body, smaller than the generic Beast (0.5), so a civilian reads a Beast as
        // bigger-than-me. Frozen placeholder; the human/Beast size ratio is the fear lever.
        public static readonly (AtomTypeId Type, Fixed Value)[] Civilian =
        {
            (AtomName.Size.ToId(), Fixed.FromDouble(0.4)),
        };
    }
}
```

- [ ] **Step 3b: Stamp it at spawn** — in `TownLoader.cs` `Spawn`, the identity-atom block currently reads:

```csharp
            world.Perceivable.Seed(id, PerceivableAtoms.Kind(EntityKind.CivilianNPC), Fixed.One);
            if (race >= 0) world.Perceivable.Seed(id, PerceivableAtoms.Race(race), Fixed.One);
            world.Perceivable.Seed(id, PerceivableAtoms.Role(role), Fixed.One);

            // Empty private memory — the agent learns categories from perception over time.
            world.AgentMemory.Seed(id);
```
Insert the Size stamp before `world.AgentMemory.Seed(id);`:

```csharp
            world.Perceivable.Seed(id, PerceivableAtoms.Kind(EntityKind.CivilianNPC), Fixed.One);
            if (race >= 0) world.Perceivable.Seed(id, PerceivableAtoms.Race(race), Fixed.One);
            world.Perceivable.Seed(id, PerceivableAtoms.Role(role), Fixed.One);
            // Phase C: an honest body Size — a perceiver reads threat RELATIVE to its own size.
            foreach (var form in HumanForms.Civilian)
                world.Perceivable.Seed(id, form.Type, form.Value);

            // Empty private memory — the agent learns categories from perception over time.
            world.AgentMemory.Seed(id);
```

- [ ] **Step 4: Run the suite — expect PASS**

Run: `cd /home/uggeli/slopfall/Headless && DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0` (baseline + 1).

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Engine/Units/HumanForms.cs Assets/Sim/World/TownLoader.cs Headless/Sim.MemoryTests/HumanFormsTests.cs
git commit -m "$(cat <<'EOF'
feat(priors): humans carry an honest body Size atom at spawn (C2)

HumanForms.Civilian = {Size 0.4}; TownLoader.Spawn stamps it. So every human
agent has a perceivable body size — the input the relative-size threat read
(C1) uses to make a small civilian dread a Beast a big one would shrug off.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2 (C1+C3): relative-size `ThreatRead`

**Files:**
- Modify: `Assets/Sim/Engine/Units/SubjectiveSystem.cs` (`ThreatRead` ~71-100; `Interpret`'s threat block ~115-118)
- Test: rewrite `Headless/Sim.MemoryTests/ThreatReadTests.cs`

**Interfaces:**
- Produces: `public static double ThreatRead(AtomBag bag, double perceiverSize, out double threatValence)` — menace scaled by `targetSize / perceiverSize`; mass-menace fires only when the target is bigger. `Interpret` reads `self`'s `Size` (default 1.0) and passes it.

- [ ] **Step 1: Rewrite the failing tests** — replace the body of `ThreatReadTests.cs` (keep the `using`s + the `Bag` helper) with the relative-model tests:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class ThreatReadTests
    {
        static AtomBag Bag(params (AtomName name, double value)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(a.name.ToId(), Fixed.FromDouble(a.value))).ToList());

        [Fact]
        public void Weapon_ReadsGradedThreat_AndAversiveValence()
        {
            // a small perceiver (0.4) facing a fanged 0.5 body
            double t = SubjectiveSystem.ThreatRead(Bag((AtomName.Fanged, 1.0), (AtomName.Size, 0.5)), 0.4, out double v);
            Assert.True(t > 0.0 && t <= 1.0);
            Assert.True(v < 0.0);                                   // a thing that scares you reads aversive
        }

        [Fact]
        public void Veto_IsTheMaxCue_FangedDominatesFast()
        {
            double both = SubjectiveSystem.ThreatRead(Bag((AtomName.Fanged, 1.0), (AtomName.Fast, 1.0), (AtomName.Size, 0.5)), 0.4, out _);
            double fastOnly = SubjectiveSystem.ThreatRead(Bag((AtomName.Fast, 1.0), (AtomName.Size, 0.5)), 0.4, out _);
            Assert.True(both > fastOnly);                           // Fanged (worse) sets the veto
        }

        [Fact]
        public void PerceiverSize_Modulates_BigPerceiverFearsLess()
        {
            var beast = Bag((AtomName.Fanged, 1.0), (AtomName.Size, 0.5));
            double small = SubjectiveSystem.ThreatRead(beast, 0.3, out _);   // small prey: my size is the denominator
            double big   = SubjectiveSystem.ThreatRead(beast, 0.9, out _);   // big perceiver: relative menace shrinks
            Assert.True(small > big);
        }

        [Fact]
        public void SameSizeUnarmed_ReadsNoThreat()
        {
            // a 0.4 human perceiving another 0.4 human (no weapons): equals don't menace
            double t = SubjectiveSystem.ThreatRead(Bag((AtomName.Civilian, 1.0), (AtomName.Size, 0.4)), 0.4, out double v);
            Assert.Equal(0.0, t);
            Assert.Equal(0.0, v);
        }

        [Fact]
        public void MuchBiggerUnarmedBody_MenacesViaMass()
        {
            // a 0.4 perceiver vs a big unarmed body (0.9): mass-menace fires (rel > 1)
            double t = SubjectiveSystem.ThreatRead(Bag((AtomName.Size, 0.9)), 0.4, out _);
            Assert.True(t > 0.0);
        }

        [Fact]
        public void NeutralBag_NoSize_ReadsNoThreat()
        {
            double t = SubjectiveSystem.ThreatRead(Bag((AtomName.Civilian, 1.0), (AtomName.Resident, 1.0)), 0.4, out double v);
            Assert.Equal(0.0, t);
            Assert.Equal(0.0, v);
        }
    }
}
```

- [ ] **Step 2: Run them — expect FAIL** (compile: `ThreatRead` now takes 2 args + an out)

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~ThreatReadTests"`
Expected: FAIL (does not compile — old `ThreatRead(bag, out v)` signature).

- [ ] **Step 3a: Rewrite `ThreatRead`** — replace the method (the `const SizeMassMenace` line stays):

```csharp
        /// <summary>The prey-veto: Threat = max over aversive CUES, read RELATIVE to the perceiver's
        /// own size (its disposition emerges from its own form — a big/armed body fears equal menace
        /// less). rel = targetSize / perceiverSize. Built as a max over cues so the L2 behaviour cue
        /// and the Phase-C/D reputation cue drop in as further entries. NO oracle.</summary>
        public static double ThreatRead(AtomBag bag, double perceiverSize, out double threatValence)
        {
            threatValence = 0;
            if (bag == null || bag.Count == 0) return 0;
            if (perceiverSize < 0.01) perceiverSize = 0.01;          // floor: never divide by zero
            bool hasSize = bag.TryGet(AtomName.Size.ToId(), out var sizeFx);
            double targetSize = hasSize ? sizeFx.ToDouble() : 1.0;
            double rel = targetSize / perceiverSize;                 // my own size is the denominator
            double threat = 0;
            for (int i = 0; i < bag.Count; i++)                      // capacity cues: weapon tone x relative size
            {
                AtomTone tone = AtomCatalog.For(bag[i].Type).Tone;
                double menace = -tone.Valence.ToDouble();            // > 0 only for an aversive (weapon) atom
                if (menace <= 0) continue;
                double cue = menace * tone.Arousal.ToDouble() * rel;
                if (cue > threat) { threat = cue; threatValence = tone.Valence.ToDouble(); }
            }
            if (hasSize)                                             // mass-menace: only a BIGGER body menaces
            {
                double mass = SizeMassMenace * (rel > 1.0 ? rel - 1.0 : 0.0);
                if (mass > threat) { threat = mass; threatValence = -mass; }
            }
            if (threat > 1.0) threat = 1.0;
            if (threatValence < -1.0) threatValence = -1.0;
            return threat;
        }
```

- [ ] **Step 3b: Pass the perceiver's size in `Interpret`** — replace the threat block (the `if (perceivable != null) threat = ThreatRead(perceivable.Bag(other), out threatValence);`):

```csharp
            // Threat = the prey-veto over perceived aversive cues, read RELATIVE to the perceiver's
            // own size (disposition emerges from its own form). NO _creatures oracle.
            double threat = 0, threatValence = 0;
            if (perceivable != null)
            {
                double selfSize = perceivable.Bag(self).TryGet(AtomName.Size.ToId(), out var ssz) ? ssz.ToDouble() : 1.0;
                threat = ThreatRead(perceivable.Bag(other), selfSize, out threatValence);
            }
```

- [ ] **Step 4: Run the suite — expect PASS**

Run: `cd /home/uggeli/slopfall/Headless && DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0` (the 6 rewritten ThreatReadTests pass; no other test referenced the old signature).

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Engine/Units/SubjectiveSystem.cs Headless/Sim.MemoryTests/ThreatReadTests.cs
git commit -m "$(cat <<'EOF'
feat(priors): threat read is relative to the perceiver's own size (C1/C3)

ThreatRead scales menace by targetSize/perceiverSize (was absolute target
size); mass-menace fires only when the target is bigger (kills the would-be
civilian-fears-civilian artifact). Interpret reads self's Size (default 1.0)
and passes it. A small civilian now dreads a Beast a big one shrugs off —
disposition emerges from form, no flag.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3 (C5): `MeaningsStore.SeedInnate`

**Files:**
- Modify: `Assets/Sim/Memory/MeaningsStore.cs` (after `AddNode`, ~line 41)
- Test: Create `Headless/Sim.MemoryTests/SeedInnateTests.cs`

**Interfaces:**
- Consumes: the existing `public CategoryId AddNode(AtomBag prototype, Fixed valence, Fixed confidence, bool innate)`.
- Produces: `public CategoryId SeedInnate(AtomBag prototype, Fixed valence, Fixed confidence)`. Consumed by T4's `AgentMemoryRegistry.SeedInnatePrior`.

- [ ] **Step 1: Write the failing test** — `SeedInnateTests.cs`:

```csharp
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class SeedInnateTests
    {
        static AtomBag Sig(AtomName kind)
            => AtomBag.Create(new[] { new Atom(kind.ToId(), Fixed.One) });

        [Fact]
        public void SeedInnate_IsRecognized_ThenDriftsWithReinforce()
        {
            var store = new MeaningsStore(8, MeaningsConfig.Default);
            var sig = Sig(AtomName.Civilian);
            var id = store.SeedInnate(sig, Fixed.FromDouble(0.2), Fixed.FromDouble(0.3));

            Assert.True(store.RecognizedValence(sig, out var v, out var c));   // born believing
            Assert.True(v.ToDouble() > 0.0);
            Assert.True(c.ToDouble() > 0.0);

            // a bad encounter drifts the innate node negative (it learns)
            store.Reinforce(id, sig, Fixed.FromDouble(-1.0));
            store.RecognizedValence(sig, out var v2, out _);
            Assert.True(v2.ToDouble() < v.ToDouble());
        }
    }
}
```

- [ ] **Step 2: Run it — expect FAIL** (compile: `SeedInnate` undefined)

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~SeedInnateTests"`
Expected: FAIL (does not compile).

- [ ] **Step 3: Add `SeedInnate`** — in `MeaningsStore.cs`, right after the `AddNode` method:

```csharp
        /// <summary>Seed an INNATE category node — an evolved/birth prior the agent is born holding,
        /// which RecognizedValence returns and Reinforce then drifts. The named load-time entry point
        /// (SeedPriors uses it); a thin wrapper over AddNode(innate: true).</summary>
        public CategoryId SeedInnate(AtomBag prototype, Fixed valence, Fixed confidence)
            => AddNode(prototype, valence, confidence, innate: true);
```

- [ ] **Step 4: Run the suite — expect PASS**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Memory/MeaningsStore.cs Headless/Sim.MemoryTests/SeedInnateTests.cs
git commit -m "$(cat <<'EOF'
feat(priors): MeaningsStore.SeedInnate — the innate-node seed entry point (C5)

A named load-time wrapper over the existing AddNode(innate: true): seed a
prototype the agent is born believing, which RecognizedValence returns and
Reinforce then drifts. SeedPriors (C7) uses it.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4 (C6+C7): `InnatePriors` + `SeedPriors`

**Files:**
- Create: `Assets/Sim/Engine/Units/InnatePriors.cs`
- Modify: `Assets/Sim/Engine/Units/AgentMemoryRegistry.cs` (add `SeedInnatePrior`, beside `SeedPlace` ~line 74); `Assets/Sim/World/AgentMemorySeeding.cs` (`SeedPriors` + the `SeedAgent` call)
- Test: Create `Headless/Sim.MemoryTests/InnatePriorsTests.cs`

**Interfaces:**
- Consumes: `MeaningsStore.SeedInnate` (T3); `Perceivable.Signature(agent)`, `world.Identity.TryGet(agent, out IdentityData)` → `.Kind`, `AgentMemoryRegistry.TryGet`.
- Produces: `InnatePriors.For(EntityKind) : InnatePrior[]`; `enum PriorTarget { Self }`; `struct InnatePrior { PriorTarget Target; Fixed Valence; Fixed Confidence; }`; `AgentMemoryRegistry.SeedInnatePrior(EntityId, AtomBag, Fixed, Fixed)`; `AgentMemorySeeding.SeedPriors(SimWorld, EntityId)`.

- [ ] **Step 1: Write the failing tests** — `InnatePriorsTests.cs` (the two unit-testable pieces; the `SeedPriors` glue is soak-verified in T5):

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class InnatePriorsTests
    {
        [Fact]
        public void Civilian_HasOneInGroupPrior_OthersEmpty()
        {
            var civ = InnatePriors.For(EntityKind.CivilianNPC);
            Assert.Single(civ);
            Assert.Equal(PriorTarget.Self, civ[0].Target);
            Assert.True(civ[0].Valence.ToDouble() > 0.0);            // in-group warmth
            Assert.True(civ[0].Confidence.ToDouble() > 0.0);
            Assert.Empty(InnatePriors.For(EntityKind.EnemyMonster)); // monster prior deferred to Phase D
        }

        [Fact]
        public void SeedInnatePrior_WritesARecognizableNode()
        {
            var e = new EventBus();
            var mem = new AgentMemoryRegistry(e);
            var agent = new EntityId(1);
            mem.Seed(agent);
            var sig = AtomBag.Create(new[] { new Atom(AtomName.Civilian.ToId(), Fixed.One) });

            mem.SeedInnatePrior(agent, sig, Fixed.FromDouble(0.2), Fixed.FromDouble(0.3));

            Assert.True(mem.TryGet(agent, out var m));
            Assert.True(m.Meanings.RecognizedValence(sig, out var v, out _));
            Assert.True(v.ToDouble() > 0.0);
        }
    }
}
```

> Confirm the `AgentMemoryRegistry` ctor shape at execution start (`new AgentMemoryRegistry(EventBus)` per the registry pattern); if it differs, match the actual ctor.

- [ ] **Step 2: Run them — expect FAIL** (compile: `InnatePriors`/`SeedInnatePrior` undefined)

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~InnatePriorsTests"`
Expected: FAIL (does not compile).

- [ ] **Step 3a: Create `InnatePriors.cs`:**

```csharp
using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>Which signature an innate prior is ABOUT. L1 carries only Self (in-group = "those who
    /// look like me"); Kind(X) out-group targets arrive with Phase D, when episodic attribution makes
    /// them non-redundant and monsters gain a perceivable Kind atom.</summary>
    public enum PriorTarget { Self }

    /// <summary>One innate category belief: a starting valence + confidence about a PriorTarget.</summary>
    public readonly struct InnatePrior
    {
        public readonly PriorTarget Target;
        public readonly Fixed Valence;
        public readonly Fixed Confidence;
        public InnatePrior(PriorTarget target, Fixed valence, Fixed confidence)
        { Target = target; Valence = valence; Confidence = confidence; }
    }

    /// <summary>
    /// The kind-keyed innate priors a newborn is seeded with — a small, believable STARTER set in a
    /// structure built to grow: Phase D reinforces these nodes, E transmits them, and rows/targets are
    /// added as the world calls for them. Frozen content; open structure.
    /// </summary>
    public static class InnatePriors
    {
        // CivilianNPC: innate warmth toward its own kind (same-signature kin). conf 0.3 = a lean, not
        // a conviction (Reinforce drifts it; an individual dossier overrides the category entirely).
        static readonly InnatePrior[] Civilian =
        {
            new InnatePrior(PriorTarget.Self, Fixed.FromDouble(0.2), Fixed.FromDouble(0.3)),
        };
        static readonly InnatePrior[] None = new InnatePrior[0];

        public static InnatePrior[] For(EntityKind kind)
            => kind == EntityKind.CivilianNPC ? Civilian : None;
    }
}
```

- [ ] **Step 3b: Add `SeedInnatePrior`** — in `AgentMemoryRegistry.cs`, beside `SeedPlace`:

```csharp
        /// <summary>Seed an innate category belief into the agent's MEANINGS store (load-time, direct —
        /// the SeedPlace pattern). SeedPriors uses this.</summary>
        public void SeedInnatePrior(EntityId agent, AtomBag signature, Fixed valence, Fixed confidence)
        { if (_d.TryGetValue(agent, out var mem)) mem.Meanings.SeedInnate(signature, valence, confidence); }
```

- [ ] **Step 3c: Add `SeedPriors` + wire it** — in `AgentMemorySeeding.cs`, add the call in `SeedAgent` and the method:

```csharp
        public static void SeedAgent(SimWorld world, EntityId agent)
        {
            SeedPlaces(world, agent);
            SeedPriors(world, agent);            // Phase C: innate kind-keyed category beliefs
            // Future SeedX (slot in here — callers need no change):
            //   SeedOwnerships(world, agent);    // the buildings/goods this agent owns
            //   SeedReputations(world, agent);   // standings it already holds
            //   SeedSocial(world, agent);        // kin / household ties known from birth
        }

        /// <summary>Innate species priors: stamp this agent's kind-keyed innate category beliefs into
        /// MEANINGS. L1 = in-group warmth, keyed by the agent's OWN signature (so it matches same-kind
        /// kin under the store's near-exact recognition). The learned layer drifts these at runtime;
        /// an individual dossier overrides the category entirely.</summary>
        public static void SeedPriors(SimWorld world, EntityId agent)
        {
            if (!world.Identity.TryGet(agent, out var ident) || ident == null) return;
            foreach (var prior in InnatePriors.For(ident.Kind))
            {
                AtomBag proto = prior.Target == PriorTarget.Self
                    ? world.Perceivable.Signature(agent)
                    : AtomBag.Empty;
                if (proto.Count == 0) continue;                     // nothing to key on → skip
                world.AgentMemory.SeedInnatePrior(agent, proto, prior.Valence, prior.Confidence);
            }
        }
```

> `AgentMemorySeeding` already `using`s `DaggerfallWorkshop.Sim.Engine` and `DaggerfallWorkshop.Sim.Memory` — `InnatePriors`/`PriorTarget` resolve without a new using.

- [ ] **Step 4: Run the suite — expect PASS**

Run: `cd /home/uggeli/slopfall/Headless && DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Engine/Units/InnatePriors.cs Assets/Sim/Engine/Units/AgentMemoryRegistry.cs Assets/Sim/World/AgentMemorySeeding.cs Headless/Sim.MemoryTests/InnatePriorsTests.cs
git commit -m "$(cat <<'EOF'
feat(priors): seed innate in-group warmth at spawn — SeedPriors (C6/C7)

InnatePriors (kind-keyed starter table, structure built to grow) + the
AgentMemoryRegistry.SeedInnatePrior direct-write + a SeedPriors sibling in
AgentMemorySeeding.SeedAgent. A newborn civilian is seeded warm (+0.2) toward
its own-signature kin, keyed by Perceivable.Signature(self) so it matches
under the store's near-exact recognition. Drifts via Reinforce; dossier
overrides. The monster out-group prior is deferred to Phase D.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Soak gate — behaviour-sense

**Files:** scratchpad captures only.

> NOT a determinism gate. Judge whether the result is sensible and in-regime against the Phase B reference (Gothway 337→340), not bytes.

- [ ] **Step 1: Capture + run**

```bash
cd /home/uggeli/slopfall/Headless
export DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2
SP=/tmp/claude-1000/-home-uggeli-slopfall/<session>/scratchpad   # use this session's scratchpad
dotnet run --project Sim.Host/Sim.Host.csproj -c Release -- --soak Daggerfall "Gothway Garden" 1 > "$SP/phasec-gothway.txt" 2>&1
dotnet run --project Sim.Host/Sim.Host.csproj -c Release -- --soak Daggerfall Gallotale 1 > "$SP/phasec-gallotale.txt" 2>&1
tail -6 "$SP/phasec-gothway.txt"
```

- [ ] **Step 2: Judge the regime (behaviour-sense)** — confirm:
  - **Population stable** (no collapse/explosion).
  - **Civilians fear and flee monsters MORE than the Phase B baseline** — the relative-size disposition means a small (0.4) civilian reads a Beast (0.5) as bigger-than-me, so `Flee`/creature-`kills` should be present and plausibly higher, not zero and not exploded. If civilians are *paralysed* (work/shop collapse, constant fleeing), the lever is the human/Beast size ratio (raise human Size toward 0.5) — do NOT change the formula; re-run.
  - **No civilian-fears-civilian artifact** — same-size unarmed humans must not read each other as threats (no spurious `Flee` among townsfolk, no social collapse).
  - **In-group warmth visible/plausible** — same-signature kin read each other warmly (greet/socialize among like kin); social behaviour stays believable.
  - **Day/night rhythm + guards** unchanged and sensible.

  A different-but-believable regime is success; an implausible one (ghost-town, mass death, townsfolk fleeing each other) is a bug — investigate with superpowers:systematic-debugging, do NOT tune.

- [ ] **Step 3: Full green sweep + record the gate**

```bash
cd /home/uggeli/slopfall/Headless
DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release 2>&1 | tail -1
dotnet test Sim.SpatialTests/Sim.SpatialTests.csproj -c Release 2>&1 | tail -1
cd /home/uggeli/slopfall
git commit --allow-empty -m "$(cat <<'EOF'
test(priors): soak gate — relative-size fear + in-group warmth in-regime (C-L1)

Gothway/Gallotale soaks: population stable; civilians read monsters as
bigger-than-me and flee believably (more than the Phase B baseline); no
civilian-fears-civilian artifact; in-group warmth plausible; guards + rhythm
intact. Behaviour-sense gate (the parallel soak is nondeterministic; not a
byte-diff). Phase C/L1 complete.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review

**1. Spec coverage:** C1 (relative-size ThreatRead) → T2. C2 (Size on humans) → T1. C3 (ThreatRead/Interpret wiring + default 1.0) → T2. C4 (guard weapons deferred) → not built, by design. C5 (SeedInnate) → T3. C6 (InnatePriors table) → T4 (in-group only; monster row deferred per Realization note 3). C7 (SeedPriors via a registry method) → T4. C8 (confidence drives learning, dossier overrides) → T4 (conf 0.3; the read uses valence directly — unchanged from Phase B). C9 (signature keying) → T4 (in-group via `Signature(self)`, per Realization note 2). Realization notes 1–3 honoured. Soak gate → T5.

**2. Placeholder scan:** No "TBD"/"handle edge cases". Every code step shows full before/after; every run step has a command + expected output. The one `<session>` token in T5 is an explicit instruction to use the live scratchpad path (the controller substitutes it), not a code placeholder.

**3. Type consistency:** `ThreatRead(AtomBag, double, out double)` is consistent across T2's tests, the method, and the `Interpret` call. `HumanForms.Civilian : (AtomTypeId, Fixed)[]` matches T1's stamp + test. `SeedInnate(AtomBag, Fixed, Fixed) → CategoryId` (T3) is what `SeedInnatePrior` (T4) calls. `InnatePrior{Target,Valence,Confidence}` + `PriorTarget.Self` + `InnatePriors.For(EntityKind)` (T4) are used consistently in `SeedPriors` and the test. `SeedInnatePrior(EntityId, AtomBag, Fixed, Fixed)` matches its call in `SeedPriors`.

**Open flags for the implementer:**
- T4 Step 1 note: confirm the `AgentMemoryRegistry` constructor (`new AgentMemoryRegistry(EventBus)`) at execution start; match the actual ctor if it differs.
- The `Size` default `1.0` in `ThreatRead`/`Interpret` keeps any un-sized agent (e.g. `StaticNPC`, or pre-T1 state) behaving as Phase B — confirm no other live caller of the old `ThreatRead(bag, out v)` signature remains (grep `ThreatRead(`; only `Interpret` should call it).
- Calibration sensitivity (T5): the human/Beast size ratio (0.4 / 0.5) sets fear intensity — the named soak lever if civilians over- or under-react.
