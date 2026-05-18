using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Subscribes to every event type we care to log and keeps a bounded ring
    /// of recent entries for the SimInspector to display. Useful for "is the
    /// event bus actually wired up correctly?" verification.
    ///
    /// Lock-based concurrency: sim thread writes via Add (called from event
    /// handlers during EventBus.Drain); Unity main thread reads via Snapshot().
    public sealed class EventLog : ISystem
    {
        public sealed class Entry
        {
            public long Tick;
            public double SimSeconds;
            public string Summary;
        }

        const int MaxEntries = 128;
        readonly LinkedList<Entry> _entries = new LinkedList<Entry>();
        readonly object _lock = new object();
        SimulationContext _ctx;

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
            ctx.Events.Subscribe<NewHourSimEvent>(e => Add("NewHour " + e.Hour.ToString("00") + ":00  " + e.Year + "-" + e.Month + "-" + e.Day));
            ctx.Events.Subscribe<NewDaySimEvent>(e => Add("NewDay " + e.Year + "-" + e.Month + "-" + e.Day));
            ctx.Events.Subscribe<NewMonthSimEvent>(e => Add("NewMonth " + e.Month + "/" + e.Year));
            ctx.Events.Subscribe<NewYearSimEvent>(e => Add("NewYear " + e.Year));
            ctx.Events.Subscribe<DawnSimEvent>(e => Add("Dawn"));
            ctx.Events.Subscribe<DuskSimEvent>(e => Add("Dusk"));
            ctx.Events.Subscribe<MiddaySimEvent>(e => Add("Midday"));
            ctx.Events.Subscribe<MidnightSimEvent>(e => Add("Midnight"));
            ctx.Events.Subscribe<CityLightsOnSimEvent>(e => Add("CityLightsOn"));
            ctx.Events.Subscribe<CityLightsOffSimEvent>(e => Add("CityLightsOff"));
            ctx.Events.Subscribe<WeatherChangedSimEvent>(e => Add("Weather " + e.From + " → " + e.To));
            ctx.Events.Subscribe<DeathSimEvent>(e => Add("Death entity=" + e.Entity.Value + " killer=" + e.Killer.Value + " type=" + e.FatalDamageType));
        }

        public void ProcessEvents() { /* receives via Subscribe */ }
        public void Update(long tick) { /* passive listener */ }

        /// Returns a copy of the recent entries, newest first.
        public List<Entry> Snapshot()
        {
            lock (_lock)
            {
                var list = new List<Entry>(_entries.Count);
                for (var n = _entries.First; n != null; n = n.Next) list.Add(n.Value);
                return list;
            }
        }

        void Add(string summary)
        {
            var entry = new Entry { Tick = _ctx.Time.Tick, SimSeconds = _ctx.Time.Elapsed, Summary = summary };
            lock (_lock)
            {
                _entries.AddFirst(entry);
                while (_entries.Count > MaxEntries) _entries.RemoveLast();
            }
        }
    }
}
