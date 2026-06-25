namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Maps each owning enum to its AtomName-identified AtomTypeId — see AtomNames.
    /// The owning system picks which catalog atom to stamp; collision-freedom is guaranteed
    /// by the AtomName enum, not by numeric id bands. All current perceivable attributes
    /// are categorical → presence atoms.
    /// </summary>
    public static class PerceivableAtoms
    {
        public static AtomTypeId Kind(EntityKind kind) => AtomNames.From(kind).ToId();
        public static AtomTypeId Role(ResidentRole role) => AtomNames.From(role).ToId();
        public static AtomTypeId Race(int raceId) => AtomNames.FromRace(raceId).ToId();
        public static AtomTypeId Activity(ActivityKind kind) => AtomNames.From(kind).ToId();

        /// <summary>The current activity as an atom; false for None (no observable-activity atom).</summary>
        public static bool TryActivity(ActivityKind kind, out AtomTypeId atom)
        {
            if (kind == ActivityKind.None) { atom = AtomTypeId.None; return false; }
            atom = Activity(kind);
            return true;
        }
    }
}
