using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    public enum ResidentRole
    {
        Resident,   // lives here
        Keeper,     // works here (shopkeeper, publican, priest, guild steward)
    }

    public sealed class ResidencyData
    {
        public int BuildingIndex;   // key into BuildingRegistry
        public ResidentRole Role;
    }
}
