using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Trait axes, 0..1 with 0.5 as the population center. Ported from ODD's
    /// trait vector idea; these scale weights, drifts, gates, and social
    /// chemistry so the population stops being 337 copies of one person.
    public static class TraitIndex
    {
        public const int Sociability  = 0;  // loner .. social butterfly
        public const int Industry     = 1;  // idler .. workaholic
        public const int Restlessness = 2;  // homebody .. wanderer
        public const int Chronotype   = 3;  // lark .. night owl
        public const int Warmth       = 4;  // cold .. charitable
        public const int Count        = 5;
    }

    public sealed class PersonalityData
    {
        public double[] Traits = new double[TraitIndex.Count];

        /// Per-agent need weights (ODD's WeightsRegistry made real) and drift
        /// scales, derived once from traits at spawn.
        public double[] Weights = new double[NeedAxis.Count];
        public double[] DriftScale = new double[NeedAxis.Count];

        public double Trait(int index) => Traits[index];

        /// Bake weights/drifts from traits. Centered traits (0.5) reproduce
        /// the old global constants exactly.
        public static PersonalityData Derive(double[] traits)
        {
            var p = new PersonalityData { Traits = traits };

            p.Weights[NeedAxis.Hunger] = ActivityCatalog.Weights[NeedAxis.Hunger];
            p.Weights[NeedAxis.EnergyDef] = ActivityCatalog.Weights[NeedAxis.EnergyDef];
            p.Weights[NeedAxis.SocialDef] = ActivityCatalog.Weights[NeedAxis.SocialDef]
                * (0.5 + traits[TraitIndex.Sociability]);
            p.Weights[NeedAxis.CoinDef] = ActivityCatalog.Weights[NeedAxis.CoinDef]
                * (0.5 + traits[TraitIndex.Industry]);

            for (int i = 0; i < NeedAxis.Count; i++) p.DriftScale[i] = 1.0;
            p.DriftScale[NeedAxis.SocialDef] = 0.5 + traits[TraitIndex.Sociability];

            return p;
        }

        /// Short human-readable read of the extremes, for inspectors.
        public string Describe()
        {
            var parts = new List<string>();
            Note(parts, TraitIndex.Sociability, "loner", "gregarious");
            Note(parts, TraitIndex.Industry, "idle", "industrious");
            Note(parts, TraitIndex.Restlessness, "homebody", "restless");
            Note(parts, TraitIndex.Chronotype, "early riser", "night owl");
            Note(parts, TraitIndex.Warmth, "cold", "warm-hearted");
            return parts.Count == 0 ? "unremarkable" : string.Join(", ", parts.ToArray());
        }

        void Note(List<string> parts, int trait, string low, string high)
        {
            if (Traits[trait] < 0.25) parts.Add(low);
            else if (Traits[trait] > 0.75) parts.Add(high);
        }
    }

    /// Per-entity personality. Written once by TownLoader at spawn.
    public sealed class PersonalityRegistry
    {
        readonly ConcurrentDictionary<EntityId, PersonalityData> _d = new ConcurrentDictionary<EntityId, PersonalityData>();

        public void Set(EntityId id, PersonalityData data) => _d[id] = data;
        public bool TryGet(EntityId id, out PersonalityData data) => _d.TryGetValue(id, out data);
        public void Remove(EntityId id) { _d.TryRemove(id, out var _); }

        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, PersonalityData>> All => _d;
    }
}
