# Atoms Refactor — `AtomName` + Catalog (Phase A, Design)

**Date:** 2026-06-24
**Status:** draft, ready for review.
**Depends on:** the atom canon (`what_is_an_atom.md`), the cognitive framework roadmap
(`docs/cognitive_framework_roadmap.md` — **this is its Phase A**), the 2026-06-23 audit. Blast-radius
mapped against current `Assets/Sim` + `Headless`; this spec cites file:line.
**Part of:** the cognitive framework. Phase A is the **honest substrate** — it lands **before** the
affect/cognition work (Phase B = the reframed M1), because every later phase names atoms and the
current id-band scheme is both collision-prone and the root cause of the recognition mismatch.

## The idea

Today an atom's identity is an `int` packed into hand-allocated **bands**: `KindBase=1000`,
`RoleBase=2000`, `RaceBase=3000`, `ActivityBase=4000` (`PerceivableAtoms.cs`), `PlaceAtoms.KindBase=5000`,
`Provisions=6000`, `Danger=6001` (`PlaceAtoms.cs`), `SomaticAtoms` `7000-7002`. The bands are magic
numbers; the only registry is a comment; and **band boundaries are re-used as program logic** in a
dozen places — `v >= ActivityBase` means "transient state, hide from recognition"
(`PerceivableRegistry.cs:83`); `v >= PlaceAtoms.KindBase` means "gossip-shareable"
(`Communication.cs:125`); `MemorySalience.For` routes decay policy by which band an id falls in
(`MemorySalience.cs:15-26`). The scheme already collided once (doors landed on the place-kind base) and
nothing guards the next.

Replace it with **one `AtomName` enum** — the name *is* the identity, the compiler enforces uniqueness,
there is no arithmetic and no band to collide with — and **one frozen catalog** keyed by `AtomName` that
holds, as first-class data, the per-type metadata those band-checks currently fake. Identity atoms
(`Civilian`, `Tavern`) and property atoms share one vocabulary. The **`AtomBag` storage is unchanged**:
it already sorts / binary-searches / merges purely on the int key (`AtomBag.cs`, `AtomTypeId.CompareTo`),
agnostic to where that int came from.

**Why this is the foundation — one line each:**
- *Hygiene.* The bands collide silently; an enum cannot.
- *Cognition de-risk — the refactor IS the fix.* Recognition matches by **type-identity**:
  `SignatureDistance` compares `Type.Value` for *exact equality* and never differences the ids
  (verified `MeaningsStore.cs:139-156`). Today's innate seed recognizes nothing because its prototypes
  live at ids 1–4 (`MemorySeeds.cs`) while perception emits 1000–3999 — *different keys, so they never
  match.* `AtomName` makes **one canonical name per atom** a compile-time fact: a Phase-C stereotype
  seeded on `AtomName.Civilian` and a perceived `AtomName.Civilian` are the *same key by construction*.
- *No-label enforcement.* The catalog has a *tone* face and a *salience* face; it has **no verdict
  face**. After Phase A there is, structurally, nowhere on an atom to write "threat / danger / predator."
  Invariant #1 of the roadmap stops being a rule and becomes a type.

**Recognition-safety note.** Nothing persists agent memory across the migration (the sim re-seeds at
spawn), and *within* a run every producer of a given atom references the same `AtomName` member. So the
actual integer behind `AtomName` is irrelevant to correctness — we are free to number the enum however
is cleanest. (If a save/load of agent memory is ever added, that is the moment numeric stability would
matter; it does not today.)

## Decisions (locked)

### D1 — `AtomName`: one flat enum; families flattened to explicit members

Every former `Base + (int)subEnum` pair becomes a **named member**: the `EntityKind`s →
`Player/EnemyClass/EnemyMonster/Civilian/StaticNpc`; `ResidentRole` → `Resident/Keeper`; the ~35
`ActivityKind`s → `ActIdle … ActPatrol`; the ~40 `BuildingKind`s → `PlaceTavern …`; the somatic trio →
`SomaticHunger/Energy/Fear`; the place facts → `PlaceProvisions/PlaceDanger`; races → enumerated
`RaceBreton …` (see D1a).

The **source enums stay** (`EntityKind`, `ResidentRole`, `ActivityKind`, `BuildingKind` are used across
behavior/employment/spawning); `AtomName` is the *perceivable/memory identity*, reached via a `From(...)`
map — the atom doc's "two indexes of one relation." **Stamp ergonomics are preserved:**
`PerceivableAtoms.Kind(EntityKind)` keeps its call shape but now returns the mapped `AtomName` (so the
seven stamp sites barely change — see Architecture).

**D1a — Race enumeration (plan-time verification).** `Race(int raceId)` is the one *open* range today
(cast from Daggerfall race ids). Confirm the race set is **closed** (the fixed Daggerfall race roster)
and enumerate it as `AtomName.Race*` members. If town generation can emit an unbounded/unknown race id,
keep race as a single parameterized exception with a `RaceUnknown` fallback rather than inventing
members nothing produces.

### D2 — The catalog: a frozen table keyed by `AtomName`, with the faces Phase A needs

`AtomCatalog[name] → { Category, Salience, Shareable, (reserved) Tone }`:
- **Category** — the *family* the band used to encode in the integer's high digits, now a first-class
  field: `Kind | Role | Race | Activity | Somatic | PlaceKind | PlaceProvisions | PlaceDanger`. Derived
  predicates: `IsIdentity = Category ∈ {Kind, Role, Race}`. **This single field replaces every band
  range-check that asks "what kind of atom is this."**
- **Salience** — the `AtomMeta(strength, flags)` that `MemorySalience.For` routes by band today
  (PlaceKind → Innate, PlaceDanger → Surprise, Provisions/default → Ordinary). Now a per-atom cell.
- **Shareable** — bool: gossip-relayable (place facts) vs private (percepts). Replaces the
  `>= PlaceAtoms.KindBase` gossip gate.
- **Tone** (valence + arousal) is **reserved** — a declared cell that Phase B fills; Phase A leaves it
  neutral. (Reactions / face-3 and the affordance face are out of scope — see D4, Deferred.)

### D3 — Migrate the four band-as-logic consumers to catalog queries

The dangerous sites (band layout encoded as logic) become catalog lookups:
- `MemorySalience.For(atom)` (`MemorySalience.cs:15-26`) → `AtomCatalog[name].Salience`. Caller
  `AgentMemoryRegistry.cs:84` unchanged.
- `PerceivableRegistry.Signature` (`:83`, `>= ActivityBase`) → keep atoms where `IsIdentity`.
- `PerceivableActivitySystem` activity extraction (`:46`, `>= ActivityBase && < +1000`) → find the atom
  whose `Category == Activity`.
- `Communication.cs` gossip gate (`:125`, `< PlaceAtoms.KindBase`) → relay atoms where `Shareable`.

### D4 — `AffordanceCatalog.Public(BuildingKind)` stays (affordance-emergence deferred)

`AffordanceCatalog.Public` (`Affordance.cs:46-71`) switches on `BuildingKind`, **not** on atom bands —
it is *not* a band consumer, so it does not block the `AtomName` migration. Rewiring it to
molecule-pattern matching touches the planner's candidate generation
(`OddSystem.GatherAds/Discover`, `:427/568/571/576`) and carries the atom doc's open "authoring-home"
question. It is **out of Phase A scope**; affordance-emergence is a clearly-marked follow-on in the atom
arc. Phase A leaves `ActivityCatalog.Spec` and the planner untouched.

### D5 — Drop the orphan `MemorySeeds`

`MemorySeeds`' abstract `Predator/Food/Water/Conspecific` at ids 1–4 (`MemorySeeds.cs:14-40`) is
scaffolding that matches nothing perception emits and is called only in tests. **Remove it** (and the
misleading `MemorySeedsTests`). Seed *content* returns in Phase C as kind-keyed stereotypes that
reference real `AtomName` members — Phase A makes the recognition *path* correct; it is not the home of
the seed list.

### D6 — Migrate tests from band-ints to catalog semantics

Tests asserting raw band values (`PlaceMemoryWriteTests` `6000/6001/5000+Tavern`; `PerceivableAtomsTests`
`1000+Civilian`, `2000+Keeper`, …) or band ranges (`PerceivableSeedingTests` role-in-`RoleBase..+1000`;
`PerceivableActivityTests`, `PlaceSeedingTests`, `LearnedValenceReinforceTests`, `MemoryWriteSystemTests`
— full list in the blast-radius) assert an implementation detail that disappears. Rewrite them to assert
**catalog semantics** (`AtomName.PlaceTavern.Category == PlaceKind` and `.Salience == Innate`; a
`Civilian` atom `IsIdentity`; a place fact `Shareable`). Recognition/blend tests stay green by
construction (type-identity preserved).

## Architecture / blast radius (mapped, file:line from the two reports)

```
ENUM (D1)         replaces the 5 arithmetic helpers in PerceivableAtoms.cs:16-19, PlaceAtoms.cs:8
                  + the const AtomTypeIds in SomaticAtoms.cs:7-9, PlaceAtoms Provisions/Danger
CATALOG (D2)      new: AtomCatalog[AtomName] = { Category, Salience, Shareable, (Tone reserved) }
BAND-AS-LOGIC →   MemorySalience.For            MemorySalience.cs:15-26  → .Salience
catalog (D3)      Signature (>= ActivityBase)   PerceivableRegistry.cs:83 → IsIdentity
                  activity extract (4000..4999) PerceivableActivitySystem.cs:46 → Category==Activity
                  gossip gate (>= 5000)         Communication.cs:125     → Shareable
STAMP SITES →     Kind/Role/Race                TownLoader.cs:544-547
AtomName via map  Activity                      PerceivableActivitySystem.cs:30 (TryActivity)
                  Somatic                       SomaticPerceptSystem.cs:26-28
                  PlaceKind                     AgentMemorySeeding.cs:40
                  PlaceDanger                   PlaceDangerSystem.cs:49
SWITCH (kept)     AffordanceCatalog.Public      Affordance.cs:46-71 (BuildingKind, not bands — D4)
SEEDS (D5)        drop MemorySeeds 1-4          MemorySeeds.cs:14-40 + MemorySeedsTests
TESTS (D6)        ~12 range-checks + boundary asserts → catalog-semantic asserts (Headless/Sim.MemoryTests/*)
UNCHANGED         AtomBag / AtomTypeId / Atom backings; ActivityCatalog.Spec; the planner
```

## Validation

- **Build / grep gate.** After migration the band `Base` constants exist **only** inside the catalog and
  the `From` maps (or are deleted); **no `>=`/`<` band range-check survives outside the catalog.** A grep
  for `ActivityBase` / `KindBase` / `>= 7000` returns only catalog-internal hits.
- **Catalog completeness (unit).** Table-driven over all four source enums: every source value maps to a
  unique `AtomName`; every `AtomName` has a catalog entry (no holes, no dup).
- **Behaviour-preserving (unit).** The de-banded consumers are *identical* to the old ones: per-atom
  `Salience` matches old `MemorySalience.For`; `Signature` excludes exactly the old `>= ActivityBase`
  set; the gossip gate relays exactly the old `>= 5000` set.
- **The payoff (unit) — the mismatch is gone.** Seed a category on `AtomName.X`, perceive `AtomName.X`,
  assert `Recognize` fires. Today this is impossible across the 1–4 / 1000+ gap; this test is the proof
  Phase A delivered its reason.
- **Soak (regression gate).** Phase A is **behaviour-preserving** — a short Gothway/Gallotale soak must
  match the M0 baseline (population, chains, Flee/kills). A behavioural *delta* means the refactor
  changed semantics; that is a bug, not a feature.

## Scope / deferred

- **In:** D1–D6 — the enum + catalog + the four band-as-logic migrations + the seven stamp sites +
  orphan-seed removal + test migration.
- **Deferred (atom arc, follow-on):** affordance-emergence (`AffordanceCatalog.Public` → molecule
  pattern-match; the authoring-home question); the **Tone** *content* (Phase B fills the reserved cell);
  face-3 **reactions** (engine deferred per the doc); the tagged `AtomValue` union
  (symbol/count/entityRef) — only on demonstrated need.
- **Not touched:** `ActivityCatalog.Spec`, the planner, the `AtomBag` backings.

## Sequencing (for the plan)

D1 (enum + `From` maps) → D2 (catalog skeleton: `Category`/`Salience`/`Shareable`, `Tone` reserved) →
D3 (migrate the four band-as-logic consumers) → migrate the seven stamp sites onto `AtomName` →
D5 (drop orphan seeds) → D6 (migrate tests). The `From` maps let old ids and new names coexist
mid-migration so the build stays green throughout; the **behaviour-preserving soak is the final gate**.
