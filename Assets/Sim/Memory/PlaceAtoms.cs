namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>Id authority for PLACES atoms (id range 5000+, above the entity/activity ranges).
    /// BuildingKind is presence-per-kind; Provisions/Danger are graded facts.</summary>
    public static class PlaceAtoms
    {
        public const int KindBase = 5000;
        public static AtomTypeId Kind(BuildingKind kind) => AtomNames.From(kind).ToId();
        public static AtomTypeId Provisions => AtomName.PlaceProvisions.ToId();
        public static AtomTypeId Danger => AtomName.PlaceDanger.ToId();
    }
}
