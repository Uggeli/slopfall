using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace DaggerfallWorkshop.Sim
{
    /// Sim-native building taxonomy. Values mirror DFLocation.BuildingTypes
    /// so block data converts by cast; sim code never needs the DaggerfallConnect
    /// enum directly.
    public enum BuildingKind
    {
        None = -1,
        Alchemist,
        HouseForSale,
        Armorer,
        Bank,
        Town4,
        Bookseller,
        ClothingStore,
        FurnitureStore,
        GemStore,
        GeneralStore,
        Library,
        GuildHall,
        PawnShop,
        WeaponSmith,
        Temple,
        Tavern,
        Palace,
        House1,
        House2,
        House3,
        House4,
        House5,
        House6,
        Town23,
        Ship,
        Special1 = 0x74,
        Special2 = 0xdf,
        Special3 = 0xf9,
        Special4 = 0xfa,
        // Sim-native workplaces — synthesized at load (not from Daggerfall block data),
        // so their values sit clear of the 0..0x18 range the cast from BuildingType uses
        // and the 0x74..0xfa specials.
        Farm = 0x100,       // primary food production (a settlement's farmland/hinterland)
        Fishery = 0x101,    // primary food production at the coast (island/coastal regions)
        Mine = 0x102,       // primary ore production in the hills (mountain-climate regions); ore is exported
        Pasture = 0x103,    // primary fibre production (wool) — feeds the weaver (industry layers)
        Weaver = 0x104,     // secondary workshop: wool → cloth (industry layers)
    }

    public sealed class BuildingRow
    {
        public BuildingKind Kind;
        public int FactionId;
        public int Quality;        // 1-20ish, drives shop stock / service quality
        public int NameSeed;
        public float X, Z;         // world meters, town-local origin at block (0,0)
        public float YRotation;    // degrees
        public int BlockX, BlockY; // town grid cell
        public int RecordIndex;    // subrecord index within the block
    }

    /// All structures of the currently loaded location, keyed by a stable
    /// per-load index. Written once by TownLoader at load; read-only after.
    /// Single-town for now — multi-town residency arrives with LOD work.
    public sealed class BuildingRegistry
    {
        readonly ConcurrentDictionary<int, BuildingRow> _d = new ConcurrentDictionary<int, BuildingRow>();
        int _nextIndex = -1;

        public int Add(BuildingRow row)
        {
            int index = Interlocked.Increment(ref _nextIndex);
            _d[index] = row;
            return index;
        }

        public bool TryGet(int index, out BuildingRow row) => _d.TryGetValue(index, out row);

        public void Clear()
        {
            _d.Clear();
            Interlocked.Exchange(ref _nextIndex, -1);
        }

        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<int, BuildingRow>> All => _d;
    }
}
