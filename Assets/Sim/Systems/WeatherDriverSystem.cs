namespace DaggerfallWorkshop.Sim
{
    /// Headless weather authority. Until now nothing drove weather outside
    /// Unity — towns lived under a frozen sun. A small Markov chain steps once
    /// per game hour over a severity ladder (Sunny ⇄ Cloudy ⇄ Overcast ⇄ Rain
    /// ⇄ Thunder, with Fog off Overcast), drawing from the seeded SimRandom so
    /// same-seed worlds get the same skies.
    ///
    /// Register only where the sim owns weather (headless hosts) — under
    /// Unity, WeatherMirror remains the writer.
    public sealed class WeatherDriverSystem : ISystem
    {
        SimulationContext _ctx;
        bool _hourPending;

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
            ctx.Events.Subscribe<NewHourSimEvent>(e => _hourPending = true);
        }

        public void ProcessEvents() { }

        public void Update(long tick)
        {
            if (!_hourPending) return;
            _hourPending = false;

            var current = _ctx.Weather.Current.Kind;
            var next = Step(current, _ctx.Random);
            if (next == current) return;

            _ctx.Weather.Set(new WeatherData
            {
                Kind = next,
                IsRaining = next == WeatherKind.Rain || next == WeatherKind.Thunder,
                IsStorming = next == WeatherKind.Thunder,
                IsOvercast = next == WeatherKind.Overcast || next == WeatherKind.Fog,
            });
        }

        static WeatherKind Step(WeatherKind current, SimRandom random)
        {
            int roll = random.NextInt(100);

            // Fog clears back through Overcast.
            if (current == WeatherKind.Fog)
                return roll < 50 ? WeatherKind.Overcast : WeatherKind.Fog;

            int rung = Rung(current);
            if (roll < 62) return current;                          // weather mostly lingers
            if (roll < 70 && current == WeatherKind.Overcast)
                return WeatherKind.Fog;                             // morning fog flavor
            bool worsen = roll < 78;                                // 16% worsen, 22% improve: sun-biased
            rung += worsen ? 1 : -1;
            if (rung < 0) rung = 0;
            if (rung > 4) rung = 4;
            return FromRung(rung);
        }

        static int Rung(WeatherKind kind)
        {
            switch (kind)
            {
                case WeatherKind.Sunny: return 0;
                case WeatherKind.Cloudy: return 1;
                case WeatherKind.Overcast: return 2;
                case WeatherKind.Rain: return 3;
                default: return 4;      // Thunder (Snow folds in with climates later)
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
