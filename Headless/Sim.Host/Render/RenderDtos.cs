using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Host.Render
{
    // Plain JSON DTOs for the region viewer. Two payloads, matching the old
    // static/dynamic seam: WorldStaticDto is sent once at connect (map geometry),
    // SnapshotDto streams every published tick (agents + global clock/weather).
    // Enums serialize as strings (JsonStringEnumConverter) so the browser can
    // colour/label by name.

    /// One-time map geometry for the whole region (one combined world-coord space).
    public sealed class WorldStaticDto
    {
        public string RegionName;
        public int GridWidth, GridHeight;   // walkability cells
        public float CellSize;              // metres per cell (world = cell * CellSize)
        public int BlocksWide, BlocksHigh;
        public string CostBase64;           // GridWidth*GridHeight bytes; 0 = blocked, else walkable step cost

        public List<BuildingDto> Buildings = new List<BuildingDto>();
        public List<SettlementDto> Settlements = new List<SettlementDto>();
    }

    public sealed class BuildingDto
    {
        public int Id;
        public BuildingKind Kind;
        public float X, Z;        // world metres
        public float Yaw;         // degrees
        public int Settlement;    // owning settlement id, -1 if none
    }

    public sealed class SettlementDto
    {
        public int Id;
        public string Name;
        public SettlementKind Kind;
        public float OriginX, OriginZ;   // world metres (block origin of this town)
        public int BlocksWide, BlocksHigh;
        public int Residents;
    }

    /// Per-tick world state streamed to the viewer.
    public sealed class SnapshotDto
    {
        public long Tick;
        public int Year, Month, Day, Hour, Minute;
        public bool IsNight;
        public float SunIntensity;
        public WeatherKind Weather;
        public int Population;
        public float TimeScale;
        public bool Paused;

        public List<AgentDto> Agents = new List<AgentDto>();
    }

    public sealed class AgentDto
    {
        public int Id;
        public float X, Z, Yaw;    // world metres / degrees
        public EntityKind Kind;
        public ActivityKind Activity;
        public ActivityPhase Phase;
    }
}
