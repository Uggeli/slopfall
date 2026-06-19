namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of DaggerfallWorkshop.Sim.HolidayRegistry. Single global int id;
    // 0 = ordinary day. Systems Publish a HolidaySetIntent; this registry applies it
    // last-wins. Read API (CurrentId) preserved. The Holidays table (GetHolidayId)
    // and HolidayBeganEvent stay where they are — only the store moves here.

    /// Intent: "today's festival is Id (0 = none)." Emitted by HolidaySystem;
    /// applied here last-wins.
    public struct HolidaySetIntent : IEvent { public int Id; }

    public sealed class HolidayRegistry : Registry
    {
        int _currentId;

        /// Today's festival, or 0 for an ordinary day (read phase only).
        public int CurrentId => _currentId;

        public HolidayRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            var intents = Events.GetEvents<HolidaySetIntent>();
            if (intents.Length == 0) return;
            _currentId = intents[intents.Length - 1].Id;   // last write wins
        }
    }
}
