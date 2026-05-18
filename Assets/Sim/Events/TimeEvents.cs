namespace DaggerfallWorkshop.Sim
{
    /// Fires every sim tick. Used by anything that wants per-tick callbacks
    /// instead of implementing ISystem.Update directly.
    public sealed class TimeTickedEvent : ISimEvent
    {
        public long Tick;
        public double SimSeconds;
    }

    public sealed class NewHourSimEvent  : ISimEvent { public int Hour; public int Day; public int Month; public int Year; }
    public sealed class NewDaySimEvent   : ISimEvent { public int Day; public int Month; public int Year; }
    public sealed class NewMonthSimEvent : ISimEvent { public int Month; public int Year; }
    public sealed class NewYearSimEvent  : ISimEvent { public int Year; }

    /// Main → sim input. Sent once by WorldClockMirror to hand DFU's initial
    /// DaggerfallDateTime values + TimeScale to the sim, which then becomes the
    /// authoritative writer of the world clock.
    public sealed class SeedClockInput : ISimEvent
    {
        public int Year;
        public int Month;
        public int Day;
        public int Hour;
        public int Minute;
        public float Second;
        public float TimeScale;
    }

    public sealed class DawnSimEvent          : ISimEvent {}
    public sealed class DuskSimEvent          : ISimEvent {}
    public sealed class MiddaySimEvent        : ISimEvent {}
    public sealed class MidnightSimEvent      : ISimEvent {}
    public sealed class CityLightsOnSimEvent  : ISimEvent {}
    public sealed class CityLightsOffSimEvent : ISimEvent {}
}
