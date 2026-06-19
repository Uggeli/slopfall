using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    public enum MemoryKind
    {
        Met,            // first real acquaintance with Other, at Building
        BecameFriend,   // regard + familiarity crossed the friendship bar
        ReceivedHelp,   // Other gave coin when asked
        GaveHelp,       // gave coin to Other when asked
        WasRefused,     // asked Other for help and was turned away
        RefusedToHelp,  // turned Other away
    }

    public struct MemoryEntry
    {
        public long Tick;
        public MemoryKind Kind;
        public EntityId Other;
        public int Building;        // BuildingRegistry key, -1 if nowhere
    }

    public sealed class MemoryData
    {
        /// Newest-first episodic ring, bounded — older memories fall away.
        /// Atoms' consolidation (episode -> fact) comes much later; v1 is the
        /// raw episode store.
        public List<MemoryEntry> Entries = new List<MemoryEntry>();
    }
}
