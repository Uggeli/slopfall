# The drive engine — implementing *What Is a Drive?*

This builds the canonical drive architecture from the Atoms drive doc (`~/omat/Atoms/docs/
what_is_drive`) into the sim, replacing the thin scalar `NeedsData.V` + flat-`Weights` picture
with the doc's real structure: a drive is `pole × target → residual`, only true body poles are
stored-and-ticked, and everything else is a per-tick read.

It is scoped to the whole program the user locked: **full engine + social as a live directed
target-field + fear live (which pulls in the V2 threat layer)**. Discipline carries from
`fattening_roadmap.md`: behavior-preserving refactor first, then change behavior; tune after, not
during; test that systems are wired in and entities use them.

> **⚠️ Implementation status (2026-06-23 audit — see `cognitive_layer_audit.md` §2.1).** The
> data model below (4-field `DriveDef` + `LevelSource`, the Kahn `DriveGraph`, the projections, the
> derived-level discipline) is faithfully built. But **D0's prepotency cull is not actually wired by
> the gate-edges it builds:** `OddSystem`'s cull keys off a hand-tagged `Prepotent` bool set on only
> `Socialize`/`Visit`, so the `{hunger,energy,fear} ⊣ {social,goods,coin}` edges are recorded but
> inert for the goods/coin ads (`Buy`/`Steal`/`Beg`/`Gossip`) — a starving agent shops and begs
> freely. Making the gate cull by the *axis its edges name* is Phase-0 fix 0.4. The deleted
> drives/fear unit tests (commit `2cdc353a3`) also leave D0 unverified.

## The mapping (doc term → codebase)

| Drive-doc concept | Real name (doc) | Where it lives here |
|---|---|---|
| the whole construct | drive / motivation | `DriveDef` row in `DriveCatalog.Defs[]` |
| body pole (persistent level) | interoceptive / homeostatic state | a **Stored** axis in `NeedsData.V`, ticked by `NeedsSystem` (Metabolism) |
| target field (what it's about) | incentive salience / intentionality | per-tick instance minted from `SubjectiveView`/percepts (scratch) |
| felt residual | core affect | `AffectsSystem` register (derived per-tick read) |
| pole × target → live drive | incentive-motivation (Toates) | a scored ad in the marketplace (`OddSystem` join) |
| deficiency vs growth + gate | D-needs/B-needs, prepotency | `GateEdges` → `DriveGraph` (Kahn) → `OddSystem` gate |
| decision shortcut | somatic marker (Damasio) | affect pruning/biasing V (`Interpret`, `ConscienceFactor`) |

Key correction the doc forces: **coin is not a drive.** It's a conserved external quantity; the
want-for-coin is *instrumental* (hunger → provisions → coin), which is **planner propagation
through the ODD DAG** — deferred to V3. So `CoinDef`-as-a-need is interim scaffolding; we keep it
as a weak `Derived` read until real propagation lands. Same logic makes `GoodsDef` a derived read
of the larder (single source of truth — a stored copy would desync).

## The four-field `DriveDef` (canonical)

```
drive = ( ScoreField , UrgencyProjection ∈ {Max,Sum} , Satisfaction ∈ {Deplete,ResetOnPercept,None} , GateEdges )
```

Plus one engine field the doc implies but doesn't name — the **pole/derived split**, made explicit
so only real poles get stored + ticked:

```
LevelSource ∈ { Stored , DerivedCoin , DerivedLarder }
```

- **ScoreField** — the V weight (today `Weights`/`DriveDef.Weight`). Unchanged values.
- **UrgencyProjection** — how a directed drive's target *field* collapses to scalar urgency:
  `Max` (fear — the worst threat dominates; three foxes must not sum to panic) vs `Sum` (social —
  a crowd genuinely adds bonding pull). Moot for scalar/undirected drives (single implicit target);
  authored anyway so it's a real per-drive fact, not a global choice.
- **Satisfaction** — the discharge route. `Deplete` (hunger/energy/social: an action writes the
  level down). `ResetOnPercept` (fear/safety: residual tracks the threat percept, discharge = its
  absence; a vigilance floor keeps it off zero — a parameter, not bottomlessness). `None` (growth/
  curiosity: no discharge node). The engine's old `Derived`/`DecayTowardBaseline` enum is *not* this
  taxonomy — `Derived` was really "a Deplete drive whose level lives in an external store," which is
  exactly what `LevelSource` now names. Of the three routes, only `Deplete` is exercised by the
  current roster; `ResetOnPercept` lands with fear (V2b), `None` with curiosity (later).
- **GateEdges** — the prepotency DAG: `{hunger,energy} > {goods,coin,social}` today, growing to
  `{hunger,energy} > safety > {social,curiosity}` with fear. Two edge types: **PrepotencyHardCull**
  (deficiency ⊣ growth — can hard-cull the gated drive's ads past threshold) and
  **DesperationGraded** (hunger ⊣ safety — starvation overrides fear but stays graded, never culls,
  or a starving bunny couldn't flee). Required property: every deficiency pole gates every growth
  pole *directly*, never only transitively.

`DriveGraph` builds from the edges with the same acyclicity + Kahn topological-order guarantee as
the ODD tree, and `OddSystem`'s gate consumes it: graded multiplicative weight in the mid-range,
hard cull past the threshold with hysteresis (enter ~0.7 / exit ~0.6), per-edge.

## Phases (sequenced; each committable, tested)

**D0 — canonical engine, behavior-preserving.** Complete the deferred switch onto `DriveCatalog`
(`NeedsSystem` drift + `OddSystem` weights stop reading `ActivityCatalog.Weights/DriftPerHour` —
provably identical, the arrays match). Grow `DriveDef` to the four fields + `LevelSource`. Build
`DriveGraph` (Kahn) and route `PrepotencyGate` through it, reproducing today's
`max(hunger,energy)≥0.8 → cull` for the current roster. Tests green, Betony soak bit-identical. The
hysteresis (0.7/0.6) flip is a *separate* step where the soak is allowed to differ, with a recorded
reason.

**Phase 3 — famine flip** (the original subsistence Step 3, now trivial on the table): `GoodsDef`
→ `LevelSource=DerivedLarder`, `CoinDef` demoted; `EatHome` draws + larder-gates; the can-they-eat
metric; observe the famine. See `subsistence.md`.

**D2 — social as a live directed target-field.** First behavior-changing directed drive, on
perception that already exists (`SubjectiveView`/relations/occupancy): `UrgencyProjection=Sum`,
instance minted per-tick from the perceived social field. Proves the pole×target minting path.

**V2a — threat layer (new infra).** Minimal mobile hostile creatures at the edges; threat percepts
through `SenseSystem`→`SubjectiveSystem`; activate the `Attack` verb (`Take` already live).
`HealthSystem` already emits `DeathSimEvent` from `DamageEvent`.

**V2b — fear, live.** The directed fear drive (`Max`, `ResetOnPercept`, the desperation edge) +
emotion-as-controller (completes ambiguity as threat; somatic-marker pruning → `Flee` dominates);
ignition floor as trait anxiety; maintenance vs runaway spirals; escape-affordability (fed timid →
sawtooth, starving timid → clamp). S4 crime-as-tag for `(self,Attack,innocent)`.

## Carried discipline

- **Behavior-pull.** Fear/curiosity/addiction *machinery* is authored in the table now; their
  *behavior* arrives only when their perception/learning sources do. Don't invent content drives we
  can't feed.
- **Tune after.** All constants (gate thresholds, ignition floor, vigilance floor, projection
  scales) are frozen placeholders; one tuning pass on the regional soak after a coherent chunk.
- **Citations owed.** The doc's attributions (Hull, Toates, Berridge, Maslow, Damasio, Friston, …)
  are from memory; verify against primary sources before anything formal.
