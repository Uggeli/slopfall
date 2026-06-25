# Cognitive Phase B / L1 — The Affect Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make an agent's *threat* read perceived (tone × size, prey-veto, off the `_creatures` oracle) instead of an omniscient type-check, fill Phase A's reserved catalog `Tone` face, and land the kept-core cleanup (retire the role-scalar stack, de-dup `Interpret`, fix two percept bugs).

**Architecture:** Creatures stamp honest *form* atoms (`Fanged`/`Fast` weapons + a graded `Size`) carrying aversive **tone** in `AtomCatalog`. `SubjectiveSystem.Interpret` reads `Threat` = the **prey-veto** = `max` over aversive **cues** (L1 cue = `weaponTone × size`), replacing `creatures.Contains(other)`. Proximity/arousal stay in the existing fear controller (no double-count). The role-scalar `MeaningsSystem` stack is deleted and the two `Interpret`s de-duped.

**Tech Stack:** C# on **.NET 10** (`net10.0`, modern C#), xUnit 2.9.3, fixed-point `Fixed` math (the Memory subsystem is float-free; tone is `Fixed`, read into `double`-land in the Engine).

## Global Constraints

- **.NET 10 / modern C#** — match each file's surrounding style. New `Memory` types use block-scoped namespaces + `readonly struct`. `Nullable` is `disable` (no `?`/`!` annotations). No warnings-as-errors, but keep the build at **0 warnings**.
- **Atom identity is `AtomName`** — `(int)AtomName == AtomTypeId.Value`; convert with `name.ToId()` (extension in `AtomNames`). Form atoms are **direct members** (no `From(...)` overload), like `SomaticHunger`.
- **Tone lives in `AtomCatalog` only** — never on `AtomName`. `AtomTone` is `(Fixed Valence, Fixed Arousal)`; `default(AtomTone)` == `AtomTone.Neutral` == `(0,0)`. Negative valence = aversive. Tones are **frozen placeholders** (no tuning pass now).
- **No verdict atoms / no oracle in the agent's read** — threat comes from perceived tone, never `creatures.Contains`. `PlaceDangerSystem` and the metrics' creature-kill attribution legitimately keep `_creatures` (bookkeeping, not the agent's read) — do not touch them.
- **Proximity stays in the fear controller** (`NeedsSystem.FearLevel` `clarity = 1 − d/12`); `Interpret` produces the *intrinsic, proximity-free* capacity.
- **Build/test from `/home/uggeli/slopfall/Headless`.** Affected project: `Sim.MemoryTests` (xUnit). Confirm the green baseline at execution start (post-Phase-A it was **238** passing); each task reports the new count, `Failed: 0`.
- **Soak validates behaviour-SENSE, not byte-determinism** — the parallel soak is nondeterministic run-to-run (see memory `validate-sim-by-behavior-sense`). Never diff soak output for equality; judge whether behaviour stays believable and in-regime.
- **Commit style:** conventional commits, scope `affect` (e.g. `feat(affect): …`). End every commit body with:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

## Parallelism map (for distributing across agents)

```
Task 0  reference soak (controller, no commit)
        │
        ▼  ── WAVE 1 (independent files — dispatch CONCURRENTLY) ──
Task 1 (B6a SomaticPerceptSystem)   Task 2 (B6b SenseSystem)   Task 3 (B1 atoms+catalog tones)
        │                                                              │
        └──────────────────────────────────────────────┐             ▼
                                                        Task 4 (B2 CreatureForms + spawn stamp)  ── needs Task 3
                                                                       │
        ▼  ── WAVE 3 (all edit SubjectiveSystem.Interpret — SEQUENTIAL) ──
Task 5 (B3 the affect read)  →  Task 6 (B4 retire role-scalar)  →  Task 7 (B5 de-dup Interpret)
                                                                       │
                                                                       ▼
Task 8  soak gate (controller, behaviour-sense)
```

- **Genuinely parallel:** Tasks 1, 2, 3 (different files, no shared edits) — three agents at once.
- **Then** Task 4 (after Task 3 — needs the `Fanged`/`Fast`/`Size` atoms).
- **Sequential:** Tasks 5→6→7 all edit `SubjectiveSystem.Interpret` (and its signature/callers), so they cannot run concurrently. Task 5 needs Tasks 3+4 (creatures must be perceivable before the oracle is dropped) and benefits from Tasks 1+2 (clean percept inputs).

---

## File Structure

**New files:**
- `Assets/Sim/Engine/Units/CreatureForms.cs` — frozen kind→form-atom table (Task 4).
- `Headless/Sim.MemoryTests/ThreatReadTests.cs` — the prey-veto / size-modulation unit tests (Task 5).
- `Headless/Sim.MemoryTests/SenseClearTests.cs` — indoor sensed-list clear (Task 2).
- `Headless/Sim.MemoryTests/CreatureFormsTests.cs` — the form table (Task 4).

**Modified:**
- `Assets/Sim/Memory/AtomName.cs` (Task 3), `Assets/Sim/Memory/AtomCatalog.cs` (Task 3).
- `Assets/Sim/Engine/Units/SomaticPerceptSystem.cs` (Task 1), `SenseSystem.cs` (Task 2), `CreatureSystem.cs` (Task 4), `SubjectiveSystem.cs` (Tasks 5,6), `OddSystem.cs` (Tasks 6,7), `SimWorld.cs` (Tasks 6,7).
- Tests: `AtomCatalogTests.cs` (Task 3), `SomaticPerceptTests.cs` (Task 1).

**Deleted (Task 6):** `Assets/Sim/Engine/Units/MeaningsSystem.cs`, `Assets/Sim/Engine/Units/MeaningsRegistry.cs`, `Assets/Sim/Registries/MeaningsRegistry.cs`, `Headless/Sim.MemoryTests/LearnedValenceBlendTests.cs`.

---

## Task 0: Capture a behaviour-sense reference soak

**Files:** Create `…/scratchpad/phaseb-reference-gothway.txt` (scratchpad, no commit).

> The soak is nondeterministic and Phase B intentionally *changes* behaviour, so this is a **regime reference** to judge sensibility against — not a byte baseline.

- [ ] **Step 1: Confirm green + capture the pre-Phase-B regime**

```bash
cd /home/uggeli/slopfall/Headless
export DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2
dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release 2>&1 | tail -1   # expect Failed: 0 (≈238)
dotnet run --project Sim.Host/Sim.Host.csproj -c Release -- --soak Daggerfall "Gothway Garden" 1 \
  > /tmp/claude-1000/-home-uggeli-slopfall/e43f4db7-6f44-4f0f-87be-a3bcdcb77244/scratchpad/phaseb-reference-gothway.txt 2>&1
tail -3 /tmp/claude-1000/-home-uggeli-slopfall/e43f4db7-6f44-4f0f-87be-a3bcdcb77244/scratchpad/phaseb-reference-gothway.txt
```
Expected: a `SOAK DONE — pop …` line; note the regime — pop trajectory, `Flee`/`kills` counts, the activity mix. This is the "looks like a plausible town" reference.

---

## Task 1 (B6a): Somatic clear-on-zero  ·  WAVE 1, parallel-safe

**Files:**
- Modify: `Assets/Sim/Engine/Units/SomaticPerceptSystem.cs` (the private `Stamp` helper, ~lines 32-40)
- Test: `Headless/Sim.MemoryTests/SomaticPerceptTests.cs`

**Interfaces:**
- Consumes: `ClearAtomIntent { EntityId Entity; AtomTypeId Type; }` (already defined; `PerceivableRegistry.Update` applies it).
- Produces: no signature change — `Stamp(EntityId, AtomTypeId, double)` now clears on `value ≤ 0`.

- [ ] **Step 1: Write the failing test** — append to `SomaticPerceptTests.cs` (the `Rig` class already exists in this file):

```csharp
        [Fact]
        public void Hunger_Cleared_WhenNeedReturnsToZero()
        {
            var r = new Rig();
            r.SetHunger(1, 0.7);
            r.Step(0); r.Step(1);                                   // stamped
            Assert.True(r.Perceivable.Bag(new EntityId(1)).Contains(SomaticAtoms.Hunger));

            r.SetHunger(1, 0.0);                                    // need recovered
            for (long t = 2; t < 14 && r.Perceivable.Bag(new EntityId(1)).Contains(SomaticAtoms.Hunger); t++)
                r.Step(t);                                          // step past the next sense-tick; clear publishes + applies
            Assert.False(r.Perceivable.Bag(new EntityId(1)).Contains(SomaticAtoms.Hunger));
        }
```

- [ ] **Step 2: Run it — expect FAIL**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~SomaticPerceptTests.Hunger_Cleared_WhenNeedReturnsToZero"`
Expected: FAIL (the atom lingers — `Assert.False` fails).

- [ ] **Step 3: Fix `Stamp`** — in `SomaticPerceptSystem.cs`, replace:

```csharp
        void Stamp(EntityId id, AtomTypeId type, double value)
        {
            if (value <= 0) return;
            Events.Publish(new StampAtomIntent
            {
                Entity = id, Type = type,
                Value = Fixed.FromDouble(value > 1.0 ? 1.0 : value),
            });
        }
```
with:
```csharp
        void Stamp(EntityId id, AtomTypeId type, double value)
        {
            if (value <= 0)
            {
                // The need recovered — actively clear the stale somatic atom (was: linger forever).
                Events.Publish(new ClearAtomIntent { Entity = id, Type = type });
                return;
            }
            Events.Publish(new StampAtomIntent
            {
                Entity = id, Type = type,
                Value = Fixed.FromDouble(value > 1.0 ? 1.0 : value),
            });
        }
```

- [ ] **Step 4: Run the suite — expect PASS**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0` (baseline + 1).

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Engine/Units/SomaticPerceptSystem.cs Headless/Sim.MemoryTests/SomaticPerceptTests.cs
git commit -m "$(cat <<'EOF'
fix(affect): clear somatic atom when the need returns to zero (B6a)

SomaticPerceptSystem.Stamp early-returned on value<=0, leaving a stale
Hunger/Energy/Fear atom in the perceivable bag forever — corrupting what
Interpret reads. Publish a ClearAtomIntent instead.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2 (B6b): Indoor sensed-list clear  ·  WAVE 1, parallel-safe

**Files:**
- Modify: `Assets/Sim/Engine/Units/SenseSystem.cs` (`Update`, after the sensing loop ~line 88; add a field)
- Test: Create `Headless/Sim.MemoryTests/SenseClearTests.cs`

**Interfaces:**
- Consumes: `SensedSetIntent { EntityId Id; List<EntityId> Sensed; }` (existing); `BehaviorData{Phase,Activity}`; `IsOutAndAbout` (existing private).
- Produces: no signature change — indoor (`!IsOutAndAbout`) non-creature agents now get an empty `SensedSetIntent` each sense-tick.

- [ ] **Step 1: Write the failing test** — create `SenseClearTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class SenseClearTests
    {
        sealed class Rig
        {
            public readonly EventBus E = new EventBus();
            public readonly WorldClockRegistry Clock;
            public readonly BehaviorRegistry Behavior;
            public readonly PositionRegistry Position;
            public readonly CreatureRegistry Creatures;
            public readonly TownGridRegistry Grid;
            public readonly SensedRegistry Sensed;
            public readonly SenseSystem System;

            public Rig()
            {
                Clock = new WorldClockRegistry(E);
                Behavior = new BehaviorRegistry(E);
                Position = new PositionRegistry(E);
                Creatures = new CreatureRegistry(E);
                Grid = new TownGridRegistry(E);                 // null grid → no occlusion checks
                Sensed = new SensedRegistry(E);
                System = new SenseSystem(E, Clock, Behavior, Position, Creatures, Grid, seed: 42);
                E.Publish(new WorldClockSetIntent { Year = 405, Month = 1, Day = 1, Hour = 8,
                    Minute = 0, Second = 0, TimeScale = 12f, DeltaGameSeconds = 1.2 });
                E.Tick(); Clock.Update(0);
            }

            public void Place(int id, float x, float z)
                => E.Publish(new PositionSetIntent { Id = new EntityId(id), X = x, Y = 0f, Z = z, Yaw = 0f });
            public void Act(int id, ActivityPhase phase, ActivityKind a)
                => E.Publish(new BehaviorSetIntent { Id = new EntityId(id), Data = new BehaviorData { Phase = phase, Activity = a } });
            public void Step(long t)
            {
                E.Tick();
                Clock.Update(t); Behavior.Update(t); Position.Update(t); Creatures.Update(t);
                Grid.Update(t); Sensed.Update(t); System.Update(t);
            }
        }

        [Fact]
        public void IndoorAgent_SensedList_IsCleared()
        {
            var r = new Rig();
            r.Place(1, 0f, 0f); r.Place(2, 1f, 0f);
            r.Act(1, ActivityPhase.Moving, ActivityKind.Wander);
            r.Act(2, ActivityPhase.Moving, ActivityKind.Wander);
            r.Step(0);                                          // sense-tick (0 % 5 == 0): agent 1 senses agent 2
            r.Step(1);                                          // Sensed applies
            Assert.Contains(new EntityId(2), r.Sensed.Of(new EntityId(1)));

            r.Act(1, ActivityPhase.Doing, ActivityKind.EatTavern);   // agent 1 goes indoors
            r.Step(5);                                          // next sense-tick: indoor → empty SensedSetIntent
            r.Step(6);                                          // applies
            Assert.Empty(r.Sensed.Of(new EntityId(1)));
        }
    }
}
```

- [ ] **Step 2: Run it — expect FAIL**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~SenseClearTests"`
Expected: FAIL (agent 1's stale `[2]` list persists indoors — `Assert.Empty` fails).

- [ ] **Step 3: Add the indoor-clear** — in `SenseSystem.cs`, add a field near the other consts (after line 24):

```csharp
        static readonly List<EntityId> NoneSensed = new List<EntityId>();   // shared empty (never mutated)
```
and at the end of `Update`, immediately after the `foreach (var kv in buckets)` sensing loop closes (after line 88, before the method's closing brace), add:

```csharp
            // Indoor agents are skipped from the buckets above, so without this their last street
            // SensedSetIntent goes stale ("seeing" people who aren't there). Clear them each sense-tick.
            foreach (var kv in _behavior.All)
                if (!IsOutAndAbout(kv.Value) && !_creatures.Contains(kv.Key))
                    Events.Publish(new SensedSetIntent { Id = kv.Key, Sensed = NoneSensed });
```

- [ ] **Step 4: Run the suite — expect PASS**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0`.

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Engine/Units/SenseSystem.cs Headless/Sim.MemoryTests/SenseClearTests.cs
git commit -m "$(cat <<'EOF'
fix(affect): clear an indoor agent's stale sensed-list (B6b)

SenseSystem only built a view for IsOutAndAbout agents, so an agent that
went indoors kept its last street SensedSetIntent and "saw" people who
weren't there — corrupting Interpret/fear. Publish an empty SensedSetIntent
for indoor non-creature agents each sense-tick.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3 (B1): Atoms + catalog tones  ·  WAVE 1, parallel-safe

**Files:**
- Modify: `Assets/Sim/Memory/AtomName.cs`, `Assets/Sim/Memory/AtomCatalog.cs`
- Test: `Headless/Sim.MemoryTests/AtomCatalogTests.cs`

**Interfaces:**
- Produces: `AtomName.Fanged`, `AtomName.Fast`, `AtomName.Size`; `AtomCategory.Form`; their catalog entries with tone (`Fanged ≈ (−0.8,0.9)`, `Fast ≈ (−0.4,0.6)`, `Size` neutral). `AtomCatalog.For(name).Tone` now non-neutral for weapons. Consumed by Tasks 4 (stamp) and 5 (`ThreatRead`).

- [ ] **Step 1: Write the failing test** — append to `AtomCatalogTests.cs`:

```csharp
        [Fact]
        public void Catalog_FormAtoms_HaveExpectedToneAndCategory()
        {
            AtomEntry fanged = AtomCatalog.For(AtomName.Fanged);
            Assert.Equal(AtomCategory.Form, fanged.Category);
            Assert.False(fanged.IsIdentity);                                   // not a recognition-signature atom
            Assert.True(fanged.Tone.Valence.ToDouble() < 0.0, "Fanged is aversive");
            Assert.True(fanged.Tone.Arousal.ToDouble() > 0.0, "Fanged is alarming");

            // Fanged is the more aversive weapon than Fast.
            Assert.True(AtomCatalog.For(AtomName.Fanged).Tone.Valence.ToDouble()
                      < AtomCatalog.For(AtomName.Fast).Tone.Valence.ToDouble());

            // Size is a neutral modulator (no tone of its own).
            AtomEntry size = AtomCatalog.For(AtomName.Size);
            Assert.Equal(AtomCategory.Form, size.Category);
            Assert.Equal(Fixed.Zero, size.Tone.Valence);
            Assert.Equal(Fixed.Zero, size.Tone.Arousal);
        }
```

- [ ] **Step 2: Run it — expect FAIL** (compile error: `AtomName.Fanged`/`AtomCategory.Form` don't exist)

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~AtomCatalogTests.Catalog_FormAtoms_HaveExpectedToneAndCategory"`
Expected: FAIL (does not compile).

- [ ] **Step 3a: Add the form atoms** — in `AtomName.cs`, after the `PlaceProvisions, PlaceDanger,` line (line 49), before the closing `}`:

```csharp

        // ── Form atoms (descriptive properties: weapons, body) — Phase B; no From overload,
        //    stamped directly via AtomName.X.ToId(). Tone lives in AtomCatalog. ──
        Fanged, Fast, Size,
```

- [ ] **Step 3b: Add `Form` to `AtomCategory`** — in `AtomCatalog.cs`, the enum (lines 7-13) becomes:

```csharp
    public enum AtomCategory
    {
        None,
        Kind, Role, Race,          // identity
        Activity, Somatic,         // transient state
        PlaceKind, PlaceProvisions, PlaceDanger,
        Form,                      // descriptive form atoms (weapons, body) — Phase B
    }
```

- [ ] **Step 3c: Let `Put` carry a tone** — in `AtomCatalog.cs` `BuildEntries`, change the `Put` helper (lines 76-80) to:

```csharp
            void Put(AtomName n, AtomCategory c, AtomMeta sal, bool share, AtomTone tone = default)
            {
                if (n == AtomName.None) return;                       // sentinels carry no entry
                m[n] = new AtomEntry(c, sal, share, tone);            // default(AtomTone) == Neutral
            }
```

- [ ] **Step 3d: Populate the form atoms** — in `BuildEntries`, after the `Put(AtomName.PlaceDanger, …)` line (line 99), before `return m;`:

```csharp
            // Form atoms (Phase B): weapons carry aversive tone; Size is a neutral modulator (B1).
            Put(AtomName.Fanged, AtomCategory.Form, ordinary, false,
                new AtomTone(Fixed.FromDouble(-0.8), Fixed.FromDouble(0.9)));   // FROZEN placeholders
            Put(AtomName.Fast,   AtomCategory.Form, ordinary, false,
                new AtomTone(Fixed.FromDouble(-0.4), Fixed.FromDouble(0.6)));
            Put(AtomName.Size,   AtomCategory.Form, ordinary, false);           // neutral — modulates weapon cues
```

- [ ] **Step 3e: Register them in the reverse map** — in `BuildReverse`, after the `Add(PlaceAtoms.Danger, …)` line (line 135), before `return m;`:

```csharp
            // Form atoms — direct members (no source-enum helper); stamped via AtomName.X.ToId().
            Add(AtomName.Fanged.ToId(), AtomName.Fanged);
            Add(AtomName.Fast.ToId(),   AtomName.Fast);
            Add(AtomName.Size.ToId(),   AtomName.Size);
```

- [ ] **Step 4: Run the suite — expect PASS**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0`. (The existing `Catalog_HasEntry_ForEveryAtomName` now also covers Fanged/Fast/Size; `Catalog_Tone_IsNeutral_InPhaseA` for PlaceDanger still passes — only form atoms got tone.)

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Memory/AtomName.cs Assets/Sim/Memory/AtomCatalog.cs Headless/Sim.MemoryTests/AtomCatalogTests.cs
git commit -m "$(cat <<'EOF'
feat(affect): form atoms Fanged/Fast/Size + fill the catalog Tone face (B1)

Add the AtomCategory.Form family and the first tone-bearing atoms: Fanged
(-0.8/0.9) and Fast (-0.4/0.6) weapons, and a neutral graded Size modulator.
Fills Phase A's reserved AtomEntry.Tone cell. Form atoms are non-identity
(stay out of the recognition Signature). Tones are frozen placeholders.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4 (B2): CreatureForms table + creature-spawn stamp  ·  needs Task 3

**Files:**
- Create: `Assets/Sim/Engine/Units/CreatureForms.cs`
- Modify: `Assets/Sim/Engine/Units/CreatureSystem.cs` (`TrySpawn`, after the `CreatureSetIntent` publish ~line 228)
- Test: Create `Headless/Sim.MemoryTests/CreatureFormsTests.cs`

**Interfaces:**
- Consumes: `AtomName.Fanged/Fast/Size` + `.ToId()` (Task 3); `StampAtomIntent { Entity, Type, Value }`.
- Produces: `CreatureForms.Beast : (AtomTypeId Type, Fixed Value)[]` — the per-kind form bag (one row today). Creatures gain a perceivable bag at spawn.

- [ ] **Step 1: Write the failing test** — create `CreatureFormsTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class CreatureFormsTests
    {
        [Fact]
        public void Beast_CarriesFangedFastAndAGradedSize()
        {
            var bag = CreatureForms.Beast;
            // weapons present, full presence value
            Assert.Contains(bag, f => f.Type == AtomName.Fanged.ToId() && f.Value == Fixed.One);
            Assert.Contains(bag, f => f.Type == AtomName.Fast.ToId()   && f.Value == Fixed.One);
            // size graded in (0,1)
            var size = bag.Single(f => f.Type == AtomName.Size.ToId());
            Assert.True(size.Value.ToDouble() > 0.0 && size.Value.ToDouble() < 1.0);
        }
    }
}
```

- [ ] **Step 2: Run it — expect FAIL** (compile: `CreatureForms` doesn't exist)

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~CreatureFormsTests"`
Expected: FAIL (does not compile).

- [ ] **Step 3a: Create `CreatureForms.cs`:**

```csharp
using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>
    /// Per-creature-kind FORM atoms (weapons + body size) a creature broadcasts, so a perceiver can
    /// read threat from them (tone × size, in SubjectiveSystem). L1 has one creature kind (the generic
    /// Beast), so one row; this table gains rows — with their own sizes (Squirrel small, Bear large) —
    /// when monster-type variety is introduced. Frozen, beside the other catalogs.
    /// </summary>
    public static class CreatureForms
    {
        // (atom, value): presence weapons at Fixed.One; Size graded (0..1). Frozen placeholders.
        public static readonly (AtomTypeId Type, Fixed Value)[] Beast =
        {
            (AtomName.Fanged.ToId(), Fixed.One),
            (AtomName.Fast.ToId(),   Fixed.One),
            (AtomName.Size.ToId(),   Fixed.FromDouble(0.5)),
        };
    }
}
```

- [ ] **Step 3b: Stamp on spawn** — in `CreatureSystem.cs` `TrySpawn`, immediately after the `Events.Publish(new CreatureSetIntent { … });` line (~line 228), before `return true;`:

```csharp
            // Phase B/L1: stamp the creature's perceivable form atoms (weapons + size) so a perceiver
            // reads threat from them (tone × size) — off the _creatures oracle. Beast: one form row.
            foreach (var form in CreatureForms.Beast)
                Events.Publish(new StampAtomIntent { Entity = id, Type = form.Type, Value = form.Value });
```

- [ ] **Step 4: Run the suite — expect PASS**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0`.

> The end-to-end "a spawned creature actually has these atoms" is exercised by the Task 8 soak (civilians come to fear monsters *via perception*, which requires the stamp). `TrySpawn` is private and internally triggered, so the unit gate here is the form table; Task 5's behavioural test proves the read off a stamped bag.

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Engine/Units/CreatureForms.cs Assets/Sim/Engine/Units/CreatureSystem.cs Headless/Sim.MemoryTests/CreatureFormsTests.cs
git commit -m "$(cat <<'EOF'
feat(affect): creatures broadcast form atoms at spawn (B2)

CreatureForms: a frozen per-kind table (one row today, Beast ->
{Fanged, Fast, Size=0.5}) — the per-kind architecture, extensible as
monster types arrive. CreatureSystem.TrySpawn stamps them, so creatures
finally have a perceivable bag (empty before).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5 (B3): The affect read — prey-veto over cues, off the oracle  ·  SEQUENTIAL (needs Tasks 3,4)

**Files:**
- Modify: `Assets/Sim/Engine/Units/SubjectiveSystem.cs` (add `ThreatRead`; rewrite `Interpret`'s threat path; **signature unchanged**)
- Test: Create `Headless/Sim.MemoryTests/ThreatReadTests.cs`

**Interfaces:**
- Consumes: `AtomCatalog.For(atom).Tone` (Task 3); `AtomName.Size.ToId()`; `AtomBag` (`perceivable.Bag(other)`).
- Produces: `static double SubjectiveSystem.ThreatRead(AtomBag bag, out double threatValence)` (the prey-veto). `Interpret` now sets `Threat`/`Valence` from it, **not** `creatures.Contains`. Signature still `Interpret(creatures, relations, affects, meanings, behavior, personality, residency, perceivable, agentMem, self, other)` (the now-unused `creatures`/`meanings`/`residency` params are dropped in Task 6).

- [ ] **Step 1: Write the failing tests** — create `ThreatReadTests.cs`:

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
            double t = SubjectiveSystem.ThreatRead(Bag((AtomName.Fanged, 1.0), (AtomName.Size, 0.5)), out double v);
            Assert.True(t > 0.0 && t <= 1.0);
            Assert.True(v < 0.0);                                   // a thing that scares you reads aversive
        }

        [Fact]
        public void Veto_IsTheMaxCue_FangedDominatesFast()
        {
            double both = SubjectiveSystem.ThreatRead(Bag((AtomName.Fanged, 1.0), (AtomName.Fast, 1.0), (AtomName.Size, 0.5)), out _);
            double fastOnly = SubjectiveSystem.ThreatRead(Bag((AtomName.Fast, 1.0), (AtomName.Size, 0.5)), out _);
            Assert.True(both > fastOnly);                           // Fanged (worse) sets the veto
        }

        [Fact]
        public void Size_Modulates_SquirrelIsNearlyHarmlessBearIsScary()
        {
            double squirrel = SubjectiveSystem.ThreatRead(Bag((AtomName.Fanged, 1.0), (AtomName.Size, 0.1)), out _);
            double bear     = SubjectiveSystem.ThreatRead(Bag((AtomName.Fanged, 1.0), (AtomName.Size, 0.9)), out _);
            Assert.True(squirrel < bear);
            Assert.True(squirrel < 0.2);                            // same fangs, tiny body → ~harmless
        }

        [Fact]
        public void NeutralBag_ReadsNoThreat()
        {
            double t = SubjectiveSystem.ThreatRead(Bag((AtomName.Civilian, 1.0), (AtomName.Resident, 1.0)), out double v);
            Assert.Equal(0.0, t);
            Assert.Equal(0.0, v);
        }
    }
}
```

- [ ] **Step 2: Run them — expect FAIL** (compile: `ThreatRead` doesn't exist)

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~ThreatReadTests"`
Expected: FAIL (does not compile).

- [ ] **Step 3a: Add the `ThreatRead` helper** — in `SubjectiveSystem.cs`, near the other `const`s / static helpers (just below `BlendValence`, ~line 85), add:

```csharp
        const double SizeMassMenace = 0.3;   // FROZEN: raw menace a large body carries even unarmed

        /// <summary>The prey-veto: Threat = max over aversive CUES read from the perceived bag.
        /// L1 cues = capacity (each weapon's aversive tone x perceived Size) + raw size mass-menace.
        /// Built as a max over a cue list so the L2 observed-behaviour cue and the Phase-C/D reputation
        /// cue drop in as further entries (behaviour can make a low-capacity thing scary). NO oracle.</summary>
        public static double ThreatRead(AtomBag bag, out double threatValence)
        {
            threatValence = 0;
            if (bag == null || bag.Count == 0) return 0;
            bool hasSize = bag.TryGet(AtomName.Size.ToId(), out var sizeFx);
            double size = hasSize ? sizeFx.ToDouble() : 1.0;          // 1.0 = no shrink (only creatures carry Size)
            double threat = 0;
            for (int i = 0; i < bag.Count; i++)                       // capacity cues: weapon tone x size
            {
                AtomTone tone = AtomCatalog.For(bag[i].Type).Tone;
                double menace = -tone.Valence.ToDouble();             // > 0 only for an aversive (weapon) atom
                if (menace <= 0) continue;
                double cue = menace * tone.Arousal.ToDouble() * size;
                if (cue > threat) { threat = cue; threatValence = tone.Valence.ToDouble(); }
            }
            if (hasSize)                                             // raw mass-menace cue (sized things only)
            {
                double mass = SizeMassMenace * size;
                if (mass > threat) { threat = mass; threatValence = -mass; }
            }
            if (threat > 1.0) threat = 1.0;
            if (threatValence < -1.0) threatValence = -1.0;
            return threat;
        }
```

- [ ] **Step 3b: Rewrite `Interpret`'s threat path** — in `SubjectiveSystem.cs`, replace the creature early-return block at the top of `Interpret` (the `if (creatures.Contains(other)) return new EntityRead { … Threat = 1.0, };` block, lines 99-108) with a leading threat read, and apply the prey-veto + Attention boost in the final `return`. The method body becomes:

```csharp
        public static EntityRead Interpret(
            CreatureRegistry creatures, RelationsRegistry relations, AffectsRegistry affects,
            MeaningsRegistry meanings, BehaviorRegistry behavior, PersonalityRegistry personality,
            ResidencyRegistry residency, PerceivableRegistry perceivable, AgentMemoryRegistry agentMem,
            EntityId self, EntityId other)
        {
            // Threat = the prey-veto over perceived aversive cues (weapon tone x size). NO _creatures oracle.
            double threat = 0, threatValence = 0;
            if (perceivable != null)
                threat = ThreatRead(perceivable.Bag(other), out threatValence);

            double familiarity = 0, baseValence;
            if (relations.TryGet(self, out var rels) && rels.Of.TryGetValue(other, out var rel))
            {
                baseValence = rel.Regard;          // I know THEM — judge by my history (dossier)
                familiarity = rel.Familiarity;
            }
            else
            {
                // A stranger — judge by their KIND. Blend the old role-scalar with the agent's learned
                // category, weighted by confidence. (The role-scalar half is retired in B4.)
                double oldV = MeaningsSystem.CategoryValence(meanings, residency, self, other);
                double newV = 0, conf = 0;
                if (agentMem != null && perceivable != null
                    && agentMem.TryGet(self, out var mem)
                    && mem.Meanings.RecognizedValence(perceivable.Signature(other), out var lv, out var lc))
                { newV = lv.ToDouble(); conf = lc.ToDouble(); }
                baseValence = BlendValence(oldV, newV, conf);
            }
            double valence = baseValence + affects.ValenceToward(self, other);
            // beggar-stigma: a beggar is read through the perceiver's Warmth (kept; B7).
            if (behavior.TryGet(other, out var ob) && ob != null
                && ob.Phase == ActivityPhase.Doing && ob.Activity == ActivityKind.Beg
                && personality.TryGet(self, out var p) && p != null)
                valence += (p.Trait(TraitIndex.Warmth) - 0.5) * StigmaScale;

            // The prey-veto: a perceived threat forces the read aversive, overriding the social blend.
            if (threat > 0 && threatValence < valence) valence = threatValence;

            return new EntityRead
            {
                Other = other,
                Valence = valence,
                Recognition = familiarity,
                Trust = familiarity,
                Attention = 0.1 + System.Math.Abs(valence) + familiarity + threat,   // a threat grabs attention
                Threat = threat,
            };
        }
```

> The `creatures` parameter is now unused (the oracle is gone). Leave it in the signature for this task; Task 6 drops it. `ThreatSightRadius`/`CompleteThreat`/`clarity` in `NeedsSystem` are untouched — proximity stays there.

- [ ] **Step 4: Run the suite — expect PASS**

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0` (ThreatReadTests pass; existing tests unaffected — no signature change).

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Engine/Units/SubjectiveSystem.cs Headless/Sim.MemoryTests/ThreatReadTests.cs
git commit -m "$(cat <<'EOF'
feat(affect): perceived threat read — prey-veto over weapon x size cues (B3)

SubjectiveSystem.Interpret stops using creatures.Contains and reads Threat
from the perceived bag: ThreatRead = max over aversive cues (each weapon's
tone x perceived Size, + raw size mass-menace). Size modulates the weapon
(squirrel ~harmless, bear scary); the veto forces Valence aversive; a threat
keeps its attention boost. Built max-over-cues so L2 behaviour / C-D
reputation cues drop in later. creatures param now unused (dropped in B4).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6 (B4): Retire the role-scalar stack  ·  SEQUENTIAL (needs Task 5)

**Files:**
- Delete: `Assets/Sim/Engine/Units/MeaningsSystem.cs`, `Assets/Sim/Engine/Units/MeaningsRegistry.cs`, `Assets/Sim/Registries/MeaningsRegistry.cs`, `Headless/Sim.MemoryTests/LearnedValenceBlendTests.cs`
- Modify: `Assets/Sim/Engine/Units/SubjectiveSystem.cs` (drop `creatures`/`meanings`/`residency` params; stranger valence = learned-store only; delete `BlendValence`; update `Update`'s `Interpret` call + ctor), `Assets/Sim/Engine/Units/OddSystem.cs` (drop `_meanings` field/param/assign — keep its *local* `Interpret`/`CategoryValence` for now; Task 7 removes them), `Assets/Sim/Engine/SimWorld.cs` (remove the `Meanings` registry + `MeaningsSystem` + ctor args)

**Interfaces:**
- Produces: the final `SubjectiveSystem.Interpret(RelationsRegistry relations, AffectsRegistry affects, BehaviorRegistry behavior, PersonalityRegistry personality, PerceivableRegistry perceivable, AgentMemoryRegistry agentMem, EntityId self, EntityId other)` — 6 registries (dropped `creatures`, `meanings`, `residency`). Consumed by Task 7's `OddSystem` reroute.

> **Note (softens the spec):** `MemoryReinforceSystem` feeds the *new* memory-core store from interactions (`MemoryReinforceSystem.cs:7`), so after retiring the old stack social valence is **learned via the new path / `RecognizedValence`**, not permanently neutral. Verify the social regime in the Task 8 soak.

- [ ] **Step 1: Migrate the broken test** — `LearnedValenceBlendTests.cs` tests `SubjectiveSystem.BlendValence`, which this task deletes. **Delete that test file** (the blend it tested no longer exists; the stranger path is now the learned store directly). Keep `LearnedValenceReinforceTests.cs` and `CategoryNodeTests.cs` — they test the *new* memory-core store, not the old stack.

```bash
cd /home/uggeli/slopfall
git rm Headless/Sim.MemoryTests/LearnedValenceBlendTests.cs
```

- [ ] **Step 2: Simplify `Interpret` + shrink its signature** — in `SubjectiveSystem.cs`, change the method signature and the stranger branch and delete `BlendValence`. The signature line 93-97 becomes:

```csharp
        public static EntityRead Interpret(
            RelationsRegistry relations, AffectsRegistry affects,
            BehaviorRegistry behavior, PersonalityRegistry personality,
            PerceivableRegistry perceivable, AgentMemoryRegistry agentMem,
            EntityId self, EntityId other)
```
the stranger `else` branch becomes (drop the role-scalar `oldV`, `BlendValence`, and the now-unused `conf`):

```csharp
            else
            {
                // A stranger — the agent's OWN learned category over their perceived signature
                // (fed by MemoryReinforceSystem). No role-scalar stereotype anymore.
                double newV = 0;
                if (agentMem != null && perceivable != null
                    && agentMem.TryGet(self, out var mem)
                    && mem.Meanings.RecognizedValence(perceivable.Signature(other), out var lv, out _))
                    newV = lv.ToDouble();
                baseValence = newV;
            }
```
and **delete** the `BlendValence` method (lines 81-84) and the now-unused `_meanings`/`_creatures`/`_residency`-only usages in `Interpret` (the `ThreatRead` no longer needs `creatures`; the stranger path no longer needs `meanings`/`residency`).

- [ ] **Step 3: Update the `Interpret` call + ctor in `SubjectiveSystem.Update`** — the call (lines ~183-185) becomes:

```csharp
                    var read = Interpret(_relations, _affects, _behavior, _personality,
                                         _perceivable, _agentMem, self, sensed[i]);
```
Remove the `_meanings` field (line 41), its ctor param (line 57) and assignment (line 71). `_creatures`/`_residency` may still be used elsewhere in `SubjectiveSystem` (e.g. other methods) — only remove a field if the build shows it fully unused; otherwise leave it. (Run the build to find unused-field/param errors are not emitted by C#, so rely on removing what `Interpret` no longer takes and what the compiler flags as undefined.)

- [ ] **Step 4: Delete the role-scalar files + rewire `SimWorld`** —

```bash
cd /home/uggeli/slopfall
git rm Assets/Sim/Engine/Units/MeaningsSystem.cs Assets/Sim/Engine/Units/MeaningsRegistry.cs Assets/Sim/Registries/MeaningsRegistry.cs
```
In `SimWorld.cs`: delete the `Meanings` field (line 44), its construction `Meanings = new MeaningsRegistry(e);` (line 90), its entry in the registries array (line 110), the `new MeaningsSystem(e, WorldClock, Meanings, Residency),` system (line 155), and remove `Meanings` from the `SubjectiveSystem` ctor args (lines 148-149) and the `OddSystem` ctor args.

- [ ] **Step 5: Drop `_meanings` from `OddSystem`** — in `OddSystem.cs`, remove the `_meanings` field (line 44), ctor param (line 72), and assignment (line 98). Its *local* `CategoryValence` (lines 922-927) still references `_meanings` — for THIS task, make `OddSystem.CategoryValence` return `0` (a stub) so the build is green; Task 7 deletes the local `Interpret`/`CategoryValence` entirely:

```csharp
        double CategoryValence(EntityId self, EntityId other) => 0;   // role-scalar retired (B4); Interpret de-duped in B5
```

- [ ] **Step 6: Build + run the suite — expect PASS**

Run: `cd /home/uggeli/slopfall/Headless && dotnet build --configuration Release 2>&1 | tail -3 && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release 2>&1 | tail -1`
Expected: `0 Error(s)`; `Failed: 0` (minus the deleted `LearnedValenceBlendTests`). If the build flags a leftover reference to `MeaningsSystem`/`MeaningsRegistry`/`BlendValence`, fix that call site (grep: `grep -rn "Meanings\|BlendValence" Assets Headless` should show only the memory-core `MeaningsStore`/`MeaningsConfig` and the kept `RecognizedValence`/`LearnedValenceReinforceTests`).

- [ ] **Step 7: Commit**

```bash
cd /home/uggeli/slopfall
git add -A
git commit -m "$(cat <<'EOF'
refactor(affect): retire the role-scalar meanings stack (B4)

Delete MeaningsSystem + the engine MeaningsRegistry/Data/CategoryNode +
MeaningsSetIntent + BlendValence; drop them from SimWorld's schedule and
from SubjectiveSystem/OddSystem. Interpret's stranger valence is now the
agent's learned memory-core store (RecognizedValence, fed by
MemoryReinforceSystem) only. Shrink Interpret's signature (drop the now-dead
creatures/meanings/residency params). Delete LearnedValenceBlendTests.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7 (B5): De-duplicate `Interpret`  ·  SEQUENTIAL (needs Tasks 5,6)

**Files:**
- Modify: `Assets/Sim/Engine/Units/OddSystem.cs` (`RegardFieldAt` → static `SubjectiveSystem.Interpret`; delete the local `Interpret` + `CategoryValence` + `SignatureOf`; add `_perceivable` field/param/assign), `Assets/Sim/Engine/SimWorld.cs` (pass `Perceivable` to the `OddSystem` ctor)

**Interfaces:**
- Consumes: `SubjectiveSystem.Interpret(relations, affects, behavior, personality, perceivable, agentMem, self, other)` (Task 6).
- Produces: a single `Interpret` implementation; `OddSystem` holds `PerceivableRegistry _perceivable`.

- [ ] **Step 1: Add `_perceivable` to `OddSystem`** — in `OddSystem.cs`, add the field beside the others (~line 46), a ctor param, and the assignment:

```csharp
        readonly PerceivableRegistry _perceivable;
```
ctor param (add to the constructor signature, beside `AgentMemoryRegistry agentMemory`):
```csharp
            PerceivableRegistry perceivable,
```
assignment (in the ctor body, beside `_agentMemory = agentMemory;`):
```csharp
            _perceivable = perceivable;
```

- [ ] **Step 2: Reroute `RegardFieldAt`** — in `OddSystem.cs` `RegardFieldAt` (lines 729-744), change the occupant read (line ~738) from `Interpret(c.Self, occ[i]).Valence` to the static call:

```csharp
                double v = SubjectiveSystem.Interpret(_relations, _affects, _behavior, _personality,
                                                      _perceivable, _agentMemory, c.Self, occ[i]).Valence;
```

- [ ] **Step 3: Delete the local duplicates** — in `OddSystem.cs`, delete the local `EntityRead Interpret(EntityId self, EntityId other)` (lines 879-913), the local `double CategoryValence(EntityId self, EntityId other)` (now the `=> 0` stub from Task 6), and the local `SignatureOf` (lines ~915-921, if present and now unused). Remove the now-unused `ThreatValence`/`StigmaScale` consts in `OddSystem` if they are no longer referenced.

- [ ] **Step 4: Wire `Perceivable` in `SimWorld`** — in `SimWorld.cs`, add `Perceivable` to the `new OddSystem(…)` construction args (it already exposes `Perceivable`).

- [ ] **Step 5: Build + grep gate + run the suite**

Run:
```bash
cd /home/uggeli/slopfall/Headless && dotnet build --configuration Release 2>&1 | tail -3
grep -rn "EntityRead Interpret" /home/uggeli/slopfall/Assets   # expect ONE hit: SubjectiveSystem.Interpret
dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release 2>&1 | tail -1
```
Expected: `0 Error(s)`; exactly **one** `Interpret` implementation; `Failed: 0`.

- [ ] **Step 6: Commit**

```bash
cd /home/uggeli/slopfall
git add -A
git commit -m "$(cat <<'EOF'
refactor(affect): de-duplicate Interpret onto the single static path (B5)

OddSystem.RegardFieldAt now calls the static SubjectiveSystem.Interpret
(adding the PerceivableRegistry it lacked); delete OddSystem's inline
Interpret + CategoryValence copy. One interpretation implementation, on the
perceived read.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: Soak gate — behaviour-sense (controller-run)

**Files:** Read `…/scratchpad/phaseb-reference-gothway.txt` (Task 0).

> **NOT a determinism gate.** The parallel soak is nondeterministic run-to-run, and Phase B *intentionally* changes behaviour (threat now perceived; role-scalar retired). Judge whether the result is **sensible and in-regime**, comparing the *regime* — not bytes — to the Task-0 reference.

- [ ] **Step 1: Run the post-Phase-B soak**

```bash
cd /home/uggeli/slopfall/Headless
export DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2
dotnet run --project Sim.Host/Sim.Host.csproj -c Release -- --soak Daggerfall "Gothway Garden" 1 \
  > /tmp/claude-1000/-home-uggeli-slopfall/e43f4db7-6f44-4f0f-87be-a3bcdcb77244/scratchpad/phaseb-after-gothway.txt 2>&1
dotnet run --project Sim.Host/Sim.Host.csproj -c Release -- --soak Daggerfall Gallotale 1 \
  > /tmp/claude-1000/-home-uggeli-slopfall/e43f4db7-6f44-4f0f-87be-a3bcdcb77244/scratchpad/phaseb-after-gallotale.txt 2>&1
tail -4 /tmp/claude-1000/-home-uggeli-slopfall/e43f4db7-6f44-4f0f-87be-a3bcdcb77244/scratchpad/phaseb-after-gothway.txt
```

- [ ] **Step 2: Judge the regime (behaviour-sense)** — confirm, against the Task-0 reference:
  - **Population stable** (no collapse / no explosion).
  - **Civilians still fear and flee monsters — now via perception** (`Flee` and creature-`kills` counts in a plausible ballpark; *not zero* — the perceived threat must fire — and not exploded).
  - **Guards still post and attack.**
  - **Day/night rhythm + activity mix** stays believable (work/shop by day, sleep at night).
  - **Social behaviour** is sensible despite the retired role-scalar (it may shift; with `MemoryReinforceSystem` feeding the learned store it should not go fully flat — confirm it reads as a plausible town).

  If `Flee`/kills went to **zero**, the perceived threat isn't firing — investigate (creatures unstamped? `ThreatRead` returning 0? sense-gate?), using superpowers:systematic-debugging — do NOT tune. A different-but-believable regime is success; an *implausible* one (ghost-town, mass death, monsters ignored) is a bug.

- [ ] **Step 3: Full green sweep + record the gate**

Run:
```bash
cd /home/uggeli/slopfall/Headless
DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release 2>&1 | tail -1
dotnet test Sim.SpatialTests/Sim.SpatialTests.csproj -c Release 2>&1 | tail -1
```
Expected: both `Failed: 0`. Then:
```bash
cd /home/uggeli/slopfall
git commit --allow-empty -m "$(cat <<'EOF'
test(affect): soak gate — perceived threat behaves in-regime (B-L1)

Gothway/Gallotale soaks: civilians fear and flee monsters via perception
(not the oracle), population stable, guards attack, social behaviour
plausible. Behaviour-sense gate (the parallel soak is nondeterministic;
not a byte-diff). Phase B/L1 complete.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review

**1. Spec coverage (B1–B7):**
- B1 (catalog tone face) → Task 3. B2 (`Fanged`/`Fast`/`Size` + `CreatureForms` + spawn stamp) → Tasks 3,4. B3 (prey-veto over weapon×size cues, off the oracle; Attention boost) → Task 5. B4 (retire role-scalar) → Task 6. B5 (de-dup) → Task 7. B6 (somatic clear, indoor sensed) → Tasks 1,2. B7 (beggar-stigma kept) → preserved in Task 5's `Interpret` rewrite (the `Doing Beg` block stays). Soak (behaviour-sense) → Tasks 0,8.

**2. Placeholder scan:** No "TBD"/"handle edge cases". Every code step shows full before/after; every run step has a command + expected output. The one judgment step (Task 8 Step 2) is a behaviour-sense rubric, not a placeholder — it lists concrete pass/fail conditions.

**3. Type consistency:** `ThreatRead(AtomBag, out double)` (Task 5) is the same name/shape its tests call. `CreatureForms.Beast : (AtomTypeId Type, Fixed Value)[]` (Task 4) matches its test and the spawn loop. `AtomCategory.Form`, `AtomName.Fanged/Fast/Size`, `.ToId()`, `AtomCatalog.For(name).Tone` consistent across Tasks 3→4→5. The final `Interpret` signature (Task 6: `relations, affects, behavior, personality, perceivable, agentMem, self, other`) is the one Task 7's `OddSystem` reroute calls.

**Open flags for the implementer / reviewer:**
- Task 6 Step 3 notes C# does not warn on unused fields/params — remove only what `Interpret` no longer takes; let the compiler flag genuinely undefined references. If `_creatures`/`_residency` are used by *other* `SubjectiveSystem` methods, keep the fields (only `Interpret` dropped them).
- The `MemoryReinforceSystem` finding means B4's "neutral social valence" is likely milder (learned via the new path) — the Task 8 soak shows the truth; do not pre-emptively add Phase-C seeds.
- Tasks 1, 2, 3 are independent and should be dispatched **concurrently** (different files, no shared edits).
