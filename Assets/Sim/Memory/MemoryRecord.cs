using System;
using System.Collections.Generic;

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
    /// novel — CategoryRef.None). Each atom carries its own <see cref="AtomMeta"/> (strength + flags),
    /// index-aligned to the bag — so heterogeneous facts in one record decay at their own
    /// importance-scaled rates. Immutable; mutation produces a new record.
    /// </summary>
    public readonly struct MemoryRecord
    {
        public readonly MemoryKey Key;
        public readonly CategoryId CategoryRef;   // None => novelty (stored verbatim)
        public readonly AtomBag DeltaBag;
        public readonly AtomMeta[] Meta;          // index-aligned to DeltaBag.Atoms
        public readonly long WrittenAt;
        public readonly long LastRefresh;

        /// <summary>Per-atom record: meta[i] describes DeltaBag.Atoms[i].</summary>
        public MemoryRecord(MemoryKey key, CategoryId categoryRef, AtomBag deltaBag, AtomMeta[] meta,
                            long writtenAt, long lastRefresh)
        {
            Key = key;
            CategoryRef = categoryRef;
            DeltaBag = deltaBag;
            Meta = meta ?? Array.Empty<AtomMeta>();
            WrittenAt = writtenAt;
            LastRefresh = lastRefresh;
        }

        /// <summary>Back-compat constructor: broadcasts one (strength, flags) to every atom. Temporary
        /// shim while call sites migrate to per-atom meta — removed once they all do.</summary>
        public MemoryRecord(MemoryKey key, CategoryId categoryRef, AtomBag deltaBag,
                            byte strength, long writtenAt, long lastRefresh, MemoryFlags flags)
            : this(key, categoryRef, deltaBag, Broadcast(deltaBag, strength, flags), writtenAt, lastRefresh) { }

        static AtomMeta[] Broadcast(AtomBag bag, byte strength, MemoryFlags flags)
        {
            var meta = new AtomMeta[bag.Count];
            for (int i = 0; i < meta.Length; i++) meta[i] = new AtomMeta(strength, flags);
            return meta;
        }

        public bool IsNovel => CategoryRef.IsNone;

        /// <summary>The strongest atom's actual strength (what a record's etch-depth "reads" as).</summary>
        int MaxStrength
        {
            get { int max = 0; for (int i = 0; i < Meta.Length; i++) if (Meta[i].Strength > max) max = Meta[i].Strength; return max; }
        }

        /// <summary>Record keepability for eviction = the strongest atom (a record is as worth keeping
        /// as its most important fact); a record with any INNATE atom is evict-immune (saturates).</summary>
        public int EvictionStrength => AnyInnate ? 255 : MaxStrength;

        public bool AnyInnate
        {
            get { for (int i = 0; i < Meta.Length; i++) if (Meta[i].IsInnate) return true; return false; }
        }

        public bool AnySurprise
        {
            get { for (int i = 0; i < Meta.Length; i++) if (Meta[i].IsSurprise) return true; return false; }
        }

        // Record-level flag/strength reads (compat): "any atom has it" / strongest atom.
        public bool IsInnate => AnyInnate;
        public bool IsSurprise => AnySurprise;
        public byte Strength => (byte)MaxStrength;   // actual etch-depth (NOT eviction-saturated)

        /// <summary>Combined atom flags (compat, for the back-compat broadcast path). Faithful while a
        /// record's atoms share flags (broadcast-built); per-atom carry lands when consolidation/encode
        /// migrate off the shim.</summary>
        public MemoryFlags Flags
        {
            get { MemoryFlags f = MemoryFlags.None; for (int i = 0; i < Meta.Length; i++) f |= Meta[i].Flags; return f; }
        }

        /// <summary>Reconsolidation: bump every non-INNATE atom's strength (clamped 0..255) and set
        /// LastRefresh. INNATE atoms are already permanent.</summary>
        public MemoryRecord Refreshed(int delta, long atTick)
        {
            var meta = new AtomMeta[Meta.Length];
            for (int i = 0; i < Meta.Length; i++)
            {
                AtomMeta m = Meta[i];
                if (m.IsInnate) { meta[i] = m; continue; }
                int ns = m.Strength + delta;
                if (ns < 0) ns = 0; else if (ns > 255) ns = 255;
                meta[i] = m.WithStrength((byte)ns);
            }
            return new MemoryRecord(Key, CategoryRef, DeltaBag, meta, WrittenAt, atTick);
        }

        /// <summary>Sleep decay, per atom: each non-INNATE atom loses
        /// <c>max(1, base·(255−S)/255)</c> (important atoms barely erode, trivial ones fade fast);
        /// an atom at 0 is forgotten (dropped from the bag). LastRefresh unchanged.
        /// Surprise atoms use the smaller base. INNATE atoms are immune.</summary>
        public MemoryRecord Decay(int normalRate, int surpriseRate)
        {
            var atoms = DeltaBag.Atoms;
            var keepA = new List<Atom>(atoms.Count);
            var keepM = new List<AtomMeta>(atoms.Count);
            for (int i = 0; i < Meta.Length; i++)
            {
                AtomMeta m = Meta[i];
                if (m.IsInnate) { keepA.Add(atoms[i]); keepM.Add(m); continue; }
                int basis = m.IsSurprise ? surpriseRate : normalRate;
                int dec = basis * (255 - m.Strength) / 255;   // strength-scaled: important atoms barely erode
                if (dec < 1) dec = 1;
                int ns = m.Strength - dec;
                if (ns <= 0) continue;                       // atom forgotten
                keepA.Add(atoms[i]);
                keepM.Add(m.WithStrength((byte)ns));
            }
            // Nothing dropped → reuse the bag (only strengths changed); else rebuild it.
            AtomBag bag = keepA.Count == Meta.Length ? DeltaBag : AtomBag.Create(keepA);
            return new MemoryRecord(Key, CategoryRef, bag, keepM.ToArray(), WrittenAt, LastRefresh);
        }

        /// <summary>Broadcast a single strength onto every atom (compat helper); keeps the bag.</summary>
        public MemoryRecord WithStrength(byte strength, long lastRefresh)
        {
            var meta = new AtomMeta[Meta.Length];
            for (int i = 0; i < Meta.Length; i++) meta[i] = new AtomMeta(strength, Meta[i].Flags);
            return new MemoryRecord(Key, CategoryRef, DeltaBag, meta, WrittenAt, lastRefresh);
        }

        /// <summary>Replace the per-atom meta (caller guarantees index-alignment to DeltaBag).</summary>
        public MemoryRecord WithAtomMeta(AtomMeta[] meta, long lastRefresh)
            => new MemoryRecord(Key, CategoryRef, DeltaBag, meta, WrittenAt, lastRefresh);
    }
}
