namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>PLACE atoms, identified by AtomName — see AtomNames.
    /// BuildingKind is presence-per-kind; Provisions/Danger are graded facts.</summary>
    public static class PlaceAtoms
    {
        public static AtomTypeId Kind(BuildingKind kind) => AtomNames.From(kind).ToId();
        public static AtomTypeId Provisions => AtomName.PlaceProvisions.ToId();
        public static AtomTypeId Danger => AtomName.PlaceDanger.ToId();
    }
}
