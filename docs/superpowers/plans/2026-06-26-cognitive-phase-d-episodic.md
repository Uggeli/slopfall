# Cognitive Phase D / L1 — Episodic Reinforcement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the "this kind hurt me" loop — a monster attacking a civilian drifts the civilian's `{EnemyMonster}` category belief more aversive, by feeding `DamageEvent` into the already-wired category-reinforce path (plus the monster perceivable Kind atom + the C-deferred monster innate prior that make the category exist to drift).

**Architecture:** Three small wirings. **D1** — `CreatureSystem.TrySpawn` stamps a perceivable Kind atom so `Signature(monster)={EnemyMonster}`. **D2/D3** — `InnatePriors` gains the `PriorTarget.Kind` seam (C stubbed it) and a `(Kind=EnemyMonster, −0.6, 0.3)` row, so `SeedPriors` seeds every civilian a `{EnemyMonster}` category at spawn. **D4** — `MemoryReinforceSystem` adds one source: `DamageEvent → Emit(victim, attacker, −1.0)`, which `Recognize`s the attacker's signature and `Reinforce`s the victim's matching category. Reinforce drops on an unrecognized signature, so D3 is the prerequisite for D4.

**Tech Stack:** C# on **.NET 10** (`net10.0`, modern C#), xUnit, fixed-point `Fixed` (the Memory subsystem is float-free).

## Global Constraints

- **.NET 10 / modern C#**; `Nullable` disable; match surrounding style; build at **0 warnings**.
- **Memory subsystem is float-free** — valences/outcomes are `Fixed`; `.ToDouble()` only in Engine reads / test asserts.
- **No verdict atoms.** The kind-belief lives in the agent's `MeaningsStore` category node (innate-seeded, reinforced from episodes). No `Danger`/`Predator` atom.
- **Reuse the wired path; no parallel machinery.** D4 adds a `foreach` to `MemoryReinforceSystem`, nothing else; reinforcement is applied by the existing `AgentMemoryRegistry` `MemoryReinforceIntent` handler.
- **Reinforce has no create-on-miss** (`AgentMemoryRegistry.cs:145-146`: `if (!cat.IsNone) Reinforce(...)`) — so the monster category must be **seeded** (D3) for damage (D4) to land.
- **Don't touch the threat stack / the prey-veto / `NeedsSystem`.** D feeds only the existing social-valence path.
- **Soak validates behaviour-SENSE, not byte-determinism** (memory `validate-sim-by-behavior-sense`) — judge believable / in-regime; never byte-diff.
- **Build/test from `/home/uggeli/slopfall/Headless`.** Confirm the green baseline at execution start (post-Phase-C it is **249** in `Sim.MemoryTests`); each task reports the new count, `Failed: 0`.
- **Don't sweep WIP.** Two pre-existing WIP docs (`docs/what_is_an_atom.md`, the 2026-06-24 M1 spec) are uncommitted — **`git add` only your task's files**, never `git add -A`.
- **Commit style:** conventional commits, scope `episodic`. End every body with:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

## Dependency map (execute SEQUENTIALLY in the main tree)

```
DT1 (monster Kind atom)      DT2 (InnatePriors seam + monster prior)      DT3 (DamageEvent reinforce)
        │                              │                                          │
        └──────────────────────────────┴──────────────────────────────────────────┘
                                        ▼
                              DT4 (behaviour-sense soak — needs all three)
```

DT1, DT2, DT3 are independent files; per the worktree-base lesson from Phase B, run **sequentially in the main checkout** (no isolated worktrees). The end-to-end behaviour (the monster category exists *and* drifts in the soak) needs all three, so DT4 is last. Order: DT1 · DT2 · DT3 · DT4.

---

## File Structure

**Modified:**
- `Assets/Sim/Engine/Units/CreatureSystem.cs` — stamp the monster Kind atom (DT1).
- `Assets/Sim/Engine/Units/InnatePriors.cs` — `PriorTarget.Kind` + the `EntityKind` field + the monster row (DT2).
- `Assets/Sim/World/AgentMemorySeeding.cs` — `SeedPriors` handles `PriorTarget.Kind` (DT2).
- `Assets/Sim/Engine/Units/MemoryReinforceSystem.cs` — the `DamageEvent` source + `Hurt` const (DT3).
- `Headless/Sim.MemoryTests/InnatePriorsTests.cs` — the "one entry" test → two entries (DT2).

**New tests:**
- `Headless/Sim.MemoryTests/DamageReinforceTests.cs` — the DamageEvent reinforce (DT3).

---

## Task 1 (D1): Monster perceivable Kind atom

**Files:**
- Modify: `Assets/Sim/Engine/Units/CreatureSystem.cs` (`TrySpawn`, after the form-atom loop ~line 232)

**Interfaces:**
- Consumes: `PerceivableAtoms.Kind(EntityKind)` → `AtomTypeId`; `StampAtomIntent { EntityId Entity; AtomTypeId Type; Fixed Value; }`.
- Produces: a spawned monster carries a perceivable `{EnemyMonster}` Kind atom, so `Signature(monster) = {EnemyMonster}` (consumed behaviourally by DT3's reinforce + DT2's seeded prototype). No unit test — verified by the DT4 soak (the monster category cannot drift without it); this commit's gate is "build + existing suite stay green."

- [ ] **Step 1: Add the Kind-atom stamp** — in `CreatureSystem.cs` `TrySpawn`, the form-atom loop currently reads:

```csharp
            // Phase B/L1: stamp the creature's perceivable form atoms (weapons + size) so a perceiver
            // reads threat from them (tone × size) — off the _creatures oracle. Beast: one form row.
            foreach (var form in CreatureForms.Beast)
                Events.Publish(new StampAtomIntent { Entity = id, Type = form.Type, Value = form.Value });
            return true;
```
Insert the Kind-atom stamp before `return true;`:

```csharp
            // Phase B/L1: stamp the creature's perceivable form atoms (weapons + size) so a perceiver
            // reads threat from them (tone × size) — off the _creatures oracle. Beast: one form row.
            foreach (var form in CreatureForms.Beast)
                Events.Publish(new StampAtomIntent { Entity = id, Type = form.Type, Value = form.Value });
            // Phase D/L1: stamp the perceivable KIND atom (identity) so a perceiver can recognize "a
            // monster" — its signature. Lets the innate {EnemyMonster} prior match + damage reinforce it.
            Events.Publish(new StampAtomIntent { Entity = id, Type = PerceivableAtoms.Kind(EntityKind.EnemyMonster), Value = Fixed.One });
            return true;
```

> `CreatureSystem` already uses `PerceivableAtoms`/`Fixed`/`StampAtomIntent` (the form-atom loop above), so no new `using`.

- [ ] **Step 2: Build + run the suite — expect green, no regression**

Run: `cd /home/uggeli/slopfall/Headless && dotnet build --configuration Release 2>&1 | tail -3 && DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release 2>&1 | tail -1`
Expected: `0 Error(s)`; `Failed: 0` (249 — additive stamp, no test change). The monster-category behaviour is gated by the DT4 soak.

- [ ] **Step 3: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Engine/Units/CreatureSystem.cs
git commit -m "$(cat <<'EOF'
feat(episodic): monsters stamp a perceivable Kind atom at spawn (D1)

CreatureSystem.TrySpawn now stamps PerceivableAtoms.Kind(EnemyMonster)
alongside the form atoms, so Signature(monster) = {EnemyMonster}. Makes a
monster recognizable as its kind — the prerequisite for the innate monster
prior to match (D3) and for damage to reinforce that category (D4).
Soak-verified (the monster category can't drift without it).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2 (D2+D3): `InnatePriors` Kind seam + the monster prior

**Files:**
- Modify: `Assets/Sim/Engine/Units/InnatePriors.cs` (the enum, the struct, the Civilian row)
- Modify: `Assets/Sim/World/AgentMemorySeeding.cs` (`SeedPriors`, the `PriorTarget.Self`-only switch ~lines 43-45)
- Test: `Headless/Sim.MemoryTests/InnatePriorsTests.cs` (rewrite the one-entry test)

**Interfaces:**
- Consumes: `PerceivableAtoms.Kind(EntityKind)`, `new Atom(AtomTypeId, Fixed)`, `AtomBag.Create(IEnumerable<Atom>)`, `EntityKind`.
- Produces: `enum PriorTarget { Self, Kind }`; `InnatePrior` gains `EntityKind Kind` (4-arg ctor); `InnatePriors.For(CivilianNPC)` returns `[InGroup(Self,+0.2), (Kind=EnemyMonster,−0.6,0.3)]`. `SeedPriors` seeds a `{EnemyMonster}` prototype for a `Kind` target. Consumed by DT3 behaviourally (the seeded category is what damage reinforces).

- [ ] **Step 1: Rewrite the failing test** — replace `InnatePriorsTests.cs`'s `Civilian_HasOneInGroupPrior_OthersEmpty` with the two-entry version (keep the `using`s + the `SeedInnatePrior_WritesARecognizableNode` test):

```csharp
        [Fact]
        public void Civilian_HasInGroupAndMonsterPriors()
        {
            var civ = InnatePriors.For(EntityKind.CivilianNPC);
            Assert.Equal(2, civ.Length);
            Assert.Contains(civ, p => p.Target == PriorTarget.Self && p.Valence.ToDouble() > 0.0);  // in-group warmth
            var monster = civ.Single(p => p.Target == PriorTarget.Kind);
            Assert.Equal(EntityKind.EnemyMonster, monster.Kind);
            Assert.True(monster.Valence.ToDouble() < 0.0);            // innate wariness of monster-kind
            Assert.True(monster.Confidence.ToDouble() > 0.0);
            Assert.Empty(InnatePriors.For(EntityKind.EnemyMonster));  // monsters don't socially interpret
        }
```

- [ ] **Step 2: Run it — expect FAIL** (compile: `PriorTarget.Kind`/`.Kind` field don't exist)

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~InnatePriorsTests"`
Expected: FAIL (does not compile).

- [ ] **Step 3a: Extend `InnatePriors.cs`** — the enum, struct, and Civilian row become:

```csharp
    /// <summary>Which signature an innate prior is ABOUT. Self = in-group ("those who look like me");
    /// Kind(X) = an out-group kind, keyed by that kind's perceivable Kind atom (Phase D).</summary>
    public enum PriorTarget { Self, Kind }

    /// <summary>One innate category belief: a starting valence + confidence about a PriorTarget.
    /// Kind is the believed-about EntityKind, used only when Target == Kind.</summary>
    public readonly struct InnatePrior
    {
        public readonly PriorTarget Target;
        public readonly EntityKind Kind;
        public readonly Fixed Valence;
        public readonly Fixed Confidence;
        public InnatePrior(PriorTarget target, EntityKind kind, Fixed valence, Fixed confidence)
        { Target = target; Kind = kind; Valence = valence; Confidence = confidence; }
    }
```
and the Civilian array (note the now-4-arg ctor; `EntityKind.Unknown` is the unused-Kind sentinel for the Self row):

```csharp
        // CivilianNPC: innate warmth toward its own kind (Self) + innate wariness of monster-kind
        // (the prey-of-predator prior). conf 0.3 = a lean (Reinforce drifts it; D feeds "this kind hurt me").
        static readonly InnatePrior[] Civilian =
        {
            new InnatePrior(PriorTarget.Self, EntityKind.Unknown,      Fixed.FromDouble(0.2),  Fixed.FromDouble(0.3)),
            new InnatePrior(PriorTarget.Kind, EntityKind.EnemyMonster, Fixed.FromDouble(-0.6), Fixed.FromDouble(0.3)),
        };
```

> `InnatePriors.cs` is in `DaggerfallWorkshop.Sim.Engine`; `EntityKind` resolves there (the existing `For(EntityKind)` uses it). No new `using`.

- [ ] **Step 3b: Handle `PriorTarget.Kind` in `SeedPriors`** — in `AgentMemorySeeding.cs`, the prototype build currently reads:

```csharp
                AtomBag proto = prior.Target == PriorTarget.Self
                    ? world.Perceivable.Signature(agent)
                    : AtomBag.Empty;
```
becomes:

```csharp
                AtomBag proto = prior.Target switch
                {
                    PriorTarget.Self => world.Perceivable.Signature(agent),
                    PriorTarget.Kind => AtomBag.Create(new[] { new Atom(PerceivableAtoms.Kind(prior.Kind), Fixed.One) }),
                    _ => AtomBag.Empty,
                };
```

> `AgentMemorySeeding` already `using`s `DaggerfallWorkshop.Sim.Memory` (`PerceivableAtoms`/`Atom`/`AtomBag`/`Fixed`) and `.Engine` (`PriorTarget`). No new `using`.

- [ ] **Step 4: Run the suite — expect PASS**

Run: `cd /home/uggeli/slopfall/Headless && DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0` (the rewritten `InnatePriorsTests` pass; the `SeedInnatePrior` test unaffected). The `SeedPriors`-stamps-a-real-`{EnemyMonster}`-category-at-spawn glue is soak-verified (DT4).

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Engine/Units/InnatePriors.cs Assets/Sim/World/AgentMemorySeeding.cs Headless/Sim.MemoryTests/InnatePriorsTests.cs
git commit -m "$(cat <<'EOF'
feat(episodic): innate monster prior via the PriorTarget.Kind seam (D2/D3)

InnatePriors gains PriorTarget.Kind + an EntityKind field (the seam C
stubbed); CivilianNPC adds (Kind=EnemyMonster, -0.6, 0.3). SeedPriors seeds
a {EnemyMonster}-keyed category at spawn. So civilians carry innate
prey-of-predator wariness AND the category that damage (D4) reinforces —
reinforce drops on miss, so this seed is D4's prerequisite.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3 (D4): Damage reinforces the kind

**Files:**
- Modify: `Assets/Sim/Engine/Units/MemoryReinforceSystem.cs` (the `Hurt` const + the `DamageEvent` loop in `Update`)
- Test: Create `Headless/Sim.MemoryTests/DamageReinforceTests.cs`

**Interfaces:**
- Consumes: `DamageEvent { EntityId Target, Source; int Amount; DamageType Type; }`; the existing `Emit(EntityId self, EntityId other, double outcome)`; `MemoryReinforceIntent { EntityId Perceiver; AtomBag Signature; Fixed Outcome; }`; `AgentMemoryRegistry.SeedInnatePrior` + its `MemoryReinforceIntent` handler.
- Produces: a `DamageEvent` makes `MemoryReinforceSystem` emit a negative `MemoryReinforceIntent` for the victim over the attacker's signature, drifting the victim's matching category aversive.

- [ ] **Step 1: Write the failing tests** — `DamageReinforceTests.cs` (both the emit and the end-to-end drift, mirroring `MemoryReinforceSystemTests`' tick pattern):

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class DamageReinforceTests
    {
        static AtomBag MonsterSig()
            => AtomBag.Create(new[] { new Atom(PerceivableAtoms.Kind(EntityKind.EnemyMonster), Fixed.One) });

        [Fact]
        public void Damage_EmitsNegativeReinforce_ForAttackersKind()
        {
            var e = new EventBus();
            var perceivable = new PerceivableRegistry(e);
            var sys = new MemoryReinforceSystem(e, perceivable);
            var victim = new EntityId(1); var monster = new EntityId(2);
            perceivable.Seed(monster, PerceivableAtoms.Kind(EntityKind.EnemyMonster), Fixed.One);

            e.Publish(new DamageEvent { Target = victim, Source = monster, Amount = 5, Type = DamageType.Physical });
            e.Tick(); perceivable.Update(0); sys.Update(0);
            e.Tick();

            var emitted = e.GetEvents<MemoryReinforceIntent>().ToArray();
            Assert.Single(emitted);
            Assert.Equal(victim, emitted[0].Perceiver);                        // the VICTIM learns
            Assert.True(emitted[0].Outcome.ToDouble() < 0.0);                  // hurt = aversive
            Assert.Contains(emitted[0].Signature.Atoms,
                a => a.Type.Value == PerceivableAtoms.Kind(EntityKind.EnemyMonster).Value);  // about the monster KIND
        }

        [Fact]
        public void Damage_DriftsTheVictimsSeededMonsterCategory_MoreNegative()
        {
            var e = new EventBus();
            var perceivable = new PerceivableRegistry(e);
            var agentMem = new AgentMemoryRegistry(e, AgentMemoryConfig.Default);
            var sys = new MemoryReinforceSystem(e, perceivable);
            var victim = new EntityId(1); var monster = new EntityId(2);

            // the monster is perceivable as its kind; the victim is born with the innate monster prior
            perceivable.Seed(monster, PerceivableAtoms.Kind(EntityKind.EnemyMonster), Fixed.One);
            agentMem.Seed(victim);
            agentMem.SeedInnatePrior(victim, MonsterSig(), Fixed.FromDouble(-0.6), Fixed.FromDouble(0.3));
            agentMem.TryGet(victim, out var mem);
            Assert.True(mem.Meanings.RecognizedValence(MonsterSig(), out var before, out _));

            // a monster attacks → the reinforce intent flows and is applied
            e.Publish(new DamageEvent { Target = victim, Source = monster, Amount = 5, Type = DamageType.Physical });
            e.Tick(); perceivable.Update(0); sys.Update(0);     // system emits MemoryReinforceIntent
            e.Tick(); agentMem.Update(1);                       // registry applies it → Reinforce

            mem.Meanings.RecognizedValence(MonsterSig(), out var after, out _);
            Assert.True(after.ToDouble() < before.ToDouble());  // "this kind hurt me" — drifted toward -1.0
        }
    }
}
```

> If `DamageType`'s member is named other than `Physical`, use the actual member (confirm at execution start: `grep -n "enum DamageType" -r Assets`).

- [ ] **Step 2: Run them — expect FAIL** (compile: `MemoryReinforceSystem` doesn't read `DamageEvent` yet — the first test's `Assert.Single(emitted)` fails; or it's the missing source so 0 emitted)

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~DamageReinforceTests"`
Expected: FAIL (no `DamageEvent` source yet → 0 intents emitted → `Assert.Single` fails).

- [ ] **Step 3: Add the `DamageEvent` source** — in `MemoryReinforceSystem.cs`, add the `Hurt` const and the loop:

```csharp
        const double Grant = 1.0, Refuse = -1.0, Greet = 0.5, Dislike = -0.5, Hurt = -1.0;
```
and in `Update`, after the four social loops:

```csharp
            foreach (ref readonly var d in Events.GetEvents<DislikeNearbyEvent>()) Emit(d.Who, d.Whom, Dislike);
            // Phase D/L1: "this kind hurt me" — being attacked reinforces the victim's category for the
            // ATTACKER's kind, aversive. (Reinforce no-ops if the victim has no such category seeded.)
            foreach (ref readonly var dm in Events.GetEvents<DamageEvent>()) Emit(dm.Target, dm.Source, Hurt);
```

- [ ] **Step 4: Run the suite — expect PASS**

Run: `cd /home/uggeli/slopfall/Headless && DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0` (the 2 new DamageReinforceTests pass).

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Engine/Units/MemoryReinforceSystem.cs Headless/Sim.MemoryTests/DamageReinforceTests.cs
git commit -m "$(cat <<'EOF'
feat(episodic): damage reinforces the attacker's kind — "this kind hurt me" (D4)

MemoryReinforceSystem adds DamageEvent as a reinforce source: Emit(victim,
attacker, Hurt=-1.0). The victim recognizes the attacker's signature
({EnemyMonster}, from D1) and reinforces its seeded category (D3) toward -1.0
— the belief about the kind deepens from real experience. Reuses the wired
reinforce path; no-ops if no category is seeded.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4 (DT4): Soak gate — behaviour-sense

**Files:** scratchpad captures only.

> NOT a determinism gate. Judge sensibility/regime against the Phase C reference (Gothway 337→340; civilians spawned with one in-group category, `cat/agent≈1`), not bytes.

- [ ] **Step 1: Run the soak**

```bash
cd /home/uggeli/slopfall/Headless
export DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2
SP=/tmp/claude-1000/-home-uggeli-slopfall/<session>/scratchpad   # use this session's scratchpad
dotnet run --project Sim.Host/Sim.Host.csproj -c Release -- --soak Daggerfall "Gothway Garden" 1 > "$SP/phased-gothway.txt" 2>&1
dotnet run --project Sim.Host/Sim.Host.csproj -c Release -- --soak Daggerfall Gallotale 1 > "$SP/phased-gallotale.txt" 2>&1
tail -8 "$SP/phased-gothway.txt"
```

- [ ] **Step 2: Judge the regime (behaviour-sense)** — confirm:
  - **The monster category now exists at spawn:** every civilian seeds **two** innate categories (in-group +0.2 AND monster −0.6) — `cat/agent ≈ 2` at t=0 (vs Phase C's ≈1), `maxAbs ≈ 0.6` (the monster prior, vs C's 0.20 in-group).
  - **It drifts from experience:** over the day, as monsters attack, the `{EnemyMonster}` nodes appear among `reinforcedNodes` and `maxAbs` deepens toward 1.0 (the belief learns — "this kind hurt me").
  - **No regression / no new artifact:** population stable (≈ Phase C trajectory); `Flee`/`kills` in-character; NO spurious social events toward monsters (the threat-gate holds); day/night rhythm + guards intact.

  A different-but-believable regime is success. An implausible one (pop collapse, townsfolk fleeing each other, the monster category NOT appearing/drifting) is a bug — investigate with superpowers:systematic-debugging; do NOT tune.

- [ ] **Step 3: Full green sweep + record the gate**

```bash
cd /home/uggeli/slopfall/Headless
DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release 2>&1 | tail -1
dotnet test Sim.SpatialTests/Sim.SpatialTests.csproj -c Release 2>&1 | tail -1
cd /home/uggeli/slopfall
git commit --allow-empty -m "$(cat <<'EOF'
test(episodic): soak gate — monster category seeded + drifts from attacks (D-L1)

Gothway/Gallotale soaks: civilians now spawn with the {EnemyMonster}
category (cat/agent ≈ 2, maxAbs ≈ 0.6) AND it deepens toward -1.0 as monsters
attack over the day — "this kind hurt me" learns from experience. No
civilian-fears-civilian, no spurious social events toward monsters,
population/rhythm in-regime. Behaviour-sense gate (nondeterministic soak; not
a byte-diff). Phase D/L1 complete.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review

**1. Spec coverage:** D1 (monster Kind atom) → DT1. D2 (`PriorTarget.Kind` seam) → DT2. D3 (monster innate prior + the no-create-on-miss prerequisite) → DT2. D4 (`DamageEvent → Emit(victim, attacker, Hurt=−1.0)`) → DT3. D5 (reputation cue / EVENTS store / witnessing deferred) → not built, by design. Honest-payoff + threat-gate notes → reflected in DT3's comment + DT4's "no spurious social events" check. Validation (unit + soak drift) → DT2/DT3 tests + DT4.

**2. Placeholder scan:** No "TBD"/"handle edge cases". Every code step has full before/after; every run step a command + expected output. Two explicit confirm-at-execution notes (the `<session>` scratchpad path in DT4; the `DamageType.Physical` member name in DT3) are instructions to the implementer, not placeholders.

**3. Type consistency:** `InnatePrior(PriorTarget, EntityKind, Fixed, Fixed)` (DT2) is the 4-arg shape the Civilian rows and the test use. `PriorTarget { Self, Kind }` consistent across `InnatePriors` + `SeedPriors`. `DamageEvent { Target, Source, Amount, Type }` (DT3) matches `SimEvents.cs`. `Emit(self, other, outcome)` called as `Emit(dm.Target, dm.Source, Hurt)` matches the existing signature. `MemoryReinforceIntent { Perceiver, Signature, Outcome }` consistent in DT3's assertions.

**Open flags for the implementer:**
- DT1 has no unit test (a 1-line additive identity-atom stamp on a private internally-triggered spawn) — its behaviour is gated by the DT4 soak (the monster category cannot exist/drift without it). Confirm the build + existing suite stay green.
- DT3: confirm `DamageType`'s member name (`grep -n "enum DamageType" -r Assets`) before writing the test; use the real member.
- DT3 drift test ticks the registry (`agentMem.Update(1)`) to apply the emitted intent — confirm `AgentMemoryRegistry.Update` is the applier (it is: `MemoryReinforceIntent` handler at `AgentMemoryRegistry.cs:141-147`).
- Adding the monster Kind atom (DT1) makes monsters socially recognizable — the prey-veto still masks the social valence (threat-gate), so no behavioural change beyond the category bookkeeping; DT4 confirms no spurious social events fire toward monsters.
