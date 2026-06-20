using System.Collections.Generic;
using DaggerfallWorkshop.Sim.Engine;

namespace DaggerfallWorkshop.Sim
{
    /// Coarse kind of a flora instance (from the SpeciesCatalog). Pure Sim.Core enum.
    public enum FloraCategory { Unknown, Tree, Bush, Plant, Crop, Rock, Water, Deadwood }

    /// What a flora instance yields when harvested (future). Pure Sim.Core enum.
    public enum ResourceKind { None, Wood, Forage, Stone, Reed, Herb }

    /// One piece of world vegetation/resource scenery (a tree/rock/plant on a tile).
    /// Parsed from RMB ground scenery at load; classified via SpeciesCatalog. Render is a
    /// separate consumer; harvesting is future. Written only by the loader; read thereafter.
    public sealed class FloraInstance
    {
        public int Archive, Record, Climate;
        public float X, Y, Z;
        public int SpeciesId;
        public FloraCategory Category;
        public ResourceKind Resource;
    }

    /// Every flora instance in the loaded region, in load order. Seeded by the region
    /// loader (direct Add). Static after load; no runtime mutation.
    public sealed class FloraRegistry : Registry
    {
        readonly List<FloraInstance> _all = new List<FloraInstance>();
        public FloraRegistry(EventBus events) : base(events) { }
        public FloraInstance Add(FloraInstance f) { _all.Add(f); return f; }
        public IReadOnlyList<FloraInstance> All => _all;
        public int Count => _all.Count;
        public override void Update(long tick) { }
    }
}
