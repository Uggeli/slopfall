namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of DaggerfallWorkshop.Sim.HolidaySystem. The day-gate cadence
    // (the old _lastDayChecked) is replaced by reacting to NewDayEvent: the system only
    // recomputes when a new day actually begins. Reads TownGrid (region index) +
    // HolidayRegistry (to detect a change); emits HolidaySetIntent, plus HolidayBeganEvent
    // when a NEW festival starts (id changed and id > 0) — exactly as the original did.
    //
    // (Reuses the Holidays table / GetHolidayId and HolidayBeganEvent from the parent
    // DaggerfallWorkshop.Sim / Engine namespaces. dayOfYear math is preserved verbatim;
    // Day/Month from NewDayEvent are 0-based like the WorldClock the old code read.)
    public sealed class HolidaySystem : SimSystem
    {
        readonly HolidayRegistry _holiday;
        readonly TownGridRegistry _townGrid;

        public HolidaySystem(EventBus events, HolidayRegistry holiday, TownGridRegistry townGrid)
            : base(events)
        {
            _holiday = holiday;
            _townGrid = townGrid;
        }

        public override void Update(long tick)
        {
            var days = Events.GetEvents<NewDayEvent>();
            if (days.Length == 0) return;                  // only on a new day
            var day = days[days.Length - 1];               // last day of this tick's span

            int dayOfYear = day.Month * 30 + day.Day + 1;

            var grid = _townGrid.Current;
            int region = grid != null ? grid.RegionIndex : 17;   // Daggerfall by default

            int id = Holidays.GetHolidayId(dayOfYear, region);
            if (id != _holiday.CurrentId && id > 0)
                Events.Publish(new HolidayBeganEvent { HolidayId = id });
            Events.Publish(new HolidaySetIntent { Id = id });
        }
    }
}
