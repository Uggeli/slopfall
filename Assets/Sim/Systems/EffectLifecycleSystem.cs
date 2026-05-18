using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Sole writer of EffectsRegistry. Adds new effects on ApplyEffectEvent,
    /// removes on RemoveEffectEvent and EffectExpiredEvent.
    ///
    /// Instantaneous effects (DurationTicks == 0) are emitted as a single
    /// EffectAppliedEvent without ever being stored — consumers handle the
    /// one-shot apply (e.g. instant damage spells).
    public sealed class EffectLifecycleSystem : ISystem
    {
        SimulationContext _ctx;
        readonly List<ApplyEffectEvent>   _pendingApply  = new List<ApplyEffectEvent>();
        readonly List<RemoveEffectEvent>  _pendingRemove = new List<RemoveEffectEvent>();
        readonly List<EffectExpiredEvent> _pendingExpire = new List<EffectExpiredEvent>();

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
            ctx.Events.Subscribe<ApplyEffectEvent>(e => _pendingApply.Add(e));
            ctx.Events.Subscribe<RemoveEffectEvent>(e => _pendingRemove.Add(e));
            ctx.Events.Subscribe<EffectExpiredEvent>(e => _pendingExpire.Add(e));
        }

        public void ProcessEvents()
        {
            for (int i = 0; i < _pendingApply.Count; i++)
                ApplyOne(_pendingApply[i]);
            for (int i = 0; i < _pendingRemove.Count; i++)
                RemoveByKey(_pendingRemove[i].Target, _pendingRemove[i].Key);
            for (int i = 0; i < _pendingExpire.Count; i++)
                RemoveByKey(_pendingExpire[i].Target, _pendingExpire[i].Key);

            _pendingApply.Clear();
            _pendingRemove.Clear();
            _pendingExpire.Clear();
        }

        public void Update(long tick) { }

        void ApplyOne(ApplyEffectEvent e)
        {
            _ctx.Events.Emit(new EffectAppliedEvent
            {
                Target    = e.Target,
                Source    = e.Source,
                Key       = e.Key,
                Magnitude = e.Magnitude,
            });
            if (e.DurationTicks <= 0) return;

            _ctx.Effects.TryGet(e.Target, out var data);
            var next = new EffectsData();
            if (data != null && data.Active != null)
                next.Active.AddRange(data.Active);
            next.Active.Add(new EffectInstance
            {
                Key            = e.Key,
                Magnitude      = e.Magnitude,
                RemainingTicks = e.DurationTicks,
                Source         = e.Source,
                AppliesPerTick = e.AppliesPerTick,
            });
            _ctx.Effects.Set(e.Target, next);
        }

        void RemoveByKey(EntityId target, string key)
        {
            if (!_ctx.Effects.TryGet(target, out var data) || data == null || data.Active == null) return;
            int idx = -1;
            for (int i = 0; i < data.Active.Count; i++)
            {
                if (data.Active[i].Key == key) { idx = i; break; }
            }
            if (idx < 0) return;
            var next = new EffectsData();
            for (int i = 0; i < data.Active.Count; i++)
                if (i != idx) next.Active.Add(data.Active[i]);
            _ctx.Effects.Set(target, next);
        }
    }
}
