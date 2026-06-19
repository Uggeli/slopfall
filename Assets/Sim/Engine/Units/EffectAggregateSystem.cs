namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the old DaggerfallWorkshop.Sim.EffectAggregateSystem.
    // Reads the Engine EffectsRegistry read-only, derives per-entity modifier sums,
    // and emits EffectAggregateSetIntent (the EffectAggregateRegistry is the sole
    // applier). Reuses EffectAggregateData from the parent namespace unqualified.
    //
    // Clear-on-empty: the registry no longer exposes Remove, so when an entity has no
    // active effects we emit an EMPTY-value EffectAggregateSetIntent (a fresh
    // EffectAggregateData with no modifiers) instead of the old Remove call.

    /// Recomputes per-entity stat modifier sums from EffectsRegistry each tick and
    /// emits them as whole-value set intents. Modifier key convention: "<EffectType>"
    /// — magnitude is added; drain effects use negative magnitudes. Rebuilds every
    /// tick (Phase 3f scope — dirty-set optimization can come later).
    public sealed class EffectAggregateSystem : SimSystem
    {
        readonly EffectsRegistry _effects;

        public EffectAggregateSystem(EventBus events, EffectsRegistry effects) : base(events)
        {
            _effects = effects;
        }

        public override void Update(long tick)
        {
            foreach (var kv in _effects.All)
            {
                var data = kv.Value;
                if (data == null || data.Active == null || data.Active.Count == 0)
                {
                    // Clear-on-empty: emit an empty aggregate (no Remove available).
                    Events.Publish(new EffectAggregateSetIntent
                    {
                        Id   = kv.Key,
                        Data = new EffectAggregateData(),
                    });
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
                Events.Publish(new EffectAggregateSetIntent { Id = kv.Key, Data = agg });
            }
        }
    }
}
