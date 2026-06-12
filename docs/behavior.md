  1. Where the wedge stands against Atoms — honest scorecard

  Already genuinely Atoms-shaped: deplete-model deficiency poles (Needs), one growth drive (Visit — engagement-as-reward, no setpoint), graded+hard-cull prepotency (no hysteresis
  yet), traits scaling weights, rung 3 deliberation (argmax marketplace), a real rung 2 (perception interrupts), Object-Zero liveness, episodic memory ring, and — importantly — the
  event/single-writer discipline that makes all the rest retrofittable.

  Present but only as a shadow of the model:
  - Drive instances vs poles — Atoms says instances are minted per-tick from pole × perceived target. Our "instances" exist implicitly as the hardcoded candidate blocks in Decide().
  Works, doesn't scale.
  - The marketplace join (CAN × WANT) — Atoms joins afforded verbs with drive instances on a shared target. We have no affordances: OddSystem knows taverns serve food, in code. Every
  new action = another block in the god-function.
  - Emotion as residual/controller — we read needs directly; nothing is felt, nothing completes ambiguous percepts, nothing biases.

  Absent entirely: directed hot emotions (Anger(X), Fear(X) — Regard is the slow MEANINGS-like valence, not the residual), reset-on-percept satisfaction (no threats exist), the ODD
  tree and planner (no actions chain), memory consolidation (sleep is just energy; episodes never become facts), encode gates, conscience (no transgressions exist to be ashamed of),
  Things/items, reflex rung.

  The pattern in the absences is important: most of the missing Atoms machinery lacks test material in the town, not implementation difficulty. Conscience needs witnessed punishment —
  there's no crime. Fear needs threats — there are no predators. Reset-on-percept needs threat percepts. The ODD tree needs chainable actions — there are no items. Atoms' own proto
  discipline (p1–p7: pin structure, test dynamics piece by piece) argues against installing machinery before the world can exercise it.

  2. Missing actions — inventory, organized by which loop they close

  The high-value actions aren't a flat list; they cluster into loops, and each loop happens to create the test material one Atoms layer needs. That's the deep alignment worth
  exploiting.

  The economy loop (biggest gap, most mundane): residents have no income and nothing to buy. Missing: resident jobs (laborer, deliveries between shops, washerwoman), shopping (buy
  food stock for a home larder; EatHome consumes the larder, empty larder forces shopping or begging), maybe begging as a distinct public activity. Closing this kills the wage faucet
  (keepers earn from real customers), makes poverty earned rather than seeded, and gives the streets daytime purpose. Creates test material for: Things/items (the larder is the first
  possession), and the planner (buy → carry home → cook is the first natural action chain).

  The conflict loop: relations can sour but nothing ever happens. Missing: insult/argue (proximity + mutual dislike + low warmth → confrontation), tavern brawl (escalation), avoidance
  steering (path around people you hate — we have the pathfinder for it). Creates test material for: directed hot emotions — Anger(X) as a fast-decaying residual distinct from slow
  Regard, MAX-projection, discharge-by-confrontation.

  The norm loop (the most interesting one): theft (desperate + cold → steal from larder/shop), witnessing, guards/punishment. This is literally the Atoms conscience installer — "a kit
  watches the warren punish a transgressor → consolidates into a self-referential aversive node." We'd be building the social machinery that grows consciences before building the
  conscience. Creates test material for: the entire conscience layer, in the right order.

  The quest loop: generalize RequestSystem from alms to paid tasks ("deliver this to X for 0.1 coin", "fetch me food") — request + acceptance + execution + payment + gratitude. This
  is the thesis mechanism graduating from charity to employment, and it's exactly what the player plugs into. Creates test material for: multi-step plans (a hired task IS a plan).

  The social-depth loop: visit a friend at home (relations drive where you go), invite (coordinated activities), console (respond to a friend's distress). Cheap, uses existing
  machinery, makes friendships visible as behavior rather than numbers.

  Lifecycle/world texture (later, cheap, atmospheric): temple worship as a distinct piety drive, festival gatherings at the market square (holidays currently just close shops),
  illness + temple healing, visitors from other towns (news carriers — pairs with multi-town).

  3. The recommendation: not full modeling — staged convergence with one architectural commitment now

  Full Atoms modeling in one push would be months of machinery that the town can't yet exercise, validated against nothing. But "keep bolting blocks onto Decide()" hits the wall
  immediately. The resolution:

  Stage A — adopt Atoms' data shapes now (the marketplace refactor). This is the one piece I'd do before any new actions:
  - DriveDefs table: per-axis {default weight, satisfaction-model (deplete/reset-on-percept/none), projection (MAX/SUM), gate-edges} — formalizes what's currently implicit constants
  scattered across three systems. Atoms says drives are four authored fields; make it literally true.
  - Affordances as data: buildings and persons advertise offers (Tavern → {EatTavern, Socialize}; Home → {Sleep, EatHome, Shelter}; Person → {Chat, Ask}). Decide() becomes one loop:
  gather advertised offers near/known to the agent, join with drive urgencies, score uniformly, argmax. The hardcoded blocks die.
  - Keep single-step argmax. No tree, no symbolic state, no FlagMask perf machinery — town scale needs clarity, not 40k decisions/sec.
  - Behavior-preserving refactor, verified by the existing 119 tests — that's what they're for.

  After Stage A, every action in section 2 is a table row plus a small effect handler, not surgery on a god-function.

  Then the staged adoptions, each gated on its loop existing:

  ┌───────┬─────────────────────────────────────────────────────────────────────────────────┬────────────────────────────────────────┬────────────────────────────────────────────┐
  │ Stage │                                   Atoms layer                                   │              Unlocked by               │                   Brings                   │
  ├───────┼─────────────────────────────────────────────────────────────────────────────────┼────────────────────────────────────────┼────────────────────────────────────────────┤
  │ B     │ Directed emotions (pole×target instances, EmotionRegistry keyed (axis, target), │ conflict loop (insult/argue)           │ hot Anger(X)/Fear(X) vs slow Regard;       │
  │       │  MAX/SUM projections, decay)                                                    │                                        │ discharge models                           │
  ├───────┼─────────────────────────────────────────────────────────────────────────────────┼────────────────────────────────────────┼────────────────────────────────────────────┤
  │ C     │ Memory upgrade: encode gates (stakes/surprise), sleep consolidation (episodes → │ quest loop + conflict (events worth    │ knowledge as a sim quantity → rumors →     │
  │       │  fact-like opinions), gossip shares facts                                       │ remembering)                           │ quest discovery                            │
  ├───────┼─────────────────────────────────────────────────────────────────────────────────┼────────────────────────────────────────┼────────────────────────────────────────────┤
  │ D     │ Conscience: norm seeds, guilt pole w/ reparation, taboo cull                    │ norm loop (theft/guards/witnessing)    │ the OUGHT layer, installed the way Atoms   │
  │       │                                                                                 │                                        │ says it's installed                        │
  ├───────┼─────────────────────────────────────────────────────────────────────────────────┼────────────────────────────────────────┼────────────────────────────────────────────┤
  │ E     │ ODD tree + planner, Things                                                      │ economy loop items + paid tasks        │ multi-step behavior; the full flag_tests   │
  │       │                                                                                 │                                        │ pipeline comes home                        │
  └───────┴─────────────────────────────────────────────────────────────────────────────────┴────────────────────────────────────────┴────────────────────────────────────────────┘

  Deliberately not adopting: fear ignition/vigilance floors/panic spirals (until wilderness/dungeons add real danger), the full per-object scene graph (buildings + persons are the
  only Things for a long time), perf-tuned FlagMask/Pred machinery, the sacred upward-cull edge (no martyrs needed in Gothway Garden — though the conscience shape should leave the
  slot).

  4. Two infrastructure debts this planning pass surfaced

  - Persistence. The sim now accumulates irreplaceable state — relations, memories, grudges, fortunes. Every process exit is an apocalypse. Save/load (Phase 6 of the old plan) is
  becoming due, and it should land before Stage C makes memory deeper. Snapshot-to-disk of the registries is straightforward with current shapes.
  - Multi-day stability is unverified. Everything is tuned and tested on one day. Before deep modeling: a week-long soak run with assertions — does coin concentrate pathologically
  (richest was already 3.9× after one day), do friendships saturate, does the social fabric reach an equilibrium or a fixed point? Cheap to run headless, and it'll expose decay-shaped
  gaps (Regard never decays! Atoms would say: no satisfaction-model on the social valence).

  5. Proposed order

  1. Soak test (week-long run, stability assertions) — informs everything, might reveal urgent decay/equilibrium fixes like Regard decay.
  2. Stage A marketplace refactor — the architectural commitment, behavior-preserving.
  3. Economy loop actions (jobs, larder, shopping) on the new marketplace — first table-driven actions, kills the wage faucet.
  4. Persistence — before state gets deeper.
  5. Conflict loop + Stage B directed emotions together.
  6. Quest loop (paid tasks) → Stage C memory/gossip-as-knowledge.
  7. Norm loop → Stage D conscience.
  8. Player verbs can slot in any time after 2 — earlier is better for fun; the player is just another agent in the marketplace.

  The one-sentence answer to your question: don't do full Atoms modeling now — adopt its data shapes immediately (that refactor is overdue), then let each behavior loop pull in 
  exactly the Atoms layer it creates test material for, in the order above. That's also how Atoms itself was validated — proto by proto, dynamics after structure.

  Want me to write this up as docs/behavior_roadmap.md in the repo (planning artifact, not code), or adjust the staging first?
