using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Aggregated modifiers from all active effects on an entity, indexed by
    /// modifier key (e.g. "Strength", "Speed", "MagicResistFire"). Written by
    /// EffectAggregateSystem; read by anything that needs an entity's final
    /// stat values after effects.
    public sealed class EffectAggregateData
    {
        public Dictionary<string, int> Modifiers = new Dictionary<string, int>();
    }
}
