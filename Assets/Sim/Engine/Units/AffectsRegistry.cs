using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the per-agent emotional store. Reuses AffectsData / Affect /
    // AffectKind from the enclosing DaggerfallWorkshop.Sim namespace. AffectsSystem
    // computes the new per-agent value (copy-on-write) and emits a whole-value set
    // intent; this registry is the sole applier.

    /// Intent: "store this agent's recomputed affect list." Whole-value set — the
    /// owning system reads other registries, builds the new AffectsData, emits it.
    public struct AffectsSetIntent : IEvent { public EntityId Id; public AffectsData Data; }

    public sealed class AffectsRegistry : Registry
    {
        readonly Dictionary<EntityId, AffectsData> _d = new Dictionary<EntityId, AffectsData>();

        public AffectsRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            var sets = Events.GetEvents<AffectsSetIntent>();
            for (int i = 0; i < sets.Length; i++)
                _d[sets[i].Id] = sets[i].Data;          // last write per id wins

            var gone = Events.GetEvents<DespawnedEvent>();
            for (int i = 0; i < gone.Length; i++)
                _d.Remove(gone[i].Entity);
        }

        // --- read API ---
        public bool TryGet(EntityId id, out AffectsData data) => _d.TryGetValue(id, out data);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, AffectsData>> All => _d;

        /// Signed acute valence the holder feels toward `other` right now (sum of
        /// directed affects). 0 if none — the common case.
        public double ValenceToward(EntityId holder, EntityId other)
        {
            if (!_d.TryGetValue(holder, out var data)) return 0;
            double v = 0;
            var list = data.Active;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Target == other) v += Sign(list[i].Kind) * list[i].Intensity;
            return v;
        }

        public static double Sign(AffectKind k)
        {
            switch (k)
            {
                case AffectKind.Affection:
                case AffectKind.Gratitude:
                    return +1;
                case AffectKind.Resentment:
                case AffectKind.Aversion:
                case AffectKind.Fear:
                    return -1;
                default:
                    return 0;   // Loneliness is undirected
            }
        }
    }
}
