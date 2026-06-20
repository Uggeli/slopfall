using DaggerfallConnect;

namespace DaggerfallWorkshop.Sim
{
    /// The single DFRegion.LocationTypes → PoiRole mapping. Lives in Sim.World (which
    /// references the Daggerfall API) so Sim.Core stays API-enum-free. The three dungeon
    /// types collapse to PoiRole.Dungeon; the dungeon sub-type stays available via
    /// RegionMapTable.DungeonType for later work.
    public static class PoiClassifier
    {
        public static PoiRole PoiRoleOf(DFRegion.LocationTypes t)
        {
            switch (t)
            {
                case DFRegion.LocationTypes.TownCity:         return PoiRole.City;
                case DFRegion.LocationTypes.TownHamlet:       return PoiRole.Hamlet;
                case DFRegion.LocationTypes.TownVillage:      return PoiRole.Village;
                case DFRegion.LocationTypes.HomeFarms:        return PoiRole.Farm;
                case DFRegion.LocationTypes.ReligionTemple:   return PoiRole.Temple;
                case DFRegion.LocationTypes.Tavern:           return PoiRole.Tavern;
                case DFRegion.LocationTypes.DungeonLabyrinth: return PoiRole.Dungeon;
                case DFRegion.LocationTypes.DungeonKeep:      return PoiRole.Dungeon;
                case DFRegion.LocationTypes.DungeonRuin:      return PoiRole.Dungeon;
                case DFRegion.LocationTypes.Coven:            return PoiRole.Coven;
                case DFRegion.LocationTypes.ReligionCult:     return PoiRole.CultShrine;
                case DFRegion.LocationTypes.Graveyard:        return PoiRole.Graveyard;
                case DFRegion.LocationTypes.HomeWealthy:      return PoiRole.ManorWealthy;
                case DFRegion.LocationTypes.HomePoor:         return PoiRole.HovelPoor;
                case DFRegion.LocationTypes.HomeYourShips:    return PoiRole.PlayerShip;
                default:                                      return PoiRole.None;
            }
        }
    }
}
