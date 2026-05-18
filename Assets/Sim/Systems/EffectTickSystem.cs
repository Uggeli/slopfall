namespace DaggerfallWorkshop.Sim
{
    /// Walks all active effects each tick, decrements RemainingTicks, fires
    /// EffectTickedEvent for per-tick effects and EffectExpiredEvent for ones
    /// that just hit zero. Does not mutate EffectsRegistry directly — expiry
    /// removal is handled by EffectLifecycleSystem in response to
    /// EffectExpiredEvent.
    ///
    /// Note: RemainingTicks is mutated in-place here. Safe because:
    ///  - both this system and EffectLifecycleSystem run on the sim thread
    ///  - readers on the main thread receive whole-EffectsData snapshots via
    ///    Set, and we don't swap mid-walk
    public sealed class EffectTickSystem : ISystem
    {
        SimulationContext _ctx;

        public void Init(SimulationContext ctx) { _ctx = ctx; }
        public void ProcessEvents() { }

        public void Update(long tick)
        {
            foreach (var kv in _ctx.Effects.All)
            {
                var data = kv.Value;
                if (data == null || data.Active == null) continue;
                for (int i = 0; i < data.Active.Count; i++)
                {
                    var fx = data.Active[i];
                    if (fx.RemainingTicks <= 0) continue;

                    if (fx.AppliesPerTick)
                    {
                        _ctx.Events.Emit(new EffectTickedEvent
                        {
                            Target = kv.Key,
                            Source = fx.Source,
                            Key = fx.Key,
                            Magnitude = fx.Magnitude,
                        });
                    }

                    fx.RemainingTicks--;
                    if (fx.RemainingTicks == 0)
                    {
                        _ctx.Events.Emit(new EffectExpiredEvent
                        {
                            Target = kv.Key,
                            Key = fx.Key,
                        });
                    }
                }
            }
        }
    }
}
