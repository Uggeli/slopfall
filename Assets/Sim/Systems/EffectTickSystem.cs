using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Walks all active effects each tick, fires EffectTickedEvent for
    /// per-tick effects, and decrements RemainingTicks via whole-EffectsData
    /// replacement (same discipline as EffectLifecycleSystem). Emits
    /// EffectExpiredEvent for any effect that just hit zero so Lifecycle
    /// removes it next tick.
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
                if (data == null || data.Active == null || data.Active.Count == 0) continue;

                var next = new EffectsData();
                bool changed = false;
                for (int i = 0; i < data.Active.Count; i++)
                {
                    var fx = data.Active[i];
                    if (fx.RemainingTicks <= 0)
                    {
                        next.Active.Add(fx);
                        continue;
                    }

                    if (fx.AppliesPerTick)
                    {
                        _ctx.Events.Emit(new EffectTickedEvent
                        {
                            Target    = kv.Key,
                            Source    = fx.Source,
                            Key       = fx.Key,
                            Magnitude = fx.Magnitude,
                        });
                    }

                    long remaining = fx.RemainingTicks - 1;
                    next.Active.Add(new EffectInstance
                    {
                        Key            = fx.Key,
                        Magnitude      = fx.Magnitude,
                        RemainingTicks = remaining,
                        Source         = fx.Source,
                        AppliesPerTick = fx.AppliesPerTick,
                    });
                    changed = true;

                    if (remaining == 0)
                    {
                        _ctx.Events.Emit(new EffectExpiredEvent
                        {
                            Target = kv.Key,
                            Key    = fx.Key,
                        });
                    }
                }
                if (changed) _ctx.Effects.Set(kv.Key, next);
            }
        }
    }
}
