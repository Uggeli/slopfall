# L4 — Directed drives

**Goal.** Make the subjective view (L3) *behavioral* by letting agents act **on specific
people**: seek out a friend, avoid the one who refused you, go ask a likely giver for help —
all as first-class ads in the same marketplace, scored by the same uniform `V`. Today the only
person-directed behavior is `RequestSystem`'s bespoke alms-seeking, hand-wired outside the ad
loop; L4 generalizes that one case into the general mechanism and folds the special case back
into the marketplace.

**Atoms grounding.** Drive doc: a **directed (mental / target-pole-led) drive** carries its
target as a *field* — fear-of-X, curiosity-about-Y, social-bond-with-mate; *"motivation =
internal state × incentive stimulus."* World doc: smart objects advertise; **persons advertise
directed actions**. Convergence note (`decision_architecture.md`): *"persons advertise directed
actions (Chat/Ask), so `RequestSystem`'s bespoke discovery folds into the same marketplace
later … two kinds of to-do: self-serving activities (at places) vs directed actions (on/with
agents)."* L4 is that "later."

---

## Current state (anchors)

- **The one existing directed drive** — `RequestSystem` (`Systems/RequestSystem.cs`): a
  destitute agent (`CoinDef ≥ PovertyThreshold=0.8`, daytime) runs `PickTarget` →
  `BeginJourney` → `AskJourneyEvent` → on arrival `Resolve` (grant/refuse). `PickTarget`
  (`:187`) is **bespoke discovery**: sort the dossier by `Regard` (deterministic id tie-break),
  pick the first solvent contact; fall back to the richest keeper. `Resolve` (`:135`) computes
  a charity `bar` (keeper vs friend, shifted by the giver's `Warmth` trait), and on success
  emits `CoinTransferEvent` + `HelpGrantedEvent` + the two `RelationImpulseEvent`s (asker
  `ReceivedHelp +0.3`, giver `GaveHelp +0.05`); on failure the `WasRefused`/`RefusedToHelp`
  pair. Constants: `AlmsAmount=0.25`, `GiverKeepsAtLeast`, `JourneyTimeoutGameMinutes`,
  `KeeperCharityBar`, `FriendBar`.
- **Ads are place-directed** — `Ad { Verb, Building, X, Z, Spec }` (`Systems/Affordance.cs`); no
  `Target`. `GatherAds` (`ActionDiscovery.cs:47`) collects innate + known-buildings + the
  employer's workplace; `Discover` offers building-kind affordances. **No person-directed ads.**
- **Person verbs exist but as interrupts** — `Chat` (greeting, `PerceptionSystem`), `SeekHelp`
  (the alms journey). They never enter `V`/argmax.
- **L3 already scores `ad.Target`** — `RelationFactor` (L3.2) has the `!ad.Target.IsNone` branch
  ready; it returns `1 + RelationGain·Regard(target)`. L4 just needs to *produce* ads that carry
  a target.

## The gap

Directed behavior is hand-wired and one-off. There is no general "act on a person" path through
the marketplace, so the subjective dossier (L3) can only color *place* choices, not drive
*person* choices. And `RequestSystem`'s `PickTarget` duplicates, in bespoke form, exactly the
collect→score→argmax the marketplace already does — the convergence's stated redundancy.

---

## Design

### L4.1 — `Ad.Target`

Add a person field to the ad (default `None` = place/self ad):

```csharp
public struct Ad
{
    public ActivityKind Verb;
    public int Building;       // -1 = at-self / street
    public EntityId Target;    // None = place/self ad; else a person-directed ad   ← NEW
    public float X, Z;
    public ActivityCatalog.Spec Spec;
}
```

`RelationFactor` (L3.2) already consumes it. `ExecutionSystem`/journey use `X,Z` snapshotted at
decision time; the target *moving* is handled by perceptual staleness on arrival (Atoms: the
wasted journey is the membrane being honest), same as any stale place ad.

### L4.2 — Person-directed ads in `GatherAds`

After the place ads, fold in directed ads for **relevant** persons — the marketplace form of
"persons advertise directed actions":

```csharp
// candidate persons = dossier contacts above a familiarity floor  ∪  currently sensed nearby
// (both already bounded: dossier by who you've met + L1 decay/L2 prune; Sensed by range)
foreach (var other in DirectedCandidates(ctx, agent))   // KEY ORDER → determinism
{
    if (!ctx.Position.TryGet(other, out var p)) continue;      // dead/absent target ⇒ skip (L2 dangling-ref)
    foreach (var verb in DirectedCatalog.For(verb))            // Visit, Chat, Ask
        ads.Add(new Ad { Verb = verb, Building = -1, Target = other,
                         X = p.X, Z = p.Z, Spec = ActivityCatalog.SpecFor(verb) });
}
```

**Bound the candidate set explicitly** (no silent truncation — `log`/document the cap):
persons above a familiarity floor, plus those currently sensed, capped at a small `N` (analogous
to `MaxPartners=6`). The dossier is already small after L1 decay + L2 prune, so this is cheap.

### L4.3 — Directed-drive preconditions + scoring (all uniform)

Each directed verb is an ad with **Preconditions (data, gate Collect)** and is scored by the
**same uniform `V`** via `RelationFactor(ad.Target)`:

- **`Visit(person)`** — "seek my friend." Precondition: `Familiarity ≥ visitFloor`. Served
  delta: `SocialDef−`. `RelationSensitive` ⇒ regard toward the target lifts the score → you
  preferentially visit those you like; you never generate a *positive-value* visit to someone
  you dislike (negative regard drives `RelationFactor` below 1, the gap-score wins elsewhere).
- **`Chat(person)`** — lightweight street/colocated talk; folds the current greeting interrupt
  into an ad when the person is sensed.
- **`Ask(person)`** — **the alms drive, generalized.** Precondition: `CoinDef ≥ PovertyThreshold`
  (the poverty pole — a directed drive whose *physical pole* is poverty and whose *target field*
  is "a likely, solvent, warmly-regarded giver"). Scored by `RelationFactor` (ask those who like
  you / you trust) — which **reproduces `PickTarget`'s regard-sort as emergent argmax**, not
  bespoke code.

"Avoid the one who refused me" is now doubly expressed: L3 lowers any *place* ad where the
refuser is present, **and** L4 never produces a positive-value `Chat`/`Visit`/`Ask` toward them
(their `Regard` is negative from the `WasRefused` memory). Avoidance is emergent from one sign.

### L4.4 — Fold `RequestSystem` into the marketplace

The discovery+selection half of `RequestSystem` (`PickTarget` + the poverty trigger) **moves
into `GatherAds`/`V`** as the `Ask` ad above. What remains of `RequestSystem` is the **Effect of
the `Ask` action** — the resolution on arrival: the charity `bar` (keeper/friend + `Warmth`
shift), the `CoinTransferEvent`, and the four memory/impulse emissions. That is the action's
*Effect* in Atoms' Flags→Actions→**Effects** model, and it stays exactly as written. `PickTarget`
is **deleted** (selection is now V-argmax); `BeginJourney` becomes the normal Moving→Doing
journey `ExecutionSystem` already runs for any ad with `X,Z`.

This is the convergence endpoint: one marketplace, two ad shapes (place-directed, person-
directed), one uniform scorer, special-case discovery dissolved.

---

## Staged sub-steps

1. **L4.1** — add `Ad.Target`; thread through `Ad` construction sites; confirm `RelationFactor`
   reads it. No behavior change (no person ads emitted yet).
2. **L4.2** — `DirectedCatalog` + person-directed ad generation in `GatherAds`, bounded +
   logged. Emit `Visit`/`Chat` only; verify ad counts stay sane in the soak.
3. **L4.3** — directed preconditions + `RelationSensitive` scoring; behavioral check that
   agents seek friends / avoid disliked others.
4. **L4.4** — add `Ask` as the directed alms ad; move `RequestSystem` to resolution-only; delete
   `PickTarget`. Verify the alms path's behavior matches (or improves on) the bespoke version —
   the existing `EconomyAndRequestTests` become the regression guard (rewrite the brittle ones
   as behavioral expectations per the testing discipline).

## Per-stage gates (mechanism + invariants — provable on placeholders, no tuning)

- **Invariants (hard):** determinism preserved (candidate iteration key-ordered, selection
  deterministic, `Resolve` tie-breaks unchanged); **"ad is ad" holds** (person ads scored by the
  same uniform `V`, no verb switch); coin conservation across alms unchanged (`LedgerRegistry`);
  the candidate set is **bounded and the cap logged** (no silent truncation).
- **Mechanism fires (qualitative, direction-only):**
  - **Scenario:** with A as B's friend and C as B's refuser, B generates a positive-value
    `Visit(A)` and **no** positive-value directed ad toward C — seek/avoid from one regard sign.
  - The **alms path still functions** through the marketplace after `PickTarget` is deleted:
    destitute agents still produce `Ask` ads and can receive help; givers still skew
    warm/high-regard (the emergent `PickTarget`) — prove it works, not at what rate.

Deferred: the directed-homophily *rate*, the alms help *rate*, `visitFloor`/cap `N`, directed-ad
ranking vs. place ads. **With L4 in, the whole stack is up — this is where the single tuning
pass begins** (`living_world.md` → *Tuning*).

## Numbers (frozen placeholders — tuned only when the whole stack lands)

`visitFloor`/familiarity floor, candidate cap `N`, the directed verbs and their `Spec` deltas,
and how strongly `RelationFactor` ranks directed vs. place ads. With L4 landed **the whole stack
is complete** — now, and only now, the regional soak becomes the tuning rig and all the coupled
L1–L4 numbers are fit together.

## Risks / open questions

- **Ad-count blowup.** Person-directed ads scale with known/sensed persons. The familiarity
  floor + sensed-only + cap `N` bound it; the dossier is already small post-L1/L2. **Log the
  cap** — silent truncation would read as "considered everyone" when it didn't.
- **Moving targets.** A person-directed journey chases a snapshot position; the target may move.
  Handled by perceptual staleness/revalidation (the journey may "miss" — realistic at depth-1).
  Predictive interception is a future ACTION-system concern, not L4.
- **Reciprocity / obligation (future).** Atoms' conscience doc models guilt as a *deplete* pole
  with a reparative target — "owes a favor back." L4 gives the substrate (directed ads on the
  dossier); turning `GaveHelp`/`ReceivedHelp` into an obligation drive ("repay the one who
  helped me") is a natural L5 / conscience-track extension, not built here.
- **Crime as a legitimacy tag (future).** Once directed actions exist, `Take`/`Attack` on a
  person are the same ad shape with a conscience valence overlay (`what_is_conscience.md`:
  "crime = legitimacy tag on a normal action"). Explicitly out of scope for L4; the directed-ad
  substrate is the prerequisite it was waiting on.
