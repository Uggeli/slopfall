# Fear — the first directed drive, and fight-or-flight (V2b)

The fear vertical of the drive program (`drive_engine.md`). It makes fear a real
drive — not a scalar pole but the drive doc's **directed drive**: a pole × a target field
over perceived threats — and stands up the **emotion-as-controller** loop, the **flight** and
**fight** responses, and a real **combat** layer on top of the V2a threat infrastructure.

> **⚠️ Implementation status (2026-06-23 audit — see `cognitive_layer_audit.md`).** This doc
> describes intent; the live code diverges in three places. (1) The controller's *shape* is
> faithfully built (`NeedsSystem.FearLevel`), but the **prepotency cull it relies on is partly
> inert** — the `Fear ⊣ {social, goods, coin}` gate only removes `Socialize`/`Visit` ads, never the
> goods/coin ads, so a frightened agent can still shop/beg (§2.1, §2.4). (2) **Fear-completion fires
> only on objective creatures**, not on ambiguous cues — so the ignition bifurcation below cannot
> actually trigger from a faint non-creature read (§2.3). (3) The unit tests below were **deleted**
> and not migrated (§2.1).

## What fear is here

Fear is `NeedAxis.Fear`, a sixth drive in the engine, but special on every authored axis:

| Field | Value | Why |
|---|---|---|
| `Projection` | **Max** | the worst threat dominates — three distant foxes must not *sum* to panic |
| `Satisfaction` | **ResetOnPercept** | no drift; the residual tracks the threat percept and discharges on its absence |
| `Level` | **DerivedThreat** | computed by a controller, not stored-and-ticked |
| `Gates` | Fear ⊣ {social, goods, coin} **HardCull** | a frightened agent's leisure can't manifest (fear joins the prepotency sources) |
| (incoming) | Hunger ⊣ Fear **DesperationGraded** | starvation overrides fear — the escape-affordability edge |

`HarmAvoidance` (a sixth personality trait) is the **vigilance floor** — trait anxiety as a baseline
arousal.

## The controller (`NeedsSystem.FearLevel`)

Per agent, per tick: `V[Fear] = smooth( prev, target )`, where the target is the **Max** over the
agent's perceived-threat field, each cue run through the completion operator:

```
CompleteThreat(c, floor, arousal) = c + (1−c)·gain·(floor + spiral·arousal)
```

- **clarity `c` = f(distance)** — a creature at the edge of sight is an *ambiguous* cue.
- **completion** fills the ambiguous `(1−c)` remainder from the vigilance floor and the carried
  arousal (`prevFear`) — the drive doc's **ignition bifurcation** `c + (1−c)·gain·floor > threshold`:
  a **bold** soul (floor ≈ 0) leaves a faint cue faint and ignores it; a **timid** one completes it
  toward threat and can bootstrap fear *from rest*; rising arousal completes harder still (the
  positive-feedback **spiral**).
- **smoothing**: fast to flood, slow to ebb; with no threat in view the target is the floor, so fear
  decays back to vigilance (**ResetOnPercept**).

## Fight-or-flight

Both are innate, `FearDriven` ads that only win when fear is loud; personality picks which (opposite
`HarmAvoidance` gates — the **timid flee**, the **bold fight**):

- **Flee** runs away from the nearest threat. The fear delta is the *somatic-marker promise* (it
  prunes the marketplace toward running); the real relief is physical — fleeing breaks the percept and
  the controller resets fear.
- **Attack** closes on a creature; `CombatSystem` strikes it (a `DamageEvent`) — `HealthSystem` kills
  it, `LifecycleSystem` despawns, `CreatureSystem` respawns, so **creatures fight to the death**, and a
  struck creature **retaliates** against its attacker. **Guards** get a combat boost and hunt the
  nearest creature region-wide (proactive patrol). The **crime-as-tag** (striking an innocent installs
  Attack-guilt) is wired and tested but dormant — nothing makes an agent attack a non-threat yet.

**Escape-affordability** falls out of the desperation edge: a **fed** agent flees freely; a
**starving** one's fear is graded down hard, so it can't afford to run and **pins** — the doc's
"trapped by a competing need," here arising from drive competition rather than fiat.

## Observed (40-day Betony)

Soak **PASSES**; population stable; combat live (deaths now nonzero). But the behavior is *rare* at
the aggregate, for two reasons that are both **frozen placeholders** awaiting the one tuning pass:

- **The famished baseline** (97% pegged at hunger ≈ 1.5) means almost everyone is graded down by the
  desperation edge — they can neither afford to flee nor engage. This is the predicted
  "trapped by hunger" pinning, and it's *correct*, but it suppresses visible fear behavior until the
  economy is tuned to a survivable equilibrium.
- **Sparse creatures** (3 across a whole region) — proximity to a threat is rare.

So the soak shows the *mechanism* alive (1 death, fear pinning the starving) while the *frequency*
waits on tuning. **These dynamics are currently unverified:** the unit tests that backed them
(`FearTests`, `AffectsTests`) were **deleted** in commit `2cdc353a3` (2026-06-22) and not migrated to
the surviving projects. Re-establishing them — ignition bifurcation (bold ignores / timid bootstraps),
rise-near-threat / reset-when-gone, escape-affordability (fed flees / starving pins), an attacker
killing a creature, the crime-tag, and the prepotency hysteresis — is a Phase-0 follow-up
(`cognitive_layer_audit.md` §2.1). They are all pure static methods (`CompleteThreat`,
`DesperationFactor`, `PrepotencyGate`), so trivially unit-testable.

## Carried discipline

- **Tune after, not during.** Every constant here — the vigilance floor, completion/spiral gains,
  rise/decay rates, desperation strength, creature count/lethality, combat damage — is a frozen
  placeholder. The one tuning pass (now that the whole drive+fear program is in) raises food supply to
  a survivable baseline and balances creature pressure, after which fear/flight/combat become
  *frequently* visible and measurable.
- **Hysteresis** (enter 0.7 / exit 0.6) on the prepotency cull landed here, the natural home now that
  fear adds an intermediate rung.
