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
                case ActivityKind.EatHome:   return JustSustain;
                case ActivityKind.EatTavern: return PayThenSustain;
                case ActivityKind.Socialize: return PayThenSustain;
                case ActivityKind.Visit:     return JustSustain;
                case ActivityKind.Buy:       return PayThenSustain;
                case ActivityKind.Chat:      return JustSpeak;
                case ActivityKind.SeekHelp:  return JustAsk;
                default:                     return None;
            }
        }
    }
}
