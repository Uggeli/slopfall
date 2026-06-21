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
            return false;                                            // full — Task 4 adds eviction
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
