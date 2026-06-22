namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Intrinsic per-atom-type salience — the seed strength + flags a fact is etched at, before
    /// experience reshapes it. Importance = strength (what_is_memory.md), graded by the canonical
    /// flag tiers: INNATE = structural/permanent (never decays), SURPRISE = survival-grade
    /// (decay-resistant), ordinary = learned fact (fades at the normal rate). Re-observation refreshes
    /// an atom back to its salience (vivid again).
    /// </summary>
    public static class MemorySalience
    {
        public const byte Ordinary = 160;   // a learned, fade-able fact

        /// <summary>The seed meta for one atom type.</summary>
        public static AtomMeta For(AtomTypeId atom)
        {
            int v = atom.Value;
            // PLACES vocabulary (PlaceAtoms id range 5000+).
            if (v >= PlaceAtoms.KindBase && v < PlaceAtoms.Provisions.Value)
                return new AtomMeta(255, MemoryFlags.Innate);     // building kind: structural, permanent
            if (v == PlaceAtoms.Danger.Value)
                return new AtomMeta(255, MemoryFlags.Surprise);   // danger: survival-grade, resists decay
            if (v == PlaceAtoms.Provisions.Value)
                return new AtomMeta(Ordinary, MemoryFlags.None);  // provisions: ordinary learned fact
            return new AtomMeta(Ordinary, MemoryFlags.None);      // default: ordinary
        }
    }
}
