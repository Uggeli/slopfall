using System;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>INNATE = decay/evict-immune (the species seed); SURPRISE = decay-resistant.</summary>
    [Flags]
    public enum MemoryFlags : byte
    {
        None = 0,
        Innate = 1,
        Surprise = 2,
    }

    /// <summary>
    /// The unit of storage for PLACES/THINGS/EVENTS: a delta against a category's prediction.
    /// deltaBag holds ONLY the atoms that diverge from the prediction (or the full percept when
    /// novel — CategoryRef.None). strength is the one scalar doing write-depth, decay target,
    /// and eviction order. Immutable; mutation produces a new record (WithStrength).
    /// </summary>
    public readonly struct MemoryRecord
    {
        public readonly MemoryKey Key;
        public readonly CategoryId CategoryRef;   // None => novelty (stored verbatim)
        public readonly AtomBag DeltaBag;
        public readonly byte Strength;
        public readonly long WrittenAt;
        public readonly long LastRefresh;
        public readonly MemoryFlags Flags;

        public MemoryRecord(MemoryKey key, CategoryId categoryRef, AtomBag deltaBag,
                            byte strength, long writtenAt, long lastRefresh, MemoryFlags flags)
        {
            Key = key;
            CategoryRef = categoryRef;
            DeltaBag = deltaBag;
            Strength = strength;
            WrittenAt = writtenAt;
            LastRefresh = lastRefresh;
            Flags = flags;
        }

        public bool IsInnate => (Flags & MemoryFlags.Innate) != 0;
        public bool IsSurprise => (Flags & MemoryFlags.Surprise) != 0;
        public bool IsNovel => CategoryRef.IsNone;

        /// <summary>New record with a different strength and lastRefresh; everything else kept.
        /// Refresh passes the refresh tick; Decay passes the existing LastRefresh (decay is not
        /// a refresh).</summary>
        public MemoryRecord WithStrength(byte strength, long lastRefresh)
            => new MemoryRecord(Key, CategoryRef, DeltaBag, strength, WrittenAt, lastRefresh, Flags);
    }
}
