# S4 — Conscience: a valence overlay → shame / guilt / stigma

**Goal.** The payoff stage, and a *pure composition* of S1–S3 plus a seed: **conscience is
MEANINGS nodes (S3) over `(self, verb, object)` that charge a candidate action with anticipated
valence, read through emotion-as-controller (S2), biasing `V` at the same marketplace join drives
already use.** From this one mechanism fall shame, guilt, stigma, taboos, the proud-agent-who-
starves, and crime-as-tag — none of which need their own system. This is the answer to "does
begging cause shame?" — *yes, as one authored/learned node*, not a hand-coded feature.

**Atoms grounding.** Conscience doc: the **superego** = a learned family of MEANINGS nodes with
valence over **self-as-subject** actions; it doesn't choose, it **biases V** (a second
sign-opposed valence source at the marketplace MAX — Freud's drama compiled to one join).
**Guilt** = a deplete pole (target = a reparative act); **shame** = tracks the **social-standing
pole** (a global self-read, slow to discharge). Two regimes: **preference** (graded weight) vs
**taboo** (hard-cull, hysteresis). The **one new commitment**: conscience may hold an *upward*
hard-cull edge (override a deficiency drive → the hunger-strike/martyr), which is what makes it
morality not mere preference — rare, personality-gated (zealot/pragmatist). **Installer**:
altruistic punishment → a high-arousal social episode → consolidates (S3) into a self-referential
aversive node (external authority → internal voice), atop an **innate seed** (harm-aversion,
fairness, in-group). **Crime = a legitimacy tag on a normal action**, not a special verb.

**Makes emergent.** Shame about begging, stigma toward beggars (and pity), refusal of tainted
help, taboos, guilt + reparation, and — once `Take`/`Attack` are live — crime and its guilt:
all from this overlay + S1–S3.

---

## Current state (anchors)

- **Nothing.** No social-standing pole, no self-referential valence, no norms. Begging is, to the
  agent, a neutral coin-getting chore; others perceive a beggar only via the transaction
  (`RequestSystem.Resolve`); there is no stigma and no shame.
- **Prereqs in place after S1–S3:** the membrane (`interpret()`/`SubjectiveView`) to read a
  forecast self-action and to let *others* read a beggar's molecule; emotion-as-controller (the
  `Affects` register + the marketplace prune) to surface anticipated guilt/shame and bias `V`;
  the MEANINGS store to *hold* conscience nodes and to learn them by consolidation.
- **`ActionCatalog`** already frames crime as a legitimacy tag (Steal = Take-without-permission)
  — the catalog is ready for conscience to charge `(self, Take, not-mine)`.

## The gap

There is no OUGHT layer. Every "should/shouldn't" we want (don't beg if proud; don't steal;
shun the cheat) would otherwise be a bespoke system. Conscience is the general mechanism that
makes them one shape.

---

## Design

### S4.1 — Conscience nodes + the marketplace-join read (shame, graded)

A conscience node is a **MEANINGS node (S3) keyed on `(self, verb, object/role)`** with aversive/
appetitive valence + confidence + a `sacred` flag. At the marketplace join, for each candidate
ad, match `(self, ad.Verb, ad.Target/place)` against conscience nodes; a match surfaces an
**anticipated-affect residual (S2)** that re-scores the ad — *graded weight* for a preference.
This is `interpret()` pointed at a **forecast** instead of a percept (the conscience doc's
"arises / fills / shortcut" applied to your own next act).

Seed a **begging-shame** node: `(self, Beg) → aversive`, **scaled by personality** (pride / low
Warmth → strong; shameless → ~0). Result, for free: a proud agent's `Beg` ads are penalised, a
shameless agent's are not — *the same poverty, opposite behavior.* This is the "proud agent would
rather starve" the user asked about.

### S4.2 — Social-standing pole + stigma (others' reading)

Add a **social-standing pole** (an S2-style pole = reputation). **Stigma needs no new code**: it
is *others'* `interpret()` (S1) reading a beggar's observable molecule (Doing Beg) through *their*
"beggar" MEANINGS node (S3) → a valence adjustment. A **cold** agent's beggar-node is aversive
(disdain → lower regard, less likely to give); a **warm** agent's is pitiable (sympathy → more
likely to give). *Same beggar, opposite read* — the membrane delivering exactly what S1 promised.
**Shame** = the agent's own read of its standing pole (a global self-read), which a begging-shame
node and others' disdain both feed.

### S4.3 — Taboos, the installer, guilt, crime (the rest of the overlay)

- **Taboo (hard-cull) + the upward edge:** strong `sacred` nodes fail an ad's self-state
  precondition (the action is *unthinkable*), with hysteresis; only `sacred` nodes get the
  *upward* edge that can override a deficiency drive (the martyr) — rare, personality-gated.
- **Installer (learned norms):** altruistic-punishment events (a violator shunned) are
  high-arousal episodes (S2) that consolidate (S3) into self-referential aversive nodes — external
  enforcement becomes internal voice — atop an **innate seed** (harm-aversion/fairness/in-group).
- **Guilt:** a deplete pole whose target is a reparative act; doing the repair discharges it
  (moral injury = discharge blocked, the spiral pinned — S2 spiral machinery).
- **Crime = tag:** conscience nodes over `(self, Take, not-mine)` / `(self, Attack, innocent)`
  charge guilt; the action is normal, the *tag* is the conscience valence. Lands when `Take`/
  `Attack` go live (action-catalog reserved verbs).

### Why this is composition, not new machinery

Conscience reads through **the same** `interpret()` (S1), the **same** emotion-as-controller and
marketplace prune (S2), and **is** MEANINGS nodes (S3). The only genuinely new pieces are the
social-standing pole, the self-action node key, and the *upward hard-cull edge* (the conscience
doc's one new commitment). Everything else is the substrate, pointed at the self.

---

## Staged sub-steps

1. **S4.1** — conscience-node shape (MEANINGS over self-actions) + marketplace-join graded read +
   a personality-scaled begging-shame seed → proud agents avoid begging.
2. **S4.2** — social-standing pole + stigma/pity via others' `interpret()` of the beggar molecule;
   shame as the self-read of standing.
3. **S4.3** — taboo hard-cull (+ hysteresis, + the rare upward sacred edge); the altruistic-
   punishment installer (learned norms via consolidation); guilt pole + reparation; crime-as-tag
   wiring (dormant until `Take`/`Attack`).

## What it retires / resolves

Nothing *exists* to delete (net-new). But it **resolves the begging finding principledly**: the
social-souring is no longer a hand-tuned `−0.2` grudge — begging is governed by *shame* (the
beggar's own cost) and *stigma/pity* (others' interpretation), both emergent. And it converts a
long list of would-be features (taboos, crime/guilt, shunning) into authored/learned nodes.

## Acceptance (per-stage gates: mechanism + invariants)

- **Invariants:** determinism (nodes are MEANINGS → fixed-point/key-order; `hash` for the
  martyr/sacred draws); single-writer standing pole; "ad is ad" preserved (conscience is a
  *valence source read in `V`*, not a verb switch); the upward cull stays rare (assert it can't
  fire from a non-`sacred` node).
- **Mechanism fires (direction only):** a high-shame (proud) agent does **not** pick `Beg` while
  a low-shame agent in the same state does (scenario); a cold agent's `SubjectiveView` valence
  for a beggar is negative while a warm agent's is positive (stigma vs pity, same beggar); a
  seeded taboo hard-culls its action.
- **Deferred to tuning:** shame/charity bars, sacred θ (martyr rate), guilt discharge rates,
  installer arousal threshold, the pride→shame mapping — all frozen placeholders for the
  post-substrate tuning pass.

## Risks / open questions

- **Depends on all of S1–S3.** Do not start S4 before the membrane, emotion, and MEANINGS exist —
  it is their composition; built earlier it would be another hand-coded special case (the very
  thing this track exists to stop).
- **Upward hard-cull is the one sharp edge.** Keep it rare and per-`sacred`-node with high
  confidence + hysteresis, or every principled agent suicides (the conscience doc's guardrail).
- **Innate vs learned balance.** Seed a *small* moral foundation; let the installer accrete the
  rest. Over-seeding hand-codes the morality we're trying to make emergent.
- **No combat/property yet.** Crime/guilt (`Take`/`Attack`) is wired but dormant until those
  verbs are live — build the tag path, don't force the behavior (proto discipline). Begging-shame
  + stigma are the live, testable cases now.
