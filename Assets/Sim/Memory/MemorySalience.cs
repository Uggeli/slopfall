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
        public const byte Ordinary = 160;   // a learned, fade-able fact (kept for any external reference)

        /// <summary>The seed meta for one atom type — now the catalog's Salience face. Kept as a thin
        /// shim so the single caller (AgentMemoryRegistry.MergePlaceAtom) is untouched.</summary>
        public static AtomMeta For(AtomTypeId atom) => AtomCatalog.For(atom).Salience;
    }
}
