namespace DaggerfallWorkshop.Sim
{
    /// Observes WeatherRegistry and emits a sim-native event when the
    /// WeatherKind changes. Phase 3c scope: pure observation — WeatherMirror
    /// on the main thread is still the writer. Once consumers migrate, a later
    /// commit can flip the bridge and have a real weather scheduler write here.
    public sealed class WeatherSystem : ISystem
    {
        SimulationContext _ctx;
        WeatherKind _lastKind;
        bool _seeded;

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
        }

        public void ProcessEvents() { }

        public void Update(long tick)
        {
            var w = _ctx.Weather.Current;
            if (!_seeded)
            {
                _lastKind = w.Kind;
                _seeded = true;
                return;
            }
            if (w.Kind != _lastKind)
            {
                var prev = _lastKind;
                _lastKind = w.Kind;
                _ctx.Events.Emit(new WeatherChangedSimEvent { From = prev, To = w.Kind });
            }
        }
    }
}
