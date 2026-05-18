namespace DaggerfallWorkshop.Sim
{
    /// Recomputes per-entity stat modifier sums from EffectsRegistry each tick
    /// and writes them to EffectAggregateRegistry. Modifier key convention:
    ///   "<EffectType>" — e.g. "Strength" for fortify-strength, "Speed" for
    ///   fortify-speed; magnitude is added. Drain effects are expected to use
    ///   negative magnitudes (or a "Drain<X>" key — convention TBD as real
    ///   effects port over).
    ///
    /// Phase 3f scope: rebuilds the aggregate every tick. Optimizing to a
    /// dirty-set rebuild can come later if profiling shows it matters.
    public sealed class EffectAggregateSystem : ISystem
    {
        SimulationContext _ctx;

        public void Init(SimulationContext ctx) { _ctx = ctx; }
        public void ProcessEvents() { }

        public void Update(long tick)
        {
            foreach (var kv in _ctx.Effects.All)
            {
                var data = kv.Value;
                if (data == null || data.Active == null || data.Active.Count == 0)
                {
                    _ctx.EffectAggregate.Remove(kv.Key);
                    continue;
                }

                var agg = new EffectAggregateData();
                for (int i = 0; i < data.Active.Count; i++)
                {
                    var fx = data.Active[i];
                    if (fx.RemainingTicks <= 0) continue;
                    if (string.IsNullOrEmpty(fx.Key)) continue;
                    if (agg.Modifiers.TryGetValue(fx.Key, out var sum))
                        agg.Modifiers[fx.Key] = sum + fx.Magnitude;
                    else
                        agg.Modifiers[fx.Key] = fx.Magnitude;
                }
                _ctx.EffectAggregate.Set(kv.Key, agg);
            }
        }
    }
}
