namespace DaggerfallWorkshop.Sim
{
    /// Two friends crossed paths on the street (PerceptionSystem). OddSystem
    /// interrupts both into a short Chat; PerceptionSystem also emits the
    /// regard impulses. Atoms vocabulary: rung 2 — a percept short-circuiting
    /// deliberation.
    public sealed class GreetingEvent : ISimEvent
    {
        public EntityId A;
        public EntityId B;
    }

    /// Someone the entity resents is uncomfortably close. NeedsSystem turns
    /// it into social discomfort — the first directed-emotion effect.
    public sealed class DislikeNearbyEvent : ISimEvent
    {
        public EntityId Who;
        public EntityId Whom;
    }

    /// RequestSystem decided the asker should go ask Target in person.
    /// OddSystem puts the asker on the road (SeekHelp); the ask resolves when
    /// they arrive within speaking distance.
    public sealed class AskJourneyEvent : ISimEvent
    {
        public EntityId Asker;
        public EntityId Target;
        public float TargetX, TargetZ;
    }

    /// A festival day began (HolidaySystem, from the classic holiday tables).
    public sealed class HolidayBeganEvent : ISimEvent
    {
        public int HolidayId;
    }
}
