namespace DaggerfallWorkshop.Sim
{
    /// Walks each entity's active effects and derives StatusFlags, writing
    /// StatusFlagsRegistry. Pure-derive system — sole writer of the registry.
    ///
    /// Effect-key → flag mapping is hardcoded for now. The list grows as
    /// individual DFU effects port over. Keys that don't map to a flag are
    /// ignored.
    public sealed class StatusFlagDeriveSystem : ISystem
    {
        SimulationContext _ctx;

        public void Init(SimulationContext ctx) { _ctx = ctx; }
        public void ProcessEvents() { }

        public void Update(long tick)
        {
            foreach (var kv in _ctx.Effects.All)
            {
                var data = kv.Value;
                if (data == null || data.Active == null || data.Active.Count == 0)
                {
                    _ctx.StatusFlags.Remove(kv.Key);
                    continue;
                }

                StatusFlags flags = DaggerfallWorkshop.Sim.StatusFlags.None;
                for (int i = 0; i < data.Active.Count; i++)
                {
                    var fx = data.Active[i];
                    if (fx.RemainingTicks <= 0) continue;
                    flags |= FlagForKey(fx.Key);
                }
                _ctx.StatusFlags.Set(kv.Key, flags);
            }
        }

        static StatusFlags FlagForKey(string key)
        {
            switch (key)
            {
                case "Paralyze":           return DaggerfallWorkshop.Sim.StatusFlags.Paralyzed;
                case "Silence":            return DaggerfallWorkshop.Sim.StatusFlags.Silenced;
                case "Blind":              return DaggerfallWorkshop.Sim.StatusFlags.Blind;
                case "Invisibility":
                case "ChameleonNormal":
                case "ChameleonTrue":      return DaggerfallWorkshop.Sim.StatusFlags.Invisible;
                case "Levitate":           return DaggerfallWorkshop.Sim.StatusFlags.Levitating;
                case "WaterWalking":       return DaggerfallWorkshop.Sim.StatusFlags.WaterWalk;
                case "WaterBreathing":     return DaggerfallWorkshop.Sim.StatusFlags.WaterBreath;
                case "FreeAction":         return DaggerfallWorkshop.Sim.StatusFlags.FreeAction;
                case "OpenLock":           return DaggerfallWorkshop.Sim.StatusFlags.OpenLock;
                default:                   return DaggerfallWorkshop.Sim.StatusFlags.None;
            }
        }
    }
}
