namespace DaggerfallWorkshop.Sim
{
    /// The unified atomic-action vocabulary for ALL agents (player, civilian,
    /// creature). An action is one indivisible operation; activities (ActivityKind)
    /// are durative plans built from these. "live" actions have executors today;
    /// "reserved" are named so player/AI/item verbs drop in as catalog rows when
    /// their activities arrive (proto discipline — no executor before the world
    /// exercises it). See docs/action_catalog.md.
    public enum ActionKind
    {
        None,

        // --- live: an executor exists or maps directly to a current system ---
        MoveTo,         // relocate toward a point (MovementSystem)
        Sustain,        // be at the spot applying need-Δ over time (NeedsSystem Doing)
        Transfer,       // move coin: pay / buy / sell / give / earn (EconomySystem)
        Speak,          // greet / inform → RelationImpulse (Perception/SocialSystem)
        Ask,            // raise a request (RequestSystem)
        Answer,         // grant / refuse a request (RequestSystem)
        TrainSkill,     // accrue skill XP (SkillAdvancementSystem)

        // --- reserved: vocabulary only, no executor yet ---
        Attack, CastSpell, ApplyEffect,             // combat & magic
        Take, Drop, Equip, Use,                     // items
        Open, Close, Lockpick, Bash, Read,          // world objects
    }

    /// How activities decompose into atomic actions. Every activity is
    /// MoveTo(target) during the Moving phase, then these during Doing. Data the
    /// future ExecutionSystem runs; today it documents the division in code (and
    /// is pinned by a test). See docs/action_catalog.md.
    public static class ActionCatalog
    {
        static readonly ActionKind[] None = new ActionKind[0];
        static readonly ActionKind[] JustSustain = { ActionKind.Sustain };
        static readonly ActionKind[] LaborAndEarn = { ActionKind.Sustain, ActionKind.Transfer };
        static readonly ActionKind[] PayThenSustain = { ActionKind.Transfer, ActionKind.Sustain };
        static readonly ActionKind[] JustSpeak = { ActionKind.Speak };
        static readonly ActionKind[] JustAsk = { ActionKind.Ask };
        static readonly ActionKind[] TakeThenSustain = { ActionKind.Take, ActionKind.Sustain };   // steal: grab the goods (Take), then the relief accrues
        static readonly ActionKind[] JustUse = { ActionKind.Use };       // use a held item per its affordance (eat/drink/read)
        static readonly ActionKind[] JustDrop = { ActionKind.Drop };     // set a held item down (ground, or into a building = store)

        /// The Doing-phase actions an activity performs (MoveTo is implied by the
        /// Moving phase and not listed).
        public static ActionKind[] DoingSteps(ActivityKind activity)
        {
            switch (activity)
            {
                case ActivityKind.Idle:      return JustSustain;
                case ActivityKind.Wander:    return None;            // wander IS movement
                case ActivityKind.Sleep:     return JustSustain;
                case ActivityKind.Work:      return LaborAndEarn;
                case ActivityKind.Labor:     return LaborAndEarn;
                case ActivityKind.Weave:     return LaborAndEarn;
                case ActivityKind.EatHome:   return JustSustain;
                case ActivityKind.EatTavern: return PayThenSustain;
                case ActivityKind.Socialize: return PayThenSustain;
                case ActivityKind.Visit:     return JustSustain;
                case ActivityKind.Buy:       return PayThenSustain;
                case ActivityKind.Steal:     return TakeThenSustain;
                case ActivityKind.UseItem:   return JustUse;
                case ActivityKind.StoreItem: return JustDrop;
                case ActivityKind.Flee:      return None;             // flee IS movement (like Wander)
                case ActivityKind.Attack:    return None;             // close the distance; CombatSystem strikes
                case ActivityKind.Chat:      return JustSpeak;
                case ActivityKind.SeekHelp:  return JustAsk;
                default:                     return None;
            }
        }

        // The chain edges (docs/odd_spec.md "Enables"): doing the key activity makes
        // the listed activities' value reachable, so the planner (OddTree) folds their
        // discounted worth back onto it — "work is worth the meal it eventually buys."
        // Generic action-TYPE edges, NOT authored per-agent plans: the chains
        // (earn→buy→eat) emerge per agent from traversal of who-can-reach-what.
        //
        // v1 = the money loop only. Earning (Work/Labor) enables the paid, stock-gated
        // consumption (Buy, EatTavern) a broke agent otherwise can't reach now, so a
        // hungry-and-broke agent values the work that funds the meal. Everything else
        // is terminal: Farm/Fish feed in-kind (no coin step); Idle/Sleep are their own
        // reward. Adding a link is a row here — no decider code changes.
        static readonly ActivityKind[] PaidConsumption = { ActivityKind.Buy, ActivityKind.EatTavern };
        static readonly ActivityKind[] HomeMeal = { ActivityKind.EatHome };
        static readonly ActivityKind[] NoChain = new ActivityKind[0];

        public static ActivityKind[] Enables(ActivityKind activity)
        {
            switch (activity)
            {
                case ActivityKind.Work:
                case ActivityKind.Labor:
                case ActivityKind.Weave:
                case ActivityKind.Beg:
                    return PaidConsumption;   // earning — wages OR alms — → the paid meal (EatTavern) or groceries (Buy) it funds. Weave clothes you in-kind (Attire Δ), but cloth isn't food, so it ALSO heads this food chain: a weaver eats by wage→buy→eat. Begging is instrumental too now that CoinDef is retired.
                case ActivityKind.Buy:
                    return HomeMeal;          // groceries → the home meal: the HUNGER payoff that motivates the work→buy→eat chain
                // NOT Steal: theft is a desperate REACTION (it relieves GoodsDef when the
                // conscience permits), not a strategy the planner pursues for its eating
                // payoff. Chaining Steal→EatHome made free stolen food out-compete honest
                // Labor (the town turned thief, the looms went idle); real theft
                // consequences (witnesses, punishment) are a later feature.
                default:
                    return NoChain;
            }
        }
    }
}
