namespace DaggerfallWorkshop.Sim.Engine
{
    // GOLD TEMPLATE for a converted unit. Shows the four moves every conversion makes:
    //   1. Registry owns data + applies its intent event in Update() (sole writer).
    //   2. System reads (registries read-only) and Publishes intents — holds no tick state.
    //   3. Cadence comes from reacting to a time event (NewHourEvent), not a _hourPending flag.
    //   4. RNG is a STATELESS hash of (date, seed) — no shared SimRandom, parallel-safe.
    // (Reuses the existing WeatherData / WeatherKind from the DaggerfallWorkshop.Sim namespace.)

    /// Intent: "set the weather to Kind." Anyone may emit it; WeatherRegistry is the
    /// sole applier. Derived flags (IsRaining/…) are computed on apply.
    public struct WeatherSetIntent : IEvent { public WeatherKind Kind; }

    public sealed class WeatherRegistry : Registry
    {
        WeatherData _data = new WeatherData { Kind = WeatherKind.Sunny };

        /// Read view (read phase only — no concurrent writer under phase separation).
        public WeatherData Current => _data;

        public WeatherRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            var intents = Events.GetEvents<WeatherSetIntent>();
            if (intents.Length == 0) return;
            var kind = intents[intents.Length - 1].Kind;     // last write wins
            _data = new WeatherData
            {
                Kind = kind,
                IsRaining = kind == WeatherKind.Rain || kind == WeatherKind.Thunder,
                IsStorming = kind == WeatherKind.Thunder,
                IsOvercast = kind == WeatherKind.Overcast || kind == WeatherKind.Fog,
            };
        }
    }

    /// Headless weather authority: a Markov chain over the severity ladder, stepped
    /// once per game hour. Reads current weather + the hour tick; emits a set-intent.
    public sealed class WeatherSystem : SimSystem
    {
        readonly WeatherRegistry _weather;
        readonly int _seed;

        public WeatherSystem(EventBus events, WeatherRegistry weather, int seed) : base(events)
        {
            _weather = weather;
            _seed = seed;
        }

        public override void Update(long tick)
        {
            var hours = Events.GetEvents<NewHourEvent>();
            if (hours.Length == 0) return;                   // step only on the hour
            var h = hours[hours.Length - 1];

            var current = _weather.Current.Kind;
            int roll = Roll(_seed, h.Year, h.Month, h.Day, h.Hour);
            var next = Step(current, roll);
            if (next != current)
                Events.Publish(new WeatherSetIntent { Kind = next });
        }

        // --- stateless deterministic draw: same seed + same date → same roll (0..99) ---
        static int Roll(int seed, int year, int month, int day, int hour)
        {
            unchecked
            {
                uint x = (uint)seed * 2654435761u;
                x = (x ^ (uint)year) * 2654435761u;
                x = (x ^ (uint)month) * 2654435761u;
                x = (x ^ (uint)day) * 2654435761u;
                x = (x ^ (uint)hour) * 2654435761u;
                x ^= x >> 15;
                return (int)(x % 100u);
            }
        }

        // Markov step over Sunny⇄Cloudy⇄Overcast⇄Rain⇄Thunder (+Fog off Overcast),
        // sun-biased. roll is supplied (stateless) — identical math to the original.
        static WeatherKind Step(WeatherKind current, int roll)
        {
            if (current == WeatherKind.Fog)
                return roll < 50 ? WeatherKind.Overcast : WeatherKind.Fog;

            int rung = Rung(current);
            if (roll < 62) return current;
            if (roll < 70 && current == WeatherKind.Overcast) return WeatherKind.Fog;
            rung += roll < 78 ? 1 : -1;
            if (rung < 0) rung = 0; else if (rung > 4) rung = 4;
            return FromRung(rung);
        }

        static int Rung(WeatherKind k)
        {
            switch (k)
            {
                case WeatherKind.Sunny: return 0;
                case WeatherKind.Cloudy: return 1;
                case WeatherKind.Overcast: return 2;
                case WeatherKind.Rain: return 3;
                default: return 4;
            }
        }

        static WeatherKind FromRung(int rung)
        {
            switch (rung)
            {
                case 0: return WeatherKind.Sunny;
                case 1: return WeatherKind.Cloudy;
                case 2: return WeatherKind.Overcast;
                case 3: return WeatherKind.Rain;
                default: return WeatherKind.Thunder;
            }
        }
    }
}
