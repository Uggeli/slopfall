using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion that MERGES the old EffectLifecycleSystem + EffectTickSystem
    // into one system. Both old systems wrote the Effects registry (Lifecycle via
    // Set on add/remove, Tick via Set on decrement); in the new model exactly one
    // system computes the whole new EffectsData per entity and emits ONE
    // EffectsSetIntent per touched entity. EffectsRegistry (the sole applier) lives
    // in EffectsRegistry.cs and already declares EffectsSetIntent.
    //
    // Apply/Remove handling: the old apply/remove channels were ApplyEffectEvent /
    // RemoveEffectEvent (reference-type ISimEvent classes in the old bus). The new
    // bus only carries struct IEvent, so we declare struct equivalents here —
    // SimEvents.cs has no ApplyEffectIntent/RemoveEffectIntent (verified). The signal
    // events EffectAppliedEvent / EffectTickedEvent / EffectExpiredEvent are already
    // structs in SimEvents.cs and are reused unqualified.
    //
    // Reuses EffectsData / EffectInstance from the parent DaggerfallWorkshop.Sim
    // namespace unqualified.

    /// Intent: request that an effect be applied to an entity (struct port of the old
    /// ApplyEffectEvent). DurationTicks == 0 means instantaneous — emit the applied
    /// signal but never persist.
    public struct ApplyEffectIntent : IEvent
    {
        public EntityId Target;
        public EntityId Source;
        public string Key;
        public int Magnitude;
        public int DurationTicks;   // 0 = instantaneous (apply, never persist)
        public bool AppliesPerTick; // true → fires EffectTickedEvent each tick
    }

    /// Intent: request removal of an effect by key (struct port of the old
    /// RemoveEffectEvent). The first matching active instance is removed.
    public struct RemoveEffectIntent : IEvent
    {
        public EntityId Target;
        public string Key;
    }

    /// Headless effect authority. In ONE pass it folds, per affected entity:
    ///   - lifecycle adds from ApplyEffectIntent (instantaneous → signal only),
    ///   - lifecycle removes from RemoveEffectIntent (first key match),
    ///   - per-tick decay: decrement RemainingTicks, fire EffectTickedEvent for
    ///     AppliesPerTick effects, drop effects that just hit zero and fire
    ///     EffectExpiredEvent for them.
    /// Then emits exactly one EffectsSetIntent{ Id, new EffectsData } per touched
    /// entity. Preserves the original two-system logic exactly, except the round-trip
    /// where Tick emitted EffectExpiredEvent and Lifecycle removed it next tick: the
    /// expired effect is dropped from the same computed snapshot here, and the
    /// EffectExpiredEvent signal is still emitted for other consumers.
    public sealed class EffectsSystem : SimSystem
    {
        readonly EffectsRegistry _effects;

        public EffectsSystem(EventBus events, EffectsRegistry effects) : base(events)
        {
            _effects = effects;
        }

        public override void Update(long tick)
        {
            var applies = Events.GetEvents<ApplyEffectIntent>();
            var removes = Events.GetEvents<RemoveEffectIntent>();

            // The set of entities whose EffectsData may change this tick:
            // every entity that currently has effects (per-tick decay) plus any
            // targeted by an apply/remove intent this tick.
            var touched = new HashSet<EntityId>();
            foreach (var kv in _effects.All)
                touched.Add(kv.Key);
            for (int i = 0; i < applies.Length; i++)
                touched.Add(applies[i].Target);
            for (int i = 0; i < removes.Length; i++)
                touched.Add(removes[i].Target);

            foreach (var target in touched)
            {
                _effects.TryGet(target, out var current);

                // Start from the current active list (snapshot copy we own).
                var active = new List<EffectInstance>();
                if (current != null && current.Active != null)
                    active.AddRange(current.Active);

                bool changed = false;

                // 1) Lifecycle adds (preserve EffectLifecycleSystem.ApplyOne order:
                //    applies are processed before removes).
                for (int i = 0; i < applies.Length; i++)
                {
                    var e = applies[i];
                    if (!e.Target.Equals(target)) continue;

                    Events.Publish(new EffectAppliedEvent
                    {
                        Target    = e.Target,
                        Source    = e.Source,
                        Key       = e.Key,
                        Magnitude = e.Magnitude,
                    });
                    if (e.DurationTicks <= 0) continue; // instantaneous: never persist

                    active.Add(new EffectInstance
                    {
                        Key            = e.Key,
                        Magnitude      = e.Magnitude,
                        RemainingTicks = e.DurationTicks,
                        Source         = e.Source,
                        AppliesPerTick = e.AppliesPerTick,
                    });
                    changed = true;
                }

                // 2) Lifecycle removes by key (first matching instance), same as
                //    EffectLifecycleSystem.RemoveByKey.
                for (int i = 0; i < removes.Length; i++)
                {
                    var r = removes[i];
                    if (!r.Target.Equals(target)) continue;
                    int idx = -1;
                    for (int j = 0; j < active.Count; j++)
                    {
                        if (active[j].Key == r.Key) { idx = j; break; }
                    }
                    if (idx < 0) continue;
                    active.RemoveAt(idx);
                    changed = true;
                }

                // 3) Per-tick decay (EffectTickSystem.Update logic), now in the same
                //    snapshot. An effect that just hit zero is dropped here AND fires
                //    EffectExpiredEvent for any other consumer.
                var decayed = new List<EffectInstance>(active.Count);
                for (int i = 0; i < active.Count; i++)
                {
                    var fx = active[i];
                    if (fx.RemainingTicks <= 0)
                    {
                        // Matches the old TickSystem: already-zero entries are kept
                        // verbatim (not decremented, no signal).
                        decayed.Add(fx);
                        continue;
                    }

                    if (fx.AppliesPerTick)
                    {
                        Events.Publish(new EffectTickedEvent
                        {
                            Target    = target,
                            Source    = fx.Source,
                            Key       = fx.Key,
                            Magnitude = fx.Magnitude,
                        });
                    }

                    long remaining = fx.RemainingTicks - 1;
                    changed = true;

                    if (remaining == 0)
                    {
                        Events.Publish(new EffectExpiredEvent
                        {
                            Target = target,
                            Key    = fx.Key,
                        });
                        // Dropped from the snapshot in the same tick (the old model
                        // round-tripped this through Lifecycle next tick).
                        continue;
                    }

                    decayed.Add(new EffectInstance
                    {
                        Key            = fx.Key,
                        Magnitude      = fx.Magnitude,
                        RemainingTicks = remaining,
                        Source         = fx.Source,
                        AppliesPerTick = fx.AppliesPerTick,
                    });
                }

                if (!changed) continue;

                var next = new EffectsData();
                next.Active.AddRange(decayed);
                Events.Publish(new EffectsSetIntent { Id = target, Data = next });
            }
        }
    }
}
