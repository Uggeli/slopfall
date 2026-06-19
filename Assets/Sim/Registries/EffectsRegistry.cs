using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// One active effect instance on an entity. Mutated only by sim systems
    /// (Lifecycle adds/removes, TickSystem decrements). Per-entity lists are
    /// replaced atomically via EffectsRegistry.Set so readers always see a
    /// consistent snapshot.
    public sealed class EffectInstance
    {
        public string Key;          // effect identifier (DFU effect key, e.g. "Poison-Drug")
        public int Magnitude;       // primary numeric parameter (damage, fortify amount, etc.)
        public long RemainingTicks; // ticks until expiry; 0 = expired
        public EntityId Source;     // caster
        public bool AppliesPerTick; // true for DoT-style effects (poison damage over time)
    }

    public sealed class EffectsData
    {
        /// Defensive: callers may read but should not mutate. To modify, build
        /// a new EffectsData and assign via EffectsRegistry.Set.
        public List<EffectInstance> Active = new List<EffectInstance>();
    }
}
