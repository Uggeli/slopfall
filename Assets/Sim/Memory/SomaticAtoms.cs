namespace DaggerfallWorkshop.Sim.Memory
{
    /// Atom types for an agent's perception of its OWN body, identified by AtomName
    /// (collision-free by enum, not a reserved id band) — see AtomNames.
    public static class SomaticAtoms
    {
        public static readonly AtomTypeId Hunger = AtomName.SomaticHunger.ToId();
        public static readonly AtomTypeId Energy = AtomName.SomaticEnergy.ToId();   // tiredness deficit
        public static readonly AtomTypeId Fear   = AtomName.SomaticFear.ToId();
    }
}
