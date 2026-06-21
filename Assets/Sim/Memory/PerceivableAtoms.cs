namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// The single id authority for perceivable atoms. Each helper maps an owning enum to a
    /// collision-free AtomTypeId via a stable Base + (int)enum offset — a convention, not a
    /// per-value mapper: the owning system picks which catalog atom to stamp. Ranges keep
    /// categories separable. All current perceivable attributes are categorical -> presence atoms.
    /// </summary>
    public static class PerceivableAtoms
    {
        public const int KindBase = 1000;
        public const int RoleBase = 2000;
        public const int RaceBase = 3000;
        public const int ActivityBase = 4000;

        public static AtomTypeId Kind(EntityKind kind) => new AtomTypeId(KindBase + (int)kind);
        public static AtomTypeId Role(ResidentRole role) => new AtomTypeId(RoleBase + (int)role);
        public static AtomTypeId Race(int raceId) => new AtomTypeId(RaceBase + raceId);
        public static AtomTypeId Activity(ActivityKind kind) => new AtomTypeId(ActivityBase + (int)kind);

        /// <summary>The current activity as an atom; false for None (no observable-activity atom).</summary>
        public static bool TryActivity(ActivityKind kind, out AtomTypeId atom)
        {
            if (kind == ActivityKind.None) { atom = AtomTypeId.None; return false; }
            atom = Activity(kind);
            return true;
        }
    }
}
