namespace DaggerfallWorkshop.Sim.Memory
{
    /// Atom types for an agent's perception of its OWN body. Reserved band 7000-7099
    /// to avoid colliding with entity Kind/Role/Race/Activity atom ranges.
    public static class SomaticAtoms
    {
        public static readonly AtomTypeId Hunger = AtomName.SomaticHunger.ToId();
        public static readonly AtomTypeId Energy = AtomName.SomaticEnergy.ToId();   // tiredness deficit
        public static readonly AtomTypeId Fear   = AtomName.SomaticFear.ToId();
    }
}
