namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Per-atom memory metadata, index-aligned to a record's <see cref="AtomBag"/> atoms.
    /// <see cref="Strength"/> is the importance proxy the whole design maintains — arousal/salience
    /// wrote it, recall refreshes it, sleep decays it — now carried per remembered fact rather than
    /// once per record, so heterogeneous facts (structural kind vs. fleeting danger) erode at their
    /// own rates. <see cref="Flags"/> is the atom's INNATE (decay/evict-immune) / SURPRISE
    /// (decay-resistant) status.
    /// </summary>
    public readonly struct AtomMeta
    {
        public readonly byte Strength;
        public readonly MemoryFlags Flags;

        public AtomMeta(byte strength, MemoryFlags flags) { Strength = strength; Flags = flags; }

        public bool IsInnate => (Flags & MemoryFlags.Innate) != 0;
        public bool IsSurprise => (Flags & MemoryFlags.Surprise) != 0;

        public AtomMeta WithStrength(byte strength) => new AtomMeta(strength, Flags);
    }
}
