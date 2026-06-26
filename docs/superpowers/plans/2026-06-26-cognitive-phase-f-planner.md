# Cognitive Phase F/L1 — The Planner Consumes the Arc Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A town learns to fear a kind that doesn't look dangerous — by retiring verdict-named perceptual identity, adding a harmless-looking attacker, and wiring the learned kind-belief into the threat read.

**Architecture:** Five sequential slices. **FT1 (F0)** retires the verdict atom from perception — the monster is perceived as a neutral `Beast` (new `AtomName.Beast`, stamped directly like a form atom), its innate prior re-keyed to match. **FT2 (F1)** spawns the **Drifter**: mechanically an `EnemyMonster` (same AI/attack — damage is form-independent), but perceived as an unarmed, person-shaped `Drifter`. **FT3 (F2)** folds the perceiver's learned aversion (× confidence) into the threat read — the reputation cue. **FT4 (F3)** makes the danger-alarm carry the killer's *actual* neutral kind. **FT5** is the multi-day behaviour-sense soak.

**Tech Stack:** C# on **.NET 10**, xUnit, fixed-point `Fixed` (the Memory subsystem is float-free; `Interpret`/`ThreatRead` are the `double` Engine boundary).

## Global Constraints

- **.NET 10 / modern C#**; `Nullable` disable; match surrounding style; build at **0 warnings**.
- **No verdict atoms — including in identity.** Perception reports a neutral *appearance* (`Beast`, `Drifter`, `Civilian`), never an `EntityKind` hostility verdict (`EnemyMonster`/`EnemyClass`). `EntityKind` stays the internal AI/spawn role, never perceived.
- **Role ≠ appearance.** The Drifter is `EntityKind.EnemyMonster` internally (reuses all proven creature AI/attack/guard-targeting) but stamps `AtomName.Drifter` perceptually. Nothing keys threat/fear/belief on the raw `EntityKind`.
- **Memory subsystem is float-free** (`Fixed`, Raw int, Scale=256). `Interpret`/`ThreatRead` already work in `double` (the Engine read boundary) — the cue's `double` math is consistent with the surrounding method; `.ToDouble()` on the `Fixed` valence/confidence is the existing pattern.
- **Reuse the wired machinery.** The cue is a few lines in the existing `Interpret`; the alarm extends E's existing shout; the Drifter reuses `CreatureSystem`/`HealthSystem`. `AtomName` is a dense int enum — new members are append-only and safe.
- **Soak validates behaviour-SENSE, not byte-determinism** (memory `validate-sim-by-behavior-sense`). The mint→learn→fear loop spans a sleep, so the soak is **multi-day**.
- **Confirm the green baseline fresh at execution start.** Phase E left **256** `Sim.MemoryTests`; one grounding observed 225 (likely a filtered run) — run the suite first and use the real number; each task reports the new count, `Failed: 0`.
- **Don't sweep WIP.** Two pre-existing WIP docs (`docs/what_is_an_atom.md`, the 2026-06-24 M1 spec) are uncommitted — **`git add` only your task's files**, never `git add -A`.
- **Commit style:** conventional commits, scope `cognition`. End every body with:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

## Dependency map (execute SEQUENTIALLY in the main checkout)

```
FT1 (neutral identity + retrofit monster)  →  FT2 (the Drifter)  →  FT3 (reputation cue)  →  FT4 (alarm carries killer's kind)  →  FT5 (soak)
```

FT2 needs `AtomName.Drifter` (FT1). FT4's test wants a Drifter to kill with (FT2). FT3 is independent of FT2 but reads the learned belief FT1/FT2 make recordable. Run sequentially in the main tree (per the worktree-base lesson).

---

## File Structure

**Modified:**
- `Assets/Sim/Memory/AtomName.cs` — add `Beast`, `Drifter` appearance members (FT1).
- `Assets/Sim/Memory/AtomCatalog.cs` — register the two new Kind atoms (forward + reverse) (FT1).
- `Assets/Sim/Engine/Units/InnatePriors.cs` — `InnatePrior.Kind` becomes the appearance `AtomName`; monster row → `Beast` (FT1).
- `Assets/Sim/World/AgentMemorySeeding.cs` — build the Kind prior prototype from the appearance atom (FT1).
- `Assets/Sim/Engine/Units/CreatureSystem.cs` — monster stamps `Beast` (FT1); spawn the Drifter variant (FT2).
- `Assets/Sim/Engine/Units/CreatureForms.cs` — add `Drifter` (weapon-less) form (FT2).
- `Assets/Sim/Engine/Units/SubjectiveSystem.cs` — the reputation cue (FT3).
- `Assets/Sim/Engine/Units/PlaceDangerSystem.cs` — `PerceivableRegistry` + `SubjectKind = Signature(killer)` (FT4).
- `Assets/Sim/Engine/SimWorld.cs` — thread `Perceivable` into `PlaceDangerSystem` (FT4).
- Tests that seed the monster's atom move `EnemyMonster → Beast`: `DamageReinforceTests.cs`, `GossipReinforceTests.cs` (FT1); `PlaceDangerTests.cs` (FT4).

**New tests:**
- `Headless/Sim.MemoryTests/AppearanceAtomTests.cs` (FT1)
- `Headless/Sim.MemoryTests/DrifterFormTests.cs` (FT2)
- `Headless/Sim.MemoryTests/ReputationCueTests.cs` (FT3)

---

## Task 1 (F0): Neutral appearance atoms + retrofit the monster

Perception stops broadcasting the `EnemyMonster` verdict. The monster becomes a neutral `Beast`; its innate prior re-keys to match.

**Files:**
- Modify: `Assets/Sim/Memory/AtomName.cs` (the enum, after the `Fanged, Fast, Size` form section)
- Modify: `Assets/Sim/Memory/AtomCatalog.cs` (`BuildEntries` ~83, `BuildReverse` ~145)
- Modify: `Assets/Sim/Engine/Units/InnatePriors.cs` (the `InnatePrior` struct field + the `Civilian` rows)
- Modify: `Assets/Sim/World/AgentMemorySeeding.cs` (`SeedPriors` ~46)
- Modify: `Assets/Sim/Engine/Units/CreatureSystem.cs` (the monster Kind stamp ~236)
- Modify: `Headless/Sim.MemoryTests/DamageReinforceTests.cs`, `Headless/Sim.MemoryTests/GossipReinforceTests.cs` (the monster-sig helpers)
- Test: Create `Headless/Sim.MemoryTests/AppearanceAtomTests.cs`

**Interfaces:**
- Produces: `AtomName.Beast`, `AtomName.Drifter` (neutral appearance identities, `AtomCategory.Kind`); the monster's perceivable Signature is now `{Beast}`; `InnatePrior.Kind : AtomName` (the appearance to seed). Consumed by FT2 (`Drifter`), FT4 (the alarm reads whatever the killer stamps).

- [ ] **Step 1: Write the failing test** — `AppearanceAtomTests.cs`:

```csharp
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class AppearanceAtomTests
    {
        [Fact]
        public void BeastAndDrifter_AreDistinctNeutralKindAtoms()
        {
            var beast = AtomName.Beast.ToId();
            var drifter = AtomName.Drifter.ToId();
            Assert.NotEqual(beast, drifter);
            // both are identity/Kind atoms (so they form a signature + a category)
            Assert.Equal(AtomCategory.Kind, AtomCatalog.For(beast).Category);
            Assert.Equal(AtomCategory.Kind, AtomCatalog.For(drifter).Category);
            Assert.True(AtomCatalog.For(beast).IsIdentity);
            // distinct from a civilian's neutral identity
            Assert.NotEqual(beast, PerceivableAtoms.Kind(EntityKind.CivilianNPC));
            Assert.NotEqual(drifter, PerceivableAtoms.Kind(EntityKind.CivilianNPC));
            // round-trips through the reverse map
            Assert.Equal(AtomName.Beast, AtomCatalog.NameOf(beast));
            Assert.Equal(AtomName.Drifter, AtomCatalog.NameOf(drifter));
        }
    }
}
```

- [ ] **Step 2: Run it — expect FAIL** (compile: `AtomName.Beast`/`Drifter` don't exist)

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~AppearanceAtomTests"`
Expected: FAIL (does not compile).

- [ ] **Step 3a: Add the appearance members** — in `AtomName.cs`, after the `Fanged, Fast, Size` form section (end of the enum), add:

```csharp
    // ── Appearance (perceived neutral identity: what a thing LOOKS like) — Phase F.
    //    A verdict-free identity layer: the perceiver forms "enemy"/"dangerous", it is never stamped.
    //    Stamped directly via AtomName.X.ToId() (like the form atoms). The legacy verdict members
    //    above (EnemyClass/EnemyMonster) are no longer stamped on anyone — perception uses these. ──
    Beast, Drifter,
```

- [ ] **Step 3b: Register the two atoms in the catalog** — in `AtomCatalog.cs` `BuildEntries`, alongside the form-atom registrations (the `Fanged/Fast/Size` `Put(...)` block — they register as identity/Kind), add `Beast` and `Drifter` as `AtomCategory.Kind`, ordinary salience, not shareable, no tone:

```csharp
        Put(AtomName.Beast,   AtomCategory.Kind, ordinary, false);
        Put(AtomName.Drifter, AtomCategory.Kind, ordinary, false);
```

And in `BuildReverse`, alongside the `Add(AtomName.Fanged.ToId(), AtomName.Fanged);` lines, add:

```csharp
        Add(AtomName.Beast.ToId(),   AtomName.Beast);
        Add(AtomName.Drifter.ToId(), AtomName.Drifter);
```

> If the form atoms are registered in `BuildEntries` by a different idiom than `Put(...)`, match that idiom; the requirement is: both new atoms resolve to `Category == AtomCategory.Kind`, `IsIdentity == true`, and round-trip through `NameOf`.

- [ ] **Step 3c: Re-key the innate monster prior to the appearance** — in `InnatePriors.cs`, change the `InnatePrior.Kind` field from `EntityKind` to `AtomName` (the appearance to seed; `AtomName.None` for the `Self` prior), and the monster row to `Beast`:

```csharp
        static readonly InnatePrior[] Civilian =
        {
            new InnatePrior(PriorTarget.Self, AtomName.None,  Fixed.FromDouble(0.2),  Fixed.FromDouble(0.3)),
            new InnatePrior(PriorTarget.Kind, AtomName.Beast, Fixed.FromDouble(-0.6), Fixed.FromDouble(0.3)),
        };
```

Update the `InnatePrior` struct's field type and constructor accordingly (`EntityKind Kind` → `AtomName Kind`).

- [ ] **Step 3d: Build the prior prototype from the appearance atom** — in `AgentMemorySeeding.cs` `SeedPriors`, the `PriorTarget.Kind` arm:

```csharp
                PriorTarget.Kind => AtomBag.Create(new[] { new Atom(prior.Kind.ToId(), Fixed.One) }),
```

- [ ] **Step 3e: The monster stamps the neutral appearance** — in `CreatureSystem.cs` (~236), replace the Kind-atom stamp:

```csharp
            // Phase F/L1: stamp the creature's perceivable APPEARANCE (neutral identity) — a "Beast"
            // (a fanged quadruped). Verdict-free: the perceiver forms the fear (innate prior + form),
            // it is never broadcast as "enemy". Lets the innate {Beast} prior match + damage reinforce it.
            Events.Publish(new StampAtomIntent { Entity = id, Type = AtomName.Beast.ToId(), Value = Fixed.One });
```

- [ ] **Step 3f: Move the monster-sig test helpers to `Beast`** — in `DamageReinforceTests.cs` and `GossipReinforceTests.cs`, every `PerceivableAtoms.Kind(EntityKind.EnemyMonster)` that seeds/asserts the **monster's** atom becomes `AtomName.Beast.ToId()` (the monster is now perceived as a Beast). E.g. `DamageReinforceTests` `MonsterSig()` and its `perceivable.Seed(monster, …)`; `GossipReinforceTests` `MonsterKind()`. (Leave any `Kind(EntityKind.CivilianNPC)` calls untouched — civilians are unchanged.)

- [ ] **Step 4: Run the suite — expect PASS**

Run: `cd /home/uggeli/slopfall/Headless && DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0` (baseline + 1). The reinforce/gossip tests pass with the Beast sig (the logic is unchanged, only the neutral atom differs); no Kind atom encodes an `EntityKind` verdict on any creature.

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Memory/AtomName.cs Assets/Sim/Memory/AtomCatalog.cs Assets/Sim/Engine/Units/InnatePriors.cs Assets/Sim/World/AgentMemorySeeding.cs Assets/Sim/Engine/Units/CreatureSystem.cs Headless/Sim.MemoryTests/DamageReinforceTests.cs Headless/Sim.MemoryTests/GossipReinforceTests.cs Headless/Sim.MemoryTests/AppearanceAtomTests.cs
git commit -m "$(cat <<'EOF'
feat(cognition): perception reports a neutral appearance, not a verdict (F0)

Retire the verdict-named perceptual identity. The monster now broadcasts a
neutral AtomName.Beast (a fanged quadruped) instead of Kind(EnemyMonster); its
menace lives in its form atoms + the innate prior, not its label. New neutral
appearance atoms Beast/Drifter (stamped directly like form atoms, registered as
Kind/identity in the catalog). The innate prior re-keys to {Beast}; the
verdict-laden EnemyMonster/EnemyClass AtomNames are no longer stamped on any
creature. Civilians (AtomName.Civilian, already neutral) unchanged.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2 (F1): The Drifter — a harmless-looking attacker

**Files:**
- Modify: `Assets/Sim/Engine/Units/CreatureForms.cs` (add `Drifter`)
- Modify: `Assets/Sim/Engine/Units/CreatureSystem.cs` (`TrySpawn` ~218-237: pick a variant)
- Test: Create `Headless/Sim.MemoryTests/DrifterFormTests.cs`

**Interfaces:**
- Consumes: `AtomName.Drifter` (FT1), `SubjectiveSystem.ThreatRead`.
- Produces: a fraction of spawned creatures are Drifters — `EntityKind.EnemyMonster` role, `CreatureForms.Drifter` (weapon-less), `AtomName.Drifter` appearance. They attack like any creature.

- [ ] **Step 1: Write the failing test** — `DrifterFormTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class DrifterFormTests
    {
        [Fact]
        public void Drifter_IsWeaponless_AndReadsHarmless()
        {
            // no weapon (capacity) atoms — only a body Size
            Assert.DoesNotContain(CreatureForms.Drifter, f => f.Type == AtomName.Fanged.ToId());
            Assert.DoesNotContain(CreatureForms.Drifter, f => f.Type == AtomName.Fast.ToId());
            Assert.Contains(CreatureForms.Drifter, f => f.Type == AtomName.Size.ToId());

            // ThreatRead over a Drifter's perceived bag (its form + its appearance) ≈ 0: it LOOKS harmless
            var bag = AtomBag.Create(
                CreatureForms.Drifter.Select(f => new Atom(f.Type, f.Value))
                    .Append(new Atom(AtomName.Drifter.ToId(), Fixed.One)).ToList());
            double t = SubjectiveSystem.ThreatRead(bag, 0.4, out _);
            Assert.True(t <= 0.0001);   // no weapon tone, not bigger than the perceiver → no form-threat
        }
    }
}
```

- [ ] **Step 2: Run it — expect FAIL** (`CreatureForms.Drifter` doesn't exist)

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~DrifterFormTests"`
Expected: FAIL (does not compile).

- [ ] **Step 3a: Add the weapon-less Drifter form** — in `CreatureForms.cs`, alongside `Beast`:

```csharp
        // Phase F/L1: the Drifter looks like an ordinary person — a body Size, NO weapon atoms.
        // Mechanically a creature (it still attacks); perceptually harmless until learned.
        public static readonly (AtomTypeId Type, Fixed Value)[] Drifter =
        {
            (AtomName.Size.ToId(), Fixed.FromDouble(0.4)),
        };
```

- [ ] **Step 3b: Spawn a fraction as Drifters** — in `CreatureSystem.TrySpawn`, pick the variant off the spawn hash `h` (already computed), and use it for the name, the form, and the appearance atom. Replace the `IdentitySetIntent`, the form loop, and the Kind-stamp:

```csharp
            bool drifter = (h & 1u) == 0u;   // ~half spawn as the harmless-looking Drifter (frozen split)
            Events.Publish(new IdentitySetIntent
            {
                Id = id,
                Data = new IdentityData
                {
                    Name = drifter ? "Drifter" : "Beast", Kind = EntityKind.EnemyMonster,
                    Race = -1, Gender = 0, CareerIndex = -1, Level = 1, FactionId = 0, Team = 0,
                },
            });
            Events.Publish(new PositionSetIntent { Id = id, X = px, Y = 0f, Z = pz, Yaw = 0f });
            Events.Publish(new VitalsSetIntent { Id = id, Data = new VitalsData { CurrentHealth = 20, MaxHealth = 20 } });
            Events.Publish(new CreatureSetIntent { Id = id, Data = new CreatureData { TargetX = px, TargetZ = pz, NextAttackTick = 0, HungerLevel = 0.6f } });
            // Perceivable FORM: weapons + size (Beast) or just a body (Drifter — looks harmless).
            foreach (var form in (drifter ? CreatureForms.Drifter : CreatureForms.Beast))
                Events.Publish(new StampAtomIntent { Entity = id, Type = form.Type, Value = form.Value });
            // Perceivable APPEARANCE (neutral identity): a Beast or a Drifter. Verdict-free.
            Events.Publish(new StampAtomIntent { Entity = id, Type = (drifter ? AtomName.Drifter : AtomName.Beast).ToId(), Value = Fixed.One });
            return true;
```

> The variant is keyed on the existing per-spawn hash `h` (no `Math.Random`, which is unavailable). Both variants are `EntityKind.EnemyMonster` — same AI, hostility, attack, guard-targeting; damage is form-independent (`HealthSystem`), so the weapon-less Drifter hurts identically.

- [ ] **Step 4: Run the suite — expect PASS**

Run: `cd /home/uggeli/slopfall/Headless && DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0` (+1). Existing creature/spawn tests unaffected (the Beast path is behaviour-identical; the Drifter is additive).

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Engine/Units/CreatureForms.cs Assets/Sim/Engine/Units/CreatureSystem.cs Headless/Sim.MemoryTests/DrifterFormTests.cs
git commit -m "$(cat <<'EOF'
feat(cognition): the Drifter — a harmless-looking attacker (F1)

CreatureSystem spawns ~half its creatures as Drifters: same EnemyMonster role
(AI/attack/guard-targeting unchanged; damage is form-independent), but a
weapon-less humanoid form + a neutral Kind(Drifter) appearance — so ThreatRead
reads ~0 (it looks harmless) and civilians have no innate prior for it. The
contrast with the form-scary Beast is the demonstrator: agents must LEARN the
Drifter is dangerous.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3 (F2): The reputation cue — learned aversion becomes threat

**Files:**
- Modify: `Assets/Sim/Engine/Units/SubjectiveSystem.cs` (`Interpret` stranger branch ~134-141; a `RepWeight` const near `StigmaScale`/`SizeMassMenace`)
- Test: Create `Headless/Sim.MemoryTests/ReputationCueTests.cs`

**Interfaces:**
- Consumes: `MeaningsStore.RecognizedValence(AtomBag, out Fixed valence, out Fixed confidence)`.
- Produces: `Interpret(...).Threat > 0` for an entity whose learned kind-category is aversive, even with no form atoms; `Threat` scales with `confidence × −valence`.

- [ ] **Step 1: Write the failing test** — `ReputationCueTests.cs`:

```csharp
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class ReputationCueTests
    {
        // Build the registry set Interpret needs; seed the perceiver's learned belief about the kind.
        static EntityRead Read(double learnedValence, double confidence)
        {
            var e = new EventBus();
            var relations = new RelationsRegistry(e);
            var affects = new AffectsRegistry(e);
            var behavior = new BehaviorRegistry(e);
            var personality = new PersonalityRegistry(e);
            var perceivable = new PerceivableRegistry(e);
            var agentMem = new AgentMemoryRegistry(e, AgentMemoryConfig.Default);
            var self = new EntityId(1); var other = new EntityId(2);

            // `other` is perceivable ONLY as a neutral Drifter — NO weapon/form atoms (form-threat ≈ 0).
            perceivable.Seed(other, AtomName.Drifter.ToId(), Fixed.One);
            var sig = AtomBag.Create(new[] { new Atom(AtomName.Drifter.ToId(), Fixed.One) });
            agentMem.Seed(self);
            agentMem.SeedInnatePrior(self, sig, Fixed.FromDouble(learnedValence), Fixed.FromDouble(confidence));
            e.Tick(); perceivable.Update(0); agentMem.Update(0);

            return SubjectiveSystem.Interpret(relations, affects, behavior, personality, perceivable, agentMem, self, other);
        }

        [Fact]
        public void AversiveLearnedKind_ReadsAsThreat_WithNoFormAtoms()
        {
            var feared = Read(-0.8, 0.9);
            Assert.True(feared.Threat > 0.0);     // a believed-dangerous drifter reads as a THREAT...

            var neutral = Read(0.0, 0.3);
            Assert.Equal(0.0, neutral.Threat);    // ...while a not-yet-learned drifter does not

            var tentative = Read(-0.8, 0.2);
            Assert.True(tentative.Threat < feared.Threat);   // fear scales with confidence
        }
    }
}
```

- [ ] **Step 2: Run it — expect FAIL** (`Threat` is 0 — no form atoms, no cue yet)

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~ReputationCueTests"`
Expected: FAIL (`feared.Threat > 0` fails — currently 0).

- [ ] **Step 3a: Add the constant** — in `SubjectiveSystem.cs`, near `StigmaScale` / `SizeMassMenace`:

```csharp
        const double RepWeight = 1.0;   // F2: how strongly a learned aversion to a KIND reads as a threat
```

- [ ] **Step 3b: Fold the learned aversion into threat** — in `Interpret`'s stranger branch, capture the confidence (today `out _`) and add the cue:

```csharp
            else
            {
                // A stranger — the agent's OWN learned category over their perceived signature
                // (fed by MemoryReinforceSystem). No role-scalar stereotype anymore.
                double newV = 0;
                if (agentMem != null && perceivable != null
                    && agentMem.TryGet(self, out var mem)
                    && mem.Meanings.RecognizedValence(perceivable.Signature(other), out var lv, out var lconf))
                {
                    newV = lv.ToDouble();
                    // F2 reputation cue: a learned aversion to this KIND reads as a THREAT — even with no
                    // weapon form atoms. So a kind you LEARNED (D) or were TOLD (E) is dangerous is feared;
                    // fear scales with how deep + confident the belief is. (Redundant for the form-scary
                    // Beast — max() with its higher form-threat — the Drifter is where this bites.)
                    if (newV < 0)
                    {
                        double rep = lconf.ToDouble() * (-newV) * RepWeight;
                        if (rep > threat) { threat = rep; threatValence = newV; }
                    }
                }
                baseValence = newV;
            }
```

> The cue updates `threat`/`threatValence` before the prey-veto (`:151`) and the `EntityRead` construction, so both reflect it. Known entities (dossier branch) get no kind-cue in L1 — they're judged individually.

- [ ] **Step 4: Run the suite — expect PASS**

Run: `cd /home/uggeli/slopfall/Headless && DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0` (+1). Existing `ThreatReadTests` / Interpret tests unaffected (the cue only fires on an aversive *learned* belief, which those tests don't seed).

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Engine/Units/SubjectiveSystem.cs Headless/Sim.MemoryTests/ReputationCueTests.cs
git commit -m "$(cat <<'EOF'
feat(cognition): the reputation cue — learned aversion becomes threat (F2)

Interpret folds the perceiver's learned kind-belief into the threat read: an
aversive learned category contributes repThreat = confidence × -valence ×
RepWeight, max'd with the form-threat. So a kind you learned (D) or were told
(E) is dangerous reads as a threat even with no weapon atoms — the consumer
that finally makes B-E change fear. Redundant for the form-scary Beast; it's
the Drifter (form-threat ~0) where fear becomes 100% learned.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4 (F3): The alarm carries the killer's actual kind

**Files:**
- Modify: `Assets/Sim/Engine/Units/PlaceDangerSystem.cs` (ctor + the shout `SubjectKind` ~60)
- Modify: `Assets/Sim/Engine/SimWorld.cs` (the `PlaceDangerSystem` construction ~150)
- Modify: `Headless/Sim.MemoryTests/PlaceDangerTests.cs` (the `Rig` + the `WitnessShout` test)

**Interfaces:**
- Consumes: `PerceivableRegistry.Signature(EntityId)`.
- Produces: the danger shout's `SubjectKind = Signature(killer)` — the killer's actual neutral kind (`Beast` or `Drifter`), so E's gossip relay spreads fear of the real kind.

- [ ] **Step 1: Update the test rig + write the failing assertion** — in `PlaceDangerTests.cs`, give the `Rig` a `PerceivableRegistry`, pass it to `PlaceDangerSystem`, and seed the killer's appearance; assert the shout carries it. Add to the `Rig`:

```csharp
            public readonly PerceivableRegistry Perceivable;
```
construct it (before `System`) and pass it last:
```csharp
                Perceivable = new PerceivableRegistry(E);
                System = new PlaceDangerSystem(E, Sensed, Creatures, Position, Buildings, Perceivable);
```
and update `Apply()` to flip Perceivable too: `Perceivable.Update(0);`. Then change `WitnessShout_CarriesTheMonsterKind` to seed the killer's appearance and assert it is carried (rename to `…CarriesTheKillersKind`):

```csharp
            r.Perceivable.Seed(killer, AtomName.Drifter.ToId(), Fixed.One);   // this killer is a Drifter
            // ... existing: creature + sensed + DeathEvent ...
            var shout = shouts.Single(u => u.Speaker == witness);
            Assert.NotNull(shout.SubjectKind);
            Assert.Contains(shout.SubjectKind.Atoms, a => a.Type == AtomName.Drifter.ToId());  // names the ACTUAL kind
```

> The killer must be registered both in `Creatures` (the `_creatures.Contains` gate) and in `Perceivable` (so `Signature` is non-empty). Keep the existing `CreatureSetIntent`/`Apply` for the creature; add the `Perceivable.Seed`.

- [ ] **Step 2: Run it — expect FAIL** (compile: the ctor has no `PerceivableRegistry`)

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~PlaceDangerTests"`
Expected: FAIL (does not compile).

- [ ] **Step 3a: Add `PerceivableRegistry` to `PlaceDangerSystem`** — the field, ctor param, and assignment:

```csharp
        readonly SensedRegistry _sensed;
        readonly CreatureRegistry _creatures;
        readonly PositionRegistry _position;
        readonly BuildingRegistry _buildings;
        readonly PerceivableRegistry _perceivable;

        static readonly Fixed Severity = Fixed.One;

        public PlaceDangerSystem(EventBus events, SensedRegistry sensed, CreatureRegistry creatures,
            PositionRegistry position, BuildingRegistry buildings, PerceivableRegistry perceivable) : base(events)
        {
            _sensed = sensed; _creatures = creatures; _position = position; _buildings = buildings; _perceivable = perceivable;
        }
```

- [ ] **Step 3b: The shout carries the killer's actual kind** — replace the hardcoded `SubjectKind` (~60). Compute the killer's signature once per death (outside the witness loop, after the `building` check):

```csharp
                int building = NearestBuilding(d.Entity);
                if (building < 0) continue;
                AtomBag killerKind = _perceivable.Signature(d.Killer);   // the ACTUAL neutral kind (Beast/Drifter)
```
and in the shout:
```csharp
                        Content = AtomBag.Create(new[] { new Atom(PlaceAtoms.Danger, Severity) }),
                        SubjectKind = killerKind
```
Update the comment to drop the "{EnemyMonster}" wording (it now carries the killer's actual neutral kind).

- [ ] **Step 3c: Thread `Perceivable` in `SimWorld`** — at the `PlaceDangerSystem` construction (~150):

```csharp
                new PlaceDangerSystem(e, Sensed, Creatures, Position, Buildings, Perceivable),
```

- [ ] **Step 4: Run the suite — expect PASS**

Run: `cd /home/uggeli/slopfall/Headless && DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0`. The E gossip relay is unchanged — it already reinforces the hearer's category for whatever `SubjectKind` carries, so Drifter-fear now spreads by word of mouth.

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Engine/Units/PlaceDangerSystem.cs Assets/Sim/Engine/SimWorld.cs Headless/Sim.MemoryTests/PlaceDangerTests.cs
git commit -m "$(cat <<'EOF'
feat(cognition): the danger-alarm carries the killer's actual kind (F3)

PlaceDangerSystem gains a PerceivableRegistry; the witness shout sets
SubjectKind = Signature(killer) — the killer's actual neutral appearance
(Beast or Drifter) — replacing E's hardcoded {EnemyMonster}. So a witness of a
Drifter kill shouts about Drifters, and E's gossip relay spreads Drifter-fear
second-hand, trust-scaled. Generalizes the alarm off the one hardcoded kind.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5 (FT5): Multi-day soak gate — behaviour-sense

> NOT a determinism gate. The mint→learn→fear loop spans a sleep, so run **multiple days** and judge the *trajectory*.

- [ ] **Step 1: Run a multi-day soak**

```bash
cd /home/uggeli/slopfall/Headless
export DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2
SP=/tmp/claude-1000/-home-uggeli-slopfall/<session>/scratchpad   # this session's scratchpad
dotnet run --project Sim.Host/Sim.Host.csproj -c Release -- --soak Daggerfall "Gothway Garden" 3 > "$SP/phasef-gothway.txt" 2>&1
tail -20 "$SP/phasef-gothway.txt"
```

- [ ] **Step 2: Judge the regime (behaviour-sense)** — confirm:
  - **A `{Drifter}` concept forms and is learned, not innate.** After the first night's consolidation, civilians gain a *new* category (`cat/agent` rises beyond C/D/E's baseline) that starts **neutral** and drifts **aversive** over subsequent days as Drifters attack + the alarm spreads. It was never seeded.
  - **The town learns to fear a harmless-looking kind.** Drifter-directed `Flee` / avoidance is ~absent on day 1 (the Drifter reads safe) and **rises over days** as the belief deepens and the reputation cue turns it into threat — the headline payoff.
  - **The Beast contrast holds.** Beast-fear is high from day 1 (innate prior + form), unchanged by F.
  - **No regression / no artifact:** population stable across the days; **no civilian-fears-civilian** (the neutral `Civilian` appearance never accrues aversion absent harm); `maxAbs` clamped ≤ 1.0; social mix in-character; no spurious greet/dislike storm toward Drifters.

  A different-but-believable regime is success. An implausible one (pop collapse, Drifter-fear NEVER forming across 3 days, the `{Drifter}` category not minting, civilians feared, runaway) is a bug — investigate with superpowers:systematic-debugging; do NOT tune. (If the category never mints, suspect `MinClusterSupport` vs how many Drifters an agent perceives before sleeping — diagnose, don't seed.)

- [ ] **Step 3: Full green sweep + record the gate**

```bash
cd /home/uggeli/slopfall/Headless
DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release 2>&1 | tail -1
dotnet test Sim.SpatialTests/Sim.SpatialTests.csproj -c Release 2>&1 | tail -1
cd /home/uggeli/slopfall
git commit --allow-empty -m "$(cat <<'EOF'
test(cognition): soak gate — the town learns to fear what doesn't look dangerous (F-L1)

Multi-day Gothway/Gallotale soak: a {Drifter} concept MINTS at the first sleep
(never seeded), starts neutral, drifts aversive as Drifters attack + the alarm
spreads, and Drifter-directed Flee/avoidance RISES over days (the reputation cue
turning learned aversion into threat) — while Beast-fear is high from day 1
(innate+form) and civilians are never feared. The whole A-E-F arc, visible.
Behaviour-sense gate (nondeterministic, multi-day; not a byte-diff). Phase F/L1
complete.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review

**1. Spec coverage:** F0 (neutral appearance, retire the verdict atom, retrofit the monster) → FT1. F1 (the Drifter: EnemyMonster role, weapon-less, neutral appearance, no innate prior) → FT2. F2 (reputation cue: learned aversion × confidence → threat) → FT3. F3 (alarm carries the killer's actual kind) → FT4. F4 deferrals (idle belief-gossip, etc.) → not built, by design. The organic-formation property (perception → sleep-mint neutral → drift) → relied on (FT1 makes the monster recordable as Beast; the Drifter mints from perception, no seed) and validated in FT5. Honest payoff (visible via the Drifter) → FT5's "fear rises over days."

**2. Placeholder scan:** No "TBD"/"handle edge cases". Every code step shows full before/after; every run step a command + expected result. The `<session>` scratchpad token (FT5) and the "match the form-atom registration idiom" note (FT1 3b) are explicit implementer instructions, not placeholders.

**3. Type consistency:** `AtomName.Beast`/`Drifter` (FT1) are what FT2 stamps and FT4's test seeds. `InnatePrior.Kind : AtomName` (FT1) is what the `Civilian` rows and `SeedPriors` (`prior.Kind.ToId()`) use. `CreatureForms.Drifter : (AtomTypeId, Fixed)[]` (FT2) matches `Beast`'s shape. `RecognizedValence(AtomBag, out Fixed, out Fixed)` (FT3) is the real signature (the second `out` was discarded `_`, now captured). `PlaceDangerSystem(... , PerceivableRegistry)` (FT4) is the new 6-arg ctor used by `SimWorld` and the test `Rig`.

**Open flags for the implementer:**
- FT1: the form atoms' exact `BuildEntries` registration idiom — match it so `Beast`/`Drifter` resolve to `AtomCategory.Kind` + `IsIdentity` + round-trip via `NameOf`. The `AppearanceAtomTests` is the guard.
- FT1: `InnatePrior` is a struct with a positional ctor — change the `Kind` field type *and* the ctor param together; the `Self` rows pass `AtomName.None`.
- FT2: the variant is keyed on the existing spawn hash `h` (`Math.Random` is unavailable). If `h` isn't in scope at the rewritten block, hoist its existing computation above the `IdentitySetIntent`.
- FT4: the killer must be in BOTH `Creatures` (the gate) and `Perceivable` (for `Signature`) in the test; seed both.
- FT5: if the `{Drifter}` category never mints in 3 days, that's a consolidation-support diagnosis (`MinClusterSupport` vs perceptions-before-sleep) — investigate, never seed the category (that would defeat the "formed in the perceiver" point).
