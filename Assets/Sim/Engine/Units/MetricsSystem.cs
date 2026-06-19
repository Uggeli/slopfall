namespace DaggerfallWorkshop.Sim.Engine
{
    /// Read-only diagnostic: tallies creature kills of civilians, classified by
    /// where (at a gate cell vs inside the walls) and when (day vs night). Publishes
    /// NOTHING — it must run AFTER HealthSystem (which emits DeathEvent) and never
    /// mutates settled state, so it doesn't affect the determinism fingerprint.
    public sealed class MetricsSystem : SimSystem
    {
        public long KillsAtGate, KillsInside, KillsByDay, KillsByNight;

        readonly WorldClockRegistry _clock;
        readonly CreatureRegistry _creatures;
        readonly PositionRegistry _position;
        readonly TownGridRegistry _townGrid;

        public MetricsSystem(EventBus events, WorldClockRegistry clock, CreatureRegistry creatures,
            PositionRegistry position, TownGridRegistry townGrid) : base(events)
        {
            _clock = clock; _creatures = creatures; _position = position; _townGrid = townGrid;
        }

        public override void Update(long tick)
        {
            var deaths = Events.GetEvents<DeathEvent>();
            if (deaths.Length == 0) return;
            var clock = _clock.Current;
            bool night = clock.Hour < 6 || clock.Hour >= 18;
            var grid = _townGrid.Current;
            for (int i = 0; i < deaths.Length; i++)
            {
                var d = deaths[i];
                if (!_creatures.Contains(d.Killer)) continue;   // only creature kills of civilians
                if (night) KillsByNight++; else KillsByDay++;
                bool atGate = false;
                if (grid != null && _position.TryGet(d.Entity, out var vp) && vp != null)
                    atGate = grid.IsGateCell(grid.CellX(vp.X), grid.CellY(vp.Z));
                if (atGate) KillsAtGate++; else KillsInside++;
            }
        }
    }
}
