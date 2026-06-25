namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Thin shim for per-atom-type salience. The canonical tiers now live in AtomCatalog (INNATE,
    /// SURPRISE, ordinary graded by strength + flags). For(AtomTypeId) simply delegates to
    /// AtomCatalog.For(atom).Salience, kept here to avoid refactoring AgentMemoryRegistry's caller.
    /// </summary>
    public static class MemorySalience
    {
        public const byte Ordinary = 160;   // a learned, fade-able fact (kept for any external reference)

        /// <summary>The seed meta for one atom type — now the catalog's Salience face. Kept as a thin
        /// shim so the single caller (AgentMemoryRegistry.MergePlaceAtom) is untouched.</summary>
        public static AtomMeta For(AtomTypeId atom) => AtomCatalog.For(atom).Salience;
    }
}
