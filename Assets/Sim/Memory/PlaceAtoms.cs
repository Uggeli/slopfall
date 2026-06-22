namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>Id authority for PLACES atoms (id range 5000+, above the entity/activity ranges).
    /// BuildingKind is presence-per-kind; Provisions/Danger are graded facts.</summary>
    public static class PlaceAtoms
    {
        public const int KindBase = 5000;
        public static AtomTypeId Kind(BuildingKind kind) => new AtomTypeId(KindBase + (int)kind);
        public static AtomTypeId Provisions => new AtomTypeId(6000);
        public static AtomTypeId Danger => new AtomTypeId(6001);
    }
}
