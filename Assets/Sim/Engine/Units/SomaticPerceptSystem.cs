using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// The agent perceives its own body: each sense tick, the salient need axes
    /// are stamped into the agent's own AtomBag as somatic atoms. This is the
    /// perceivable-atoms model applied inward — the seam future protocols and
    /// sleep-salience read from. ODD scoring still reads NeedsData directly.
    public sealed class SomaticPerceptSystem : SimSystem
    {
        public const int SenseEveryTicks = 5;

        readonly NeedsRegistry _needs;

        public SomaticPerceptSystem(EventBus events, NeedsRegistry needs) : base(events)
        {
            _needs = needs;
        }

        public override void Update(long tick)
        {
            if (tick % SenseEveryTicks != 0) return;
            foreach (var kv in _needs.All)
            {
                var v = kv.Value.V;
                Stamp(kv.Key, SomaticAtoms.Hunger, v[NeedAxis.Hunger]);
                Stamp(kv.Key, SomaticAtoms.Energy, v[NeedAxis.EnergyDef]);
                Stamp(kv.Key, SomaticAtoms.Fear,   v[NeedAxis.Fear]);
            }
        }

        void Stamp(EntityId id, AtomTypeId type, double value)
        {
            if (value <= 0) return;
            Events.Publish(new StampAtomIntent
            {
                Entity = id, Type = type,
                Value = Fixed.FromDouble(value > 1.0 ? 1.0 : value),
            });
        }
    }
}
