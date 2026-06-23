What Is a Drive?
Companion to What Is a Bunny? and What Is a World?. The bunny doc lists drives as one row of Mental Things and defers the value function V. This doc opens that row up: what a drive actually is, why the scalar picture is wrong, and — since most of this has been studied for decades — what the real names are, so the model borrows the established vocabulary instead of inventing a private one.
The short answer (and why "scalar" was wrong)
A drive is not a level you keep topped up — low = bad, high = good, act to refill. That picture isn't wrong so much as under-resolved: it fuses three separate things into one number.

A drive is a transient relation between an agent and a target — minted when a standing pole (a body/arousal level) finds something to be about, felt as the residual between its two poles, and gone when that residual discharges.

The scalar mashed together:

Fused into one number
What it really is
the level
the pole — a persistent interoceptive/arousal state
(missing)
the target — what the drive is about (its object)
(missing)
the residual — what's felt, and what selection acts on


For the simplest case — hunger, where the target is outsourced to the food in front of you and the residual ≈ the deficit — the fusion is harmless, which is why the scalar view survives as long as it does. It shatters the moment a drive is directed (curiosity is about something), bottomless (curiosity is never "filled"), or felt as a mood (anxiety with no object). Those are target and residual phenomena a single number cannot hold.


Does it have a real name? (mostly yes)
"Drive" is itself the real term — it's the word motivation psychology has used since Hull. So the umbrella name is correct and worth keeping. What doesn't map to a single named theory is the structure we built; it's a synthesis. But nearly every piece has an established name, and adopting those names buys correctness, a literature to read, and credibility if any of this gets published.

In this framework
What researchers call it
Whose
the whole construct
drive / motivation
Hull (drive-reduction theory)
physical pole = read of a body atom
interoception; homeostatic / allostatic state, "body budget"
Cannon (homeostasis); Craig, Seth, Barrett
the felt residual = valence × arousal
core affect (the circumplex)
Russell; Barrett
arousal + what-it's-about
two-factor theory of emotion
Schachter & Singer
pole × target → a live drive-instance
incentive-motivation (internal state × incentive)
Toates; Bindra
the directed pull toward a target
incentive salience / "wanting" (≠ "liking")
Berridge & Robinson
deficiency vs growth drives
D-needs vs B-needs; prepotency hierarchy
Maslow
a drive being about something at all
intentionality (mental states have objects)
Brentano (philosophy)
curiosity = approach the surprising
information-gap theory; novelty-seeking
Loewenstein; Cloninger
emotion = the felt prediction-error
interoceptive inference; predictive processing
Seth; Clark, Hohwy
emotion also acts to close the gap
active inference (act to minimize error)
Friston
emotion as a fast decision shortcut
somatic marker hypothesis
Damasio
affect standing in for reasoning
affect heuristic; affect-as-information
Slovic; Clore & Schwarz
reflex < emotion < deliberation
Pavlovian / model-free / model-based control; dual-process
Daw & Dayan; Kahneman
brief+object vs diffuse+lasting
emotion vs mood; free-floating anxiety
(standard affective science)
lifelong interpretive bias
temperament / traits (OCEAN; harm-avoidance, novelty-seeking)
Cloninger; Big Five


Recommendation: keep drive as the top-level word, and adopt the component names where they exist — call the physical pole interoceptive state, the live binding an incentive-motivation instance, the directed pull incentive salience / wanting, the felt residual core affect, and the decision-shortcut role the somatic marker. The synthesis stays yours; the parts get their proper labels.


Anatomy — the two poles
Every drive has two components at once. What we used to call "physical drives" and "mental drives" are not two kinds — they're one structure where a different pole leads.

Pole
Is
Real name
Persists?
arousal / body
a scalar — the bodily charge or homeostatic deficit
interoceptive / homeostatic state
yes (Metabolism ticks it)
target / directed
a field over entities — what the drive is about
incentive salience; intentionality
no (minted per-tick from perception/memory)


Physical drive = arousal-pole-led. Hunger is a body scalar; it doesn't point anywhere on its own — the target (food) is supplied by the affordance. Reading the pole is interoception.
Mental drive = target-pole-led. Fear points at the fox, curiosity at the novel thing, social at the bonded one. The drive carries its own target as a field; the scalar urgency is just that field's projection (its MAX, or its SUM). Which projection is per-drive content (proto p2 → B): **fear projects MAX** — the worst threat dominates, three distant foxes must *not* sum into panic — while **social projects SUM** — a crowd genuinely adds bonding pull. Swapping the projection flips which drive wins selection, so it's a real authored field in DriveDefs, not a global choice. (Distinct from the planner's *subtree-propagation* aggregator, which is also sum-vs-max but folds the ODD tree, not the target field — same operator, two sites.)

The "physical vs mental" split you started from is real, but the mechanism under it is which pole carries the target — the body (outsourced to affordance) or the field (intrinsic). Same structure, different lead.


Deficiency vs growth — and the gate
Orthogonal to the poles, drives differ in whether satisfaction shuts them off. This is Maslow's distinction exactly.



Deficiency (D-need)
Growth (B-need)
has a setpoint?
yes — minimize toward it
no — engaging is the reward
V does what
minimizes the deficit
maximizes engagement when allowed
examples
hunger, thirst, fatigue, safety
curiosity, play, exploration
acting on it
depletes the drive
doesn't deplete — may amplify


One asymmetry inside the D-needs: not every deficiency discharges the same way. Hunger and fatigue deplete — an action's delta writes the level down. Safety is regulatory aversion: its residual tracks the threat percept, and discharge is the percept's absence — fox gone, fear resets, no delta. (Its level never reads zero, but that's a vigilance floor — a parameter — not bottomlessness.) The satisfaction-model field below names the three routes: deplete (hunger), reset-on-percept (safety), none (curiosity).

Prepotency / gating: growth drives only win selection weight when deficiency drives are quiet. A starving bunny can't binky. This is not a special rule — it's a gate (one drive's output suppresses another's). The gates make drives a DAG, not a flat list — concretely a partial Maslow ladder ({hunger, fatigue} > safety > {social, curiosity}), with one required property: every deficiency pole gates every growth pole directly, never only transitively (proto p1: relying on the hunger ⊣ safety ⊣ curiosity path lets a safe-but-starving bunny binky — hunger must gate curiosity itself). The desperation edge hunger ⊣ safety (starvation overrides fear) is what makes it a ladder rather than two flat tiers. Where social sits is a species call — for the bunny it's a middle rung, gated by hunger and safety, gating nothing. The DAG needs the same acyclicity + topological-order guarantee as the ODD tree. Kahn again.

Prepotency is two-regime — Maslow resolved into the engine's existing cull/score split (p1, F8). In the mid-range the gate is a graded multiplicative weight in scoring: Maslow's own multi-motivation — a bunny at hunger 0.4 still plays, just less. Past a threshold it becomes a hard cull: the gated drive's ads fail their self-state precondition and never enter the market. "If the root is not satisfied, higher drives can't manifest" — literally can't manifest: unthinkable, not just unchosen, the same move the world doc already makes for fear ("a frightened bunny's Binky never enters the market"). The cull needs hysteresis (enter ~0.7, exit ~0.6) or behavior flickers at the boundary, and the thresholds are a natural personality parameter. Hard-cull capability is per-edge: only prepotency edges (deficiency ⊣ growth) get it — the desperation edge must stay graded or a starving bunny couldn't flee. And the cull removes ads, never the pole: the starving bunny still feels the curiosity; it just can't act on it. With the hard regime in place the division of labor is clean — the cull enforces prepotency structurally (p1: a starving bunny doesn't explore even with no beacon and an otherwise-empty market), the gate handles mid-range trade-offs, and the Object Zero beacon owns direction (seeking food rather than idling).

Comfort — the drive that isn't one. The old roster listed comfort beside hunger and safety, but it has no pole: nothing ticks it up, nothing discharges it. Comfort is the null-state read — no deficiency pole active, safety high — a derived condition, not a stored level. The flop is its behavioral signature: an ad whose precondition is the null-state itself. It joins the world doc's biomes ("no stored danger, no stored emotion, no stored biome"): read off the running state, never kept.


Drives are minted, not stored
Because a directed drive is a binding to a target, no target = no drive. Drives are created and destroyed dynamically as their targets enter and leave the perceived-or-recalled set. This is the incentive-motivation model (Toates): motivation = internal state × incentive stimulus — a product, so either factor at zero yields no drive.

Keep the distinction sharp:

POLE      persistent   the deficit / arousal level — ticked by Metabolism, always present as a LEVEL

INSTANCE  transient    pole × target, minted each tick — SCRATCH, like Marketplace / SubjectiveView

"Not all drives are always present" is true of the instances, never the poles. The body always has some hunger level; it does not always have a hunger instance (only when food is perceivable or recalled). Lose this and hunger stops ticking.

This makes drives and affordances the same shape — both per-tick, both keyed to a target — and they meet in the marketplace: an afforded verb says CAN-do-X-to-T; a drive-instance says WANT-relative-to-T; join them on the shared target and you get a scored ad. Selection is the join's MAX. The old CAN/DO split collapses into one operation, and V scores (pole-urgency × target-affordance) pairs — nothing stored but poles and memory.

Two clocks of "created": instances minted per-tick from poles + targets (fast); new poles minted by learning (slow — addiction, specific bonds). Same word, different mechanism.


Emotion — residual, controller, shortcut
Emotion is not a separate registry. It is the drive system seen from one angle: the residual between a drive's two poles. And it does three things that are really one thing.

        residual  ──►  EMOTION  ──►  acts to close the gap

        (arises)         │              (fills)

                         ▼

                  what selection reads to act NOW

                        (shortcut)

Arises from the gap. A pole with no resolution — arousal with no target, a target you can't reach — surfaces as a feeling. (Real name: interoceptive inference / prediction error — the felt mismatch.)
Fills the gap. The same signal writes back to complete the missing pole: fear completes low-clarity percepts as threat (manufacturing a target); the inert encoding tag supplies a missing charge from memory. A signal that both reads and reduces its own error is a controller — read-signal and control-signal are one. (Real name: active inference — act to minimize prediction error.)
Is a shortcut for "what to do now." The residual collapses the whole drive state into a low-dimensional action-pull that prunes the marketplace so the obvious verb wins without a full V sweep. (Real name: Damasio's somatic marker — a cached gut-feeling that prunes the decision space. The evidence is the paralysis case: damage it and a patient can still deliberate perfectly but can't decide.)

This places emotion as the middle rung of three "what to do now" mechanisms, by cost:

Reflex      stimulus → fixed action, no eval          instant, dumb       (bunny doc row 0)

Emotion     residual → prune / weight the marketplace  fast, heuristic     (this rung)

ODD plan    full V over all ads, traverse the DAG      slow, near-optimal

The shortcut is a cache of V — fast because approximate; its errors are exactly the survival-favouring false positives the prey interpretation already accepts.

The loop doesn't break determinism. It looks cyclic but it's pipelined across ticks like the rest of the agent loop: N residual→affect, N+1 affect biases interpretation + prunes marketplace, N+2 the biased read changes the residual. No intra-tick cycle.

It can spiral. A controller that reads and writes its own error can go positive-feedback: fear completes ambiguity as threat → more threat → more fear. That's panic / rumination, and the model predicting it is a feature. It needs damping — decay, the satisfaction-model, hysteresis — and the clean exit is the real one: acting discharges the residual and breaks the loop. When action is blocked (no escape, no target found) is exactly when real animals spiral too.

Proto p1 separated two regimes the word "spiral" conflates, and found they're governed by two independent things. **Maintenance** (the common case): a real threat seeds high fear, and a stream of ambiguous percepts is then completed-as-threat often enough to hold the fear up — a stable mood that only lifts when the agent acts to end its exposure (flee → no percept → reset). **Runaway**: fear igniting from rest with no real trigger.

*Whether fear ignites* is a personality fact. The completion bias is driven by **baseline arousal — the vigilance floor read as a trait**, which rests above zero for a timid agent, so an ambiguous percept can cross the threat threshold *from rest*. There's an analytic ignition floor: a rustle of clarity `c` self-ignites iff `c + (1−c)·gain·floor > threshold`. Below it (a bold bunny) the same spooky environment is ignored; above it (a timid one) fear bootstraps. So **trait anxiety (the floor) sets the ignition threshold; the felt fear (level above the floor) is the state** — personality colors perception even when nothing is felt, the bunny doc's "a timid bunny assigns aversive valence to ambiguous signals by default," now with a mechanism and a bifurcation point.

*Whether ignition then sustains to clamp* is a separate axis: escape-affordability. A timid bunny that is **fed** ignites, but Flee outscores the food ads, so it flees → breaks exposure → resets, and the result is a sawtooth, not panic. The **same** timid bunny **starving** can't afford to flee (the food-seeking drive outscores escape, the desperation edge having halved safety's urgency) — so it stays exposed and the fear pins at the clamp. Phantom-predator panic is therefore not a pure personality outcome: it's a timid animal *trapped* by a competing need. This is the same lesson as flee-blocked maintenance above, arising here from drive competition rather than fiat — and it's why the runaway case is rare. Caveat carried over: trait/state and these thresholds are personality parameters to tune, not fixed constants.


Mood vs emotion — and "depth"
A drive that mints, discharges, and dissolves in a few ticks produces a transient emotion (a startle that passes). A drive whose residual persists — because it can't be discharged quickly — produces a lingering mood. So depth ("one drive is easier to fulfill than another") sets instance lifetime, and lifetime sets how long the feeling lasts.

"Depth" decomposes into two axes — don't collapse them:

Axis
Meaning
Lives in
plan depth
the discharge needs a long chain of actions
the ODD DAG
search depth
discharge is simple but the target is scarce/absent
world state (is CAN empty while DO is high?)


leaf        drink: lap×n                      shallow — one action, residual gone

chain       territory: mark → patrol → repel  deep — long DAG before relief

bottomless  curiosity                         no discharge node; never "filled"

Plan depth = subtree depth in the planner. Hunger discharges shallow; territory is deep.
Search depth = target scarcity. Lonely with no one near is a trivial discharge with no target — the arousal-without-object quadrant, the one prone to runaway.
Bottomless drives have no discharge node (satisfaction-model = none). Curiosity is never "filled." These are the non-regulatory (non-homeostatic) motivations, and they're why some moods never fully lift. Safety is not one of them — it discharges (reset-on-percept) the moment the threat percept is gone; what never zeroes is its vigilance floor, a parameter, not a missing discharge node.

Caveat: "depth" is partly our own construct. Regulatory vs non-regulatory is a real distinction; the plan-depth / search-depth split is the ODD framework talking, not a named neuroscience result.


Acquired drives & addiction
Drives aren't only an innate roster — the learning loop can mint new poles. The same machinery that consolidates episodes into facts can install a new standing drive. The benign case is a specific bond (attachment to one mate, one place) or a taste preference. The pathological case is addiction, and it spans both poles at once because the loop can write both:

a real physical pole — tolerance/dependence; a withdrawal deficit that ticks like hunger (the body genuinely adapts);
a mental field — cue-driven craving; the substance-signature and its contexts get relevance-hijacked, so a cue spikes the drive.

The exact neuroscience names this: Berridge & Robinson's incentive-salience account of addiction as sensitized "wanting" decoupled from "liking" — the craving (wanting) grows while the pleasure (liking) does not. That is precisely "a hijacked drive": the directed pull intensifies independent of any real reward or deficit. Addiction being expressible with no new mechanism — just the learning loop fed an exploitative input — is the proof there is one drive system, not two.


How it's built (link to the engine)
In the World doc's terms: the persistent poles are the Drives registry (a level per pole, ticked by Metabolism); the per-tick instances are scratch, minted in the Interpretation → Planner stages and joined with affordances in the Marketplace; emotion is a derived per-tick read over the poles' residuals — Affects becomes a register, not stored state, exactly like SubjectiveView. A drive's authored content is four fields, frozen in a DriveDefs static table beside Ads and Reactions:

drive = ( score-field , urgency-projection ∈ {MAX, SUM} , satisfaction-model ∈ {deplete | reset-on-percept | none} , gate-edges )

The operators it's built from recur from everywhere else in the system — leaf-tick · percept-read · memory-read · MAX · SUM · PRODUCT — which is the signal the abstraction sits at the right level: it keeps minting its new parts from its old parts.


Grounding & a caveat
The research programs this synthesis draws on: homeostasis/allostasis (Cannon, Sterling, Barrett), interoception (Craig, Seth, Barrett), drive theory (Hull), incentive-motivation (Toates, Bindra), incentive salience / wanting–liking (Berridge & Robinson), needs hierarchy (Maslow), core affect / constructed emotion (Russell, Barrett), somatic marker (Damasio), predictive processing / active inference / interoceptive inference (Friston, Clark, Hohwy, Seth), affect heuristic / affect-as-information (Slovic, Clore & Schwarz), dual-process & model-free/model-based control (Kahneman, Daw & Dayan), temperament (Cloninger, Big Five).

The convergence — that a framework reasoned out from the bunny lands on terms a dozen separate research programs already coined — is a good sign the abstraction is sound rather than arbitrary, the same way the bunny doc's subjective layer landed on Barrett independently. But these attributions are from memory; before any of this goes into something formal, verify the exact terms, originators, and dates against primary sources. The ideas are solid; the citations are owed.

