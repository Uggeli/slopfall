using System;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// A bounded, key-sorted store of MemoryRecords — one of PLACES/THINGS/EVENTS for one agent.
    /// Backed by a flat fixed-capacity array kept sorted ascending by MemoryKey (deterministic
    /// iteration + binary search). Encode/Refresh/Decay/eviction are plain methods here; they
    /// become CQRS deltas in Phase B. All arithmetic is integer/byte.
    /// </summary>
    public sealed class MemoryStore
    {
        readonly MemoryRecord[] _records;
        int _count;

        public MemoryStore(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _records = new MemoryRecord[capacity];
            _count = 0;
        }

        public int Count => _count;
        public int Capacity => _records.Length;
        public MemoryRecord this[int i] => _records[i];

        /// <summary>Index of the record with this key, or -1. Binary search on the sorted array.</summary>
        int IndexOf(MemoryKey key)
        {
            int lo = 0, hi = _count - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                int cmp = _records[mid].Key.CompareTo(key);
                if (cmp == 0) return mid;
                if (cmp < 0) lo = mid + 1; else hi = mid - 1;
            }
            return -1;
        }

        public bool TryGet(MemoryKey key, out MemoryRecord record)
        {
            int idx = IndexOf(key);
            if (idx < 0) { record = default; return false; }
            record = _records[idx];
            return true;
        }

        /// <summary>Remove the record with this key (preserving key order). Returns false if absent.</summary>
        public bool Remove(MemoryKey key)
        {
            int idx = IndexOf(key);
            if (idx < 0) return false;
            System.Array.Copy(_records, idx + 1, _records, idx, _count - idx - 1);
            _count--;
            _records[_count] = default(MemoryRecord);
            return true;
        }

        /// <summary>
        /// Insert or replace a record. If the key already exists it is replaced in place
        /// (absolute insert — Encode wins). A new key inserts in sorted position while there is
        /// room. When full, eviction decides (Task 4); until then a full store rejects new keys.
        /// Returns true if the record is now stored.
        /// </summary>
        public bool Encode(in MemoryRecord record)
        {
            int idx = IndexOf(record.Key);
            if (idx >= 0) { _records[idx] = record; return true; }   // replace in place
            if (_count < _records.Length) { InsertSorted(record); return true; }
            return TryEvictAndInsert(record);                        // full — beat the weakest or bust
        }

        /// <summary>
        /// Reconsolidation: bump a record's strength (clamped to [0,255]) and set its LastRefresh
        /// to the recall tick. Returns false if the key is absent.
        /// </summary>
        public bool Refresh(MemoryKey key, int deltaStrength, long atTick)
        {
            int idx = IndexOf(key);
            if (idx < 0) return false;
            int ns = _records[idx].Strength + deltaStrength;
            if (ns < 0) ns = 0;
            else if (ns > 255) ns = 255;
            _records[idx] = _records[idx].WithStrength((byte)ns, atTick);
            return true;
        }

        /// <summary>
        /// Sleep decay: subtract (IsSurprise ? surpriseRate : normalRate) from each non-INNATE
        /// record's strength. A record whose strength reaches 0 (or below) is dropped, not floored
        /// at 0; survivors keep key order via in-place compaction. INNATE records are immune.
        /// LastRefresh is unchanged.
        /// Caller passes surpriseRate &lt;= normalRate (surprising memories resist decay).
        /// </summary>
        public void Decay(int normalRate, int surpriseRate)
        {
            int w = 0;
            for (int r = 0; r < _count; r++)
            {
                MemoryRecord rec = _records[r];
                if (rec.IsInnate) { _records[w++] = rec; continue; }
                int applicable = rec.IsSurprise ? surpriseRate : normalRate;
                int ns = rec.Strength - applicable;
                if (ns <= 0) continue;                                  // dropped
                _records[w++] = rec.WithStrength((byte)ns, rec.LastRefresh);
            }
            for (int i = w; i < _count; i++) _records[i] = default;     // release dropped slots
            _count = w;
        }

        /// <summary>
        /// Full-store write: evict the weakest evictable (non-INNATE) record and insert the new
        /// one iff the new record is more keepable than that weakest. Otherwise the write does
        /// not take. Returns whether the record was stored.
        /// </summary>
        bool TryEvictAndInsert(in MemoryRecord record)
        {
            int weakest = -1;
            for (int i = 0; i < _count; i++)
            {
                if (_records[i].IsInnate) continue;                 // INNATE is evict-immune
                if (weakest < 0 || LessKeepable(_records[i], _records[weakest])) weakest = i;
            }
            if (weakest < 0) return false;                          // everything INNATE — rejected
            if (!LessKeepable(_records[weakest], record)) return false;  // new doesn't beat the weakest

            // Remove the weakest (preserve order), then insert the new record in sorted position.
            Array.Copy(_records, weakest + 1, _records, weakest, _count - weakest - 1);
            _count--;
            InsertSorted(record);
            return true;
        }

        /// <summary>True if a is less keepable than b: weaker strength, else older LastRefresh,
        /// else lower Key. A total order, so eviction is deterministic.</summary>
        static bool LessKeepable(in MemoryRecord a, in MemoryRecord b)
        {
            if (a.Strength != b.Strength) return a.Strength < b.Strength;
            if (a.LastRefresh != b.LastRefresh) return a.LastRefresh < b.LastRefresh;
            return a.Key.CompareTo(b.Key) < 0;
        }

        /// <summary>Insert into the sorted array at the lower-bound position. Caller guarantees
        /// the key is absent and there is room.</summary>
        void InsertSorted(in MemoryRecord record)
        {
            int pos = LowerBound(record.Key);
            Array.Copy(_records, pos, _records, pos + 1, _count - pos);
            _records[pos] = record;
            _count++;
        }

        /// <summary>First index whose key is >= the given key (insertion point).</summary>
        int LowerBound(MemoryKey key)
        {
            int lo = 0, hi = _count;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (_records[mid].Key.CompareTo(key) < 0) lo = mid + 1; else hi = mid;
            }
            return lo;
        }
    }
}
