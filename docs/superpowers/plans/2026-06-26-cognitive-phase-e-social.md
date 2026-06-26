# Cognitive Phase E / L1 — Social / Vicarious Learning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A monster-fear belief spreads by word of mouth — when a monster attacks, the witnesses' danger-shout also carries the attacker's kind, and every hearer in earshot reinforces their `{EnemyMonster}` category **second-hand, trust-scaled** by how much they trust the shouter.

**Architecture:** Three wirings on the existing shout→hear→communicate pipeline. **E1** adds a `Scale` to `MemoryReinforceIntent` and a scaled-step `Reinforce` overload (trust scales how far the belief moves, never pulls it back). **E2** adds `Utterance.SubjectKind` and has the danger-alarm set it to `{EnemyMonster}`. **E3** has `CommunicationSystem` relay a heard `SubjectKind` as a second-hand `MemoryReinforceIntent` (aversive, `Scale = BeliefScale`). D's innate monster prior is the prerequisite (no create-on-miss).

**Tech Stack:** C# on **.NET 10** (`net10.0`, modern C#), xUnit, fixed-point `Fixed` (the Memory subsystem is float-free).

## Global Constraints

- **.NET 10 / modern C#**; `Nullable` disable; match surrounding style; build at **0 warnings**.
- **Memory subsystem is float-free** — `Fixed` (Raw int, Scale=256). `Fixed` has **no `operator*`**: scale via int math `(int)((long)x * scale.Raw / Fixed.Scale)` (mirrors `AgentMemoryRegistry`'s `strength * ts / Fixed.Scale`). `.ToDouble()`/`FromDouble` only in Engine reads / test asserts.
- **No verdict atoms.** The kind-belief lives in `MeaningsStore` category nodes (innate-seeded, first-hand reinforced in D, second-hand here). No `Danger`/`Predator` atom.
- **Trust scales the MOVE, not the target.** The reinforce step (toward the aversive outcome) is scaled; a low-trust report moves the belief less and never pulls a deeper belief back. The min ±1-raw guard stays (even a low-trust report nudges).
- **`Fixed` structs zero-init → set `Scale = Fixed.One` explicitly** in the first-hand `MemoryReinforceSystem.Emit` (else `Scale` defaults to 0 = no reinforce — a silent regression). The existing `DamageReinforceTests`/social loops are the regression guard.
- **Reuse the pipeline; don't add transport.** E rides the existing shout/hear/`CommunicationSystem`; it adds a relay branch + a struct field, nothing more.
- **Don't touch the threat stack / prey-veto / `NeedsSystem`.** The spread belief feeds only the existing social-valence path.
- **Soak validates behaviour-SENSE, not byte-determinism** (memory `validate-sim-by-behavior-sense`) — judge believable / in-regime; never byte-diff.
- **Build/test from `/home/uggeli/slopfall/Headless`.** Confirm the green baseline at execution start (post-Phase-D it is **251** in `Sim.MemoryTests`); each task reports the new count, `Failed: 0`.
- **Don't sweep WIP.** Two pre-existing WIP docs (`docs/what_is_an_atom.md`, the 2026-06-24 M1 spec) are uncommitted — **`git add` only your task's files**, never `git add -A`.
- **Commit style:** conventional commits, scope `social`. End every body with:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`

## Dependency map (execute SEQUENTIALLY in the main tree)

```
ET1 (Scale primitive)  →  ET2 (Utterance.SubjectKind + alarm)  →  ET3 (CommunicationSystem kind-relay)  →  ET4 (soak)
```

ET3 needs ET1's `Scale` field + ET2's `SubjectKind`. Per the worktree-base lesson, run **sequentially in the main checkout**. Order: ET1 · ET2 · ET3 · ET4.

---

## File Structure

**Modified:**
- `Assets/Sim/Engine/Units/AgentMemoryRegistry.cs` — `MemoryReinforceIntent` + `Scale`; the apply loop passes it (ET1).
- `Assets/Sim/Memory/MeaningsStore.cs` — the scaled-step `Reinforce` overload (ET1).
- `Assets/Sim/Engine/Units/MemoryReinforceSystem.cs` — `Emit` sets `Scale = Fixed.One` (ET1).
- `Assets/Sim/Engine/Units/Communication.cs` — `Utterance.SubjectKind` (ET2) + the `CommunicationSystem` kind-relay (ET3).
- `Assets/Sim/Engine/Units/PlaceDangerSystem.cs` — the shout sets `SubjectKind` (ET2).

**New tests:**
- `Headless/Sim.MemoryTests/ReinforceScaleTests.cs` (ET1)
- `Headless/Sim.MemoryTests/GossipReinforceTests.cs` (ET3)
- (ET2 extends `PlaceDangerTests.cs`)

---

## Task 1 (E1): The trust-scaled reinforce primitive

**Files:**
- Modify: `Assets/Sim/Engine/Units/AgentMemoryRegistry.cs` (`MemoryReinforceIntent` struct ~line 49; the apply loop ~141-147)
- Modify: `Assets/Sim/Memory/MeaningsStore.cs` (`Reinforce` ~118-143)
- Modify: `Assets/Sim/Engine/Units/MemoryReinforceSystem.cs` (`Emit` ~30-34)
- Test: Create `Headless/Sim.MemoryTests/ReinforceScaleTests.cs`

**Interfaces:**
- Produces: `MemoryReinforceIntent { Perceiver, Signature, Outcome, Fixed Scale }`; `MeaningsStore.Reinforce(CategoryId, AtomBag, Fixed outcome, Fixed scale)` (scaled step) + the 3-arg overload (`scale = Fixed.One`). The first-hand `Emit` sets `Scale = Fixed.One`. Consumed by ET3 (the gossip relay sets `Scale = BeliefScale`).

- [ ] **Step 1: Write the failing tests** — `ReinforceScaleTests.cs`:

```csharp
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class ReinforceScaleTests
    {
        static AtomBag Sig() => AtomBag.Create(new[] { new Atom(AtomName.EnemyMonster.ToId(), Fixed.One) });

        [Fact]
        public void Reinforce_Scaled_MovesLessThanFullTrust()
        {
            var a = new MeaningsStore(8, MeaningsConfig.Default);
            var b = new MeaningsStore(8, MeaningsConfig.Default);
            var ida = a.SeedInnate(Sig(), Fixed.FromDouble(-0.6), Fixed.FromDouble(0.3));
            var idb = b.SeedInnate(Sig(), Fixed.FromDouble(-0.6), Fixed.FromDouble(0.3));

            a.Reinforce(ida, Sig(), Fixed.FromDouble(-1.0), Fixed.One);             // full trust
            b.Reinforce(idb, Sig(), Fixed.FromDouble(-1.0), Fixed.FromDouble(0.5)); // half trust

            a.RecognizedValence(Sig(), out var va, out _);
            b.RecognizedValence(Sig(), out var vb, out _);
            Assert.True(va.ToDouble() < vb.ToDouble());   // full-trust moved further toward -1.0
            Assert.True(vb.ToDouble() < -0.6);            // half-trust still moved (min-step keeps it un-stalled)
        }

        [Fact]
        public void ReinforceIntent_Scale_IsAppliedByTheRegistry()
        {
            var e = new EventBus();
            var mem = new AgentMemoryRegistry(e, AgentMemoryConfig.Default);
            var agent = new EntityId(1);
            mem.Seed(agent);
            mem.SeedInnatePrior(agent, Sig(), Fixed.FromDouble(-0.6), Fixed.FromDouble(0.3));

            e.Publish(new MemoryReinforceIntent
            { Perceiver = agent, Signature = Sig(), Outcome = Fixed.FromDouble(-1.0), Scale = Fixed.FromDouble(0.5) });
            e.Tick(); mem.Update(0);

            mem.TryGet(agent, out var m);
            m.Meanings.RecognizedValence(Sig(), out var v, out _);
            Assert.True(v.ToDouble() < -0.6);   // the scaled second-hand intent reinforced (deepened)
        }
    }
}
```

- [ ] **Step 2: Run them — expect FAIL** (compile: `MemoryReinforceIntent.Scale` + the 4-arg `Reinforce` don't exist)

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~ReinforceScaleTests"`
Expected: FAIL (does not compile).

- [ ] **Step 3a: Add `Scale` to the intent** — in `AgentMemoryRegistry.cs`, the struct becomes:

```csharp
    public struct MemoryReinforceIntent : IEvent { public EntityId Perceiver; public AtomBag Signature; public Fixed Outcome; public Fixed Scale; }
```

- [ ] **Step 3b: Pass `Scale` in the apply loop** — the reinforce loop's `Reinforce` call becomes:

```csharp
                if (!cat.IsNone) rm.Meanings.Reinforce(cat, reinforce[i].Signature, reinforce[i].Outcome, reinforce[i].Scale);
```

- [ ] **Step 3c: The scaled-step `Reinforce` overload** — in `MeaningsStore.cs`, keep the existing 3-arg method as a wrapper and add the 4-arg with the scaled step. Replace the 3-arg `Reinforce` with:

```csharp
        /// <summary>First-hand reinforce — full trust (scale = 1).</summary>
        public bool Reinforce(CategoryId id, AtomBag percept, Fixed outcome) => Reinforce(id, percept, outcome, Fixed.One);

        /// <summary>
        /// StatFold + valence/confidence update for one category, the valence MOVE scaled by trust
        /// (scale ∈ [0,1]; 1 = first-hand). A low-trust (second-hand) report moves the belief LESS toward
        /// the outcome and never past it; the ±1-raw floor keeps even a low-trust report from stalling.
        /// Confidence is not scaled (L1). INNATE nodes learn too. Returns false if absent.
        /// </summary>
        public bool Reinforce(CategoryId id, AtomBag percept, Fixed outcome, Fixed scale)
        {
            CategoryNode node;
            if (!TryGetNode(id, out node)) return false;

            node.Predicted.Fold(percept);

            int gap = outcome.Raw - node.Valence.Raw;
            int step = gap >> Config.LearnShift;
            step = (int)((long)step * scale.Raw / Fixed.Scale);   // trust-scale the move (Fixed has no operator*)
            if (step == 0 && gap != 0) step = gap > 0 ? 1 : -1;   // floor so a low-trust report still nudges
            int valenceBefore = node.Valence.Raw;
            node.Valence = new Fixed(valenceBefore + step);

            bool neutral = valenceBefore <= Config.NeutralBandRaw && valenceBefore >= -Config.NeutralBandRaw;
            bool confirming = neutral || ((outcome.Raw >= 0) == (valenceBefore >= 0));
            int conf = node.Confidence.Raw + (confirming ? Config.ConfidenceGainRaw : -Config.ConfidenceGainRaw);
            if (conf < 0) conf = 0;
            else if (conf > Fixed.Scale) conf = Fixed.Scale;
            node.Confidence = new Fixed(conf);

            return true;
        }
```

- [ ] **Step 3d: First-hand `Emit` sets `Scale = Fixed.One`** — in `MemoryReinforceSystem.cs`, the `Emit` publish becomes:

```csharp
            Events.Publish(new MemoryReinforceIntent { Perceiver = self, Signature = sig, Outcome = Fixed.FromDouble(outcome), Scale = Fixed.One });
```

- [ ] **Step 4: Run the suite — expect PASS**

Run: `cd /home/uggeli/slopfall/Headless && DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0` (251 + 2). The existing `DamageReinforceTests` MUST still pass — they regression-guard that `Emit` sets `Scale = Fixed.One` (first-hand drift unchanged).

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Engine/Units/AgentMemoryRegistry.cs Assets/Sim/Memory/MeaningsStore.cs Assets/Sim/Engine/Units/MemoryReinforceSystem.cs Headless/Sim.MemoryTests/ReinforceScaleTests.cs
git commit -m "$(cat <<'EOF'
feat(social): trust-scaled (second-hand) reinforce primitive (E1)

MemoryReinforceIntent gains a Scale; MeaningsStore.Reinforce gains a 4-arg
overload that scales the valence STEP by trust (a low-trust report moves the
belief less toward the outcome, never past it; the min ±1 floor keeps it
un-stalled). AgentMemoryRegistry passes intent.Scale. The first-hand Emit
sets Scale=Fixed.One (Fixed structs zero-init, so this is required to keep D's
drift). Confidence unscaled in L1.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2 (E2): `Utterance.SubjectKind` + the danger-alarm sets it

**Files:**
- Modify: `Assets/Sim/Engine/Units/Communication.cs` (`Utterance` struct ~35-43)
- Modify: `Assets/Sim/Engine/Units/PlaceDangerSystem.cs` (the shout ~53-58)
- Test: extend `Headless/Sim.MemoryTests/PlaceDangerTests.cs`

**Interfaces:**
- Produces: `Utterance.SubjectKind` (`AtomBag`, default `null` = none). The danger-shout sets it to the `{EnemyMonster}` kind signature. Consumed by ET3.

- [ ] **Step 1: Write the failing test** — append to `PlaceDangerTests.cs` (the `Rig` exists; it can read emitted `Utterance`s via `r.E.GetEvents<Utterance>()` after `r.Run()` — confirm the rig's tick exposes them; if not, capture them as the existing test captures `PlaceObserveIntent`):

```csharp
        [Fact]
        public void WitnessShout_CarriesTheMonsterKind()
        {
            var r = new Rig();
            var witness = new EntityId(1);
            var victim = new EntityId(2);
            var killer = new EntityId(99);   // the creature

            r.Buildings.Add(new BuildingRow { Kind = BuildingKind.Tavern, X = 0f, Z = 0f });
            r.Position.Seed(victim, 1f, 0f, 1f, 0f);
            r.E.Publish(new CreatureSetIntent { Id = killer, Data = new CreatureData() });
            r.E.Publish(new SensedSetIntent { Id = witness, Sensed = new System.Collections.Generic.List<EntityId> { victim } });
            r.Apply();

            r.E.Publish(new DeathEvent { Entity = victim, Killer = killer });
            var shouts = r.RunCapturingUtterances();   // ticks PlaceDangerSystem; returns this tick's Utterances

            var shout = System.Linq.Enumerable.Single(shouts, u => u.Speaker == witness);
            Assert.NotNull(shout.SubjectKind);
            Assert.Contains(shout.SubjectKind.Atoms,
                a => a.Type.Value == PerceivableAtoms.Kind(EntityKind.EnemyMonster).Value);   // "a monster attacked"
        }
```

> Implementer note: if the `Rig` has no utterance-capturing helper, add a tiny `RunCapturingUtterances()` (tick `Danger.Update`, then `r.E.GetEvents<Utterance>().ToArray()` before the next `Tick`) — the existing `Emitted()` captures `PlaceObserveIntent` the same way. Keep it in the test rig.

- [ ] **Step 2: Run it — expect FAIL** (compile: `Utterance.SubjectKind` doesn't exist)

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~PlaceDangerTests.WitnessShout_CarriesTheMonsterKind"`
Expected: FAIL (does not compile).

- [ ] **Step 3a: Add `SubjectKind` to `Utterance`** — in `Communication.cs`, the struct becomes:

```csharp
    public struct Utterance : IEvent
    {
        public EntityId Speaker, Audience;
        public CommChannel Channel;
        public SpeechAct Act;
        public int SubjectBuilding;   // the place the content is ABOUT (for place facts); <0 = none
        public AtomBag Content;
        public AtomBag SubjectKind;   // the KIND the utterance is about (a reputation belief); null = none
        public Fixed Confidence;
    }
```

> `AtomBag` is a reference type (the existing `u.Content == null` check), so `SubjectKind` defaults to `null` on every existing `Utterance` construction — none need changing.

- [ ] **Step 3b: The danger-shout sets `SubjectKind`** — in `PlaceDangerSystem.cs`, the shout publish (inside the witness loop) becomes:

```csharp
                    // A witness also SHOUTS the danger — bystanders in earshot who didn't see it learn
                    // it second-hand (word of mouth). Phase E: the shout also names the KIND that attacked
                    // (the killer is a creature — _creatures.Contains(d.Killer) above — so {EnemyMonster}),
                    // so hearers deepen their monster-belief, not just the place-danger.
                    Events.Publish(new Utterance
                    {
                        Speaker = kv.Key, Audience = EntityId.None, Channel = CommChannel.Shout,
                        Act = SpeechAct.Inform, SubjectBuilding = building, Confidence = Severity,
                        Content = AtomBag.Create(new[] { new Atom(PlaceAtoms.Danger, Severity) }),
                        SubjectKind = AtomBag.Create(new[] { new Atom(PerceivableAtoms.Kind(EntityKind.EnemyMonster), Fixed.One) })
                    });
```

> `PlaceDangerSystem` already `using`s `DaggerfallWorkshop.Sim.Memory` (`PlaceAtoms`/`AtomBag`/`Atom`/`Fixed`); `PerceivableAtoms`/`EntityKind` resolve there too. The killer's kind is `EnemyMonster` because the loop is gated on `_creatures.Contains(d.Killer)` — no `PerceivableRegistry` needed. (When monster variety arrives, this becomes `Signature(killer)`.)

- [ ] **Step 4: Run the suite — expect PASS**

Run: `cd /home/uggeli/slopfall/Headless && DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0` (253 + 1; the existing place-danger tests unaffected — `SubjectKind` is additive).

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Engine/Units/Communication.cs Assets/Sim/Engine/Units/PlaceDangerSystem.cs Headless/Sim.MemoryTests/PlaceDangerTests.cs
git commit -m "$(cat <<'EOF'
feat(social): the danger-alarm names the attacker's kind (E2)

Utterance gains a SubjectKind (AtomBag, null = none — additive, every existing
utterance keeps null). A witness's danger-shout now also carries {EnemyMonster}
(the killer is a creature, per the _creatures gate), so the alarm says "a
MONSTER attacked here," not just "danger here." Hearers consume it in E3.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3 (E3): `CommunicationSystem` relays the kind-belief second-hand

**Files:**
- Modify: `Assets/Sim/Engine/Units/Communication.cs` (`CommunicationSystem.Update` ~114-133)
- Test: Create `Headless/Sim.MemoryTests/GossipReinforceTests.cs`

**Interfaces:**
- Consumes: `Utterance.SubjectKind` (ET2); `MemoryReinforceIntent { …, Scale }` (ET1); the existing `BeliefScale`.
- Produces: a heard `Inform` utterance with a non-empty `SubjectKind` emits a second-hand `MemoryReinforceIntent` (Perceiver = hearer, the kind signature, aversive outcome, `Scale = BeliefScale`).

- [ ] **Step 1: Write the failing tests** — `GossipReinforceTests.cs`:

```csharp
using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class GossipReinforceTests
    {
        static AtomBag MonsterKind()
            => AtomBag.Create(new[] { new Atom(PerceivableAtoms.Kind(EntityKind.EnemyMonster), Fixed.One) });

        static Utterance Alarm(EntityId speaker)
            => new Utterance
            {
                Speaker = speaker, Audience = EntityId.None, Channel = CommChannel.Shout, Act = SpeechAct.Inform,
                SubjectBuilding = 7, Content = AtomBag.Create(new[] { new Atom(PlaceAtoms.Danger, Fixed.One) }),
                SubjectKind = MonsterKind(), Confidence = Fixed.One
            };

        [Fact]
        public void HeardKindAlarm_EmitsSecondHandReinforce_ForTheKind()
        {
            var e = new EventBus();
            var rel = new RelationsRegistry(e);
            var comm = new CommunicationSystem(e, rel);
            var speaker = new EntityId(1); var hearer = new EntityId(2);

            e.Publish(new HeardUtterance { Hearer = hearer, Said = Alarm(speaker) });
            e.Tick(); comm.Update(0);
            e.Tick();

            var ri = e.GetEvents<MemoryReinforceIntent>().ToArray();
            Assert.Single(ri);
            Assert.Equal(hearer, ri[0].Perceiver);
            Assert.True(ri[0].Outcome.ToDouble() < 0.0);                                 // "this kind is bad"
            Assert.True(ri[0].Scale.ToDouble() > 0.0 && ri[0].Scale.ToDouble() <= 1.0);  // trust-scaled
            Assert.Contains(ri[0].Signature.Atoms,
                a => a.Type.Value == PerceivableAtoms.Kind(EntityKind.EnemyMonster).Value);
        }

        [Fact]
        public void PlaceOnlyUtterance_EmitsNoKindReinforce()
        {
            var e = new EventBus();
            var rel = new RelationsRegistry(e);
            var comm = new CommunicationSystem(e, rel);
            var u = Alarm(new EntityId(1)); u.SubjectKind = null;   // a plain place-danger shout

            e.Publish(new HeardUtterance { Hearer = new EntityId(2), Said = u });
            e.Tick(); comm.Update(0);
            e.Tick();

            Assert.Empty(e.GetEvents<MemoryReinforceIntent>().ToArray());   // no kind belief → no reinforce
            Assert.NotEmpty(e.GetEvents<PlaceObserveIntent>().ToArray());   // place relay still fires
        }
    }
}
```

- [ ] **Step 2: Run them — expect FAIL** (the kind-relay doesn't exist → 0 `MemoryReinforceIntent`s)

Run: `cd /home/uggeli/slopfall/Headless && dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release --filter "FullyQualifiedName~GossipReinforceTests"`
Expected: FAIL (`Assert.Single(ri)` fails — none emitted).

- [ ] **Step 3: Add the kind-relay** — in `Communication.cs`, restructure `CommunicationSystem.Update` so the place-fact relay and the kind-belief relay are independent branches of one `Inform`:

```csharp
        public override void Update(long tick)
        {
            var heard = Events.GetEvents<HeardUtterance>();
            for (int i = 0; i < heard.Length; i++)
            {
                var u = heard[i].Said;
                if (u.Act != SpeechAct.Inform) continue;
                Fixed belief = BeliefScale(heard[i].Hearer, u.Speaker, u.Confidence);

                // Place-fact relay (P2): a heard place atom → second-hand PlaceObserveIntent.
                if (u.SubjectBuilding >= 0 && u.Content != null)
                {
                    var atoms = u.Content.Atoms;
                    for (int k = 0; k < atoms.Count; k++)
                    {
                        if (!AtomCatalog.For(atoms[k].Type).Shareable) continue;   // only PLACE facts relay
                        Events.Publish(new PlaceObserveIntent
                        {
                            Agent = heard[i].Hearer, Building = u.SubjectBuilding, Atom = atoms[k].Type,
                            Value = atoms[k].Value, SecondHand = true, TrustScale = belief
                        });
                    }
                }

                // Kind-belief relay (Phase E): a heard reputation ("that kind is dangerous") →
                // second-hand, trust-scaled reinforce of the hearer's category for that kind.
                if (u.SubjectKind != null && u.SubjectKind.Count > 0)
                {
                    Events.Publish(new MemoryReinforceIntent
                    {
                        Perceiver = heard[i].Hearer, Signature = u.SubjectKind,
                        Outcome = -Fixed.One,   // a danger alarm conveys "this kind is bad" (−1.0)
                        Scale = belief
                    });
                }
            }
        }
```

> Net change vs current: the `continue` no longer also gates on `SubjectBuilding < 0 || Content == null` (those become the place-branch's own `if`), so a kind-only utterance still reaches the kind-branch. `BeliefScale` is reused unchanged.

- [ ] **Step 4: Run the suite — expect PASS**

Run: `cd /home/uggeli/slopfall/Headless && DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release`
Expected: `Failed: 0` (254 + 2). The existing `CommReceptionTests` (place relay) MUST still pass — the place branch is behaviour-preserved.

- [ ] **Step 5: Commit**

```bash
cd /home/uggeli/slopfall
git add Assets/Sim/Engine/Units/Communication.cs Headless/Sim.MemoryTests/GossipReinforceTests.cs
git commit -m "$(cat <<'EOF'
feat(social): hearers reinforce a kind-belief second-hand — gossip (E3)

CommunicationSystem relays a heard Utterance.SubjectKind as a second-hand
MemoryReinforceIntent (Perceiver=hearer, the kind signature, Outcome=-1.0,
Scale=BeliefScale) — so everyone in earshot of a monster-alarm deepens their
{EnemyMonster} belief, weighted by how much they trust the shouter. Place-fact
relay is split into its own branch (behaviour-preserved). Reuses BeliefScale.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4 (ET4): Soak gate — behaviour-sense

**Files:** scratchpad captures only.

> NOT a determinism gate. Judge against the Phase D reference (monster `maxAbs` 0.6→1.0 driven only by direct victims), not bytes.

- [ ] **Step 1: Run the soak**

```bash
cd /home/uggeli/slopfall/Headless
export DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2
SP=/tmp/claude-1000/-home-uggeli-slopfall/<session>/scratchpad   # use this session's scratchpad
dotnet run --project Sim.Host/Sim.Host.csproj -c Release -- --soak Daggerfall "Gothway Garden" 1 > "$SP/phasee-gothway.txt" 2>&1
dotnet run --project Sim.Host/Sim.Host.csproj -c Release -- --soak Daggerfall Gallotale 1 > "$SP/phasee-gallotale.txt" 2>&1
tail -8 "$SP/phasee-gothway.txt"
```

- [ ] **Step 2: Judge the regime (behaviour-sense)** — confirm:
  - **The monster belief spreads further than D:** more of the population reaches a deep `{EnemyMonster}` belief / high confidence — the alarm reaches hearers who were neither attacked nor saw it. Watch `val.meanAbs` and `conf` trending higher across the day than the D reference (the belief now reaches survivors via word of mouth, not just the dying victims).
  - **No runaway / no artifact:** population stable (≈ D trajectory); `maxAbs` stays clamped ≤ 1.0 (the move scales, never overshoots); `Flee`/kills and social mix in-character; no civilian-fears-civilian.
  - **Trust differentiation is plausible:** the spread is gradual (trust-scaled), not an instant town-wide jump.

  A different-but-believable regime is success. An implausible one (pop collapse, the belief NOT spreading beyond D, everyone instantly at −1.0, or social collapse) is a bug — investigate with superpowers:systematic-debugging; do NOT tune.

- [ ] **Step 3: Full green sweep + record the gate**

```bash
cd /home/uggeli/slopfall/Headless
DAGGERFALL_ARENA2=/home/uggeli/df-data/arena2 dotnet test Sim.MemoryTests/Sim.MemoryTests.csproj -c Release 2>&1 | tail -1
dotnet test Sim.SpatialTests/Sim.SpatialTests.csproj -c Release 2>&1 | tail -1
cd /home/uggeli/slopfall
git commit --allow-empty -m "$(cat <<'EOF'
test(social): soak gate — monster-fear spreads by word of mouth (E-L1)

Gothway/Gallotale soaks: the {EnemyMonster} belief now reaches MORE of the
population than D (the danger-alarm carries the kind, hearers reinforce it
second-hand, trust-scaled) — val.meanAbs/conf trend higher across the day,
sourced from others' experience not just direct victims. maxAbs stays ≤1.0
(the move scales, never overshoots); population/rhythm in-regime; no
civilian-fears-civilian, no runaway. Behaviour-sense gate (nondeterministic
soak; not a byte-diff). Phase E/L1 complete.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review

**1. Spec coverage:** E1 (trust-scaled reinforce: `Scale` on the intent + scaled-step `Reinforce` + apply + `Emit` sets One) → ET1. E2 (`Utterance.SubjectKind` + danger-alarm sets it) → ET2. E3 (`CommunicationSystem` kind-relay → second-hand reinforce, `Scale = BeliefScale`) → ET3. E4 (D's prior as the no-create-on-miss prerequisite) → relied on (the seeded `{EnemyMonster}` category; the reinforce no-ops without it, as in D). E5 (deferred) → not built, by design. The "trust scales the move not the target" principle → ET1's scaled step (never past the outcome). Honest-payoff (veto-masked; validated by spread) → ET4's "spreads further than D" check.

**2. Placeholder scan:** No "TBD"/"handle edge cases". Every code step shows full before/after; every run step a command + expected output. The `<session>` scratchpad token (ET4) + the `RunCapturingUtterances` rig-helper note (ET2) are explicit implementer instructions, not placeholders.

**3. Type consistency:** `MemoryReinforceIntent { Perceiver, Signature, Outcome, Scale }` (ET1) is what ET3 constructs and the apply loop reads. `Reinforce(CategoryId, AtomBag, Fixed, Fixed)` (ET1) is the 4-arg the apply loop calls. `Utterance.SubjectKind : AtomBag` (ET2) is what ET3's relay reads (`!= null && .Count > 0`). `BeliefScale` (existing) returns the `Fixed` used as both `TrustScale` (place) and `Scale` (kind). `-Fixed.One` is the aversive outcome (Fixed supports unary `-`).

**Open flags for the implementer:**
- ET1: the existing `DamageReinforceTests` are the regression guard for `Emit` setting `Scale = Fixed.One` — if they fail, `Scale` defaulted to 0 (no reinforce). Confirm they pass.
- ET1: confirm nothing else calls `MeaningsStore.Reinforce` with the old 3-arg expecting different behaviour — the 3-arg wrapper preserves it (scale = One).
- ET2: if the `PlaceDangerTests` `Rig` can't surface emitted `Utterance`s, add a small `RunCapturingUtterances()` helper (tick `Danger.Update`, snapshot `GetEvents<Utterance>()`), mirroring the existing `Emitted()` for `PlaceObserveIntent`.
- ET3: the `CommunicationSystem.Update` restructure must keep the place-relay behaviour identical (`CommReceptionTests` is the guard) — the only change is splitting the one gate into two independent `if`s so a kind-only utterance isn't dropped.
