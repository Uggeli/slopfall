namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the old DaggerfallWorkshop.Sim.StatusFlagDeriveSystem.
    // Reads the Engine EffectsRegistry read-only, derives the per-entity StatusFlags
    // word, and emits StatusFlagsSetIntent (the StatusFlagsRegistry is the sole
    // applier). Reuses the StatusFlags enum from the parent namespace unqualified.
    //
    // Clear-on-empty: the registry no longer exposes Remove, so when an entity has no
    // active effects we emit StatusFlags.None instead of the old Remove call.

    /// Walks each entity's active effects and derives StatusFlags. Effect-key → flag
    /// mapping is hardcoded; keys that don't map to a flag are ignored.
    public sealed class StatusFlagDeriveSystem : SimSystem
    {
        readonly EffectsRegistry _effects;

        public StatusFlagDeriveSystem(EventBus events, EffectsRegistry effects) : base(events)
        {
            _effects = effects;
        }

        public override void Update(long tick)
        {
            foreach (var kv in _effects.All)
            {
                var data = kv.Value;
                if (data == null || data.Active == null || data.Active.Count == 0)
                {
                    // Clear-on-empty: emit None (no Remove available).
                    Events.Publish(new StatusFlagsSetIntent { Id = kv.Key, Flags = StatusFlags.None });
                    continue;
                }

                StatusFlags flags = StatusFlags.None;
                for (int i = 0; i < data.Active.Count; i++)
                {
                    var fx = data.Active[i];
                    if (fx.RemainingTicks <= 0) continue;
                    flags |= FlagForKey(fx.Key);
                }
                Events.Publish(new StatusFlagsSetIntent { Id = kv.Key, Flags = flags });
            }
        }

        static StatusFlags FlagForKey(string key)
        {
            switch (key)
            {
                case "Paralyze":           return StatusFlags.Paralyzed;
                case "Silence":            return StatusFlags.Silenced;
                case "Blind":              return StatusFlags.Blind;
                case "Invisibility":
                case "ChameleonNormal":
                case "ChameleonTrue":      return StatusFlags.Invisible;
                case "Levitate":           return StatusFlags.Levitating;
                case "WaterWalking":       return StatusFlags.WaterWalk;
                case "WaterBreathing":     return StatusFlags.WaterBreath;
                case "FreeAction":         return StatusFlags.FreeAction;
                case "OpenLock":           return StatusFlags.OpenLock;
                default:                   return StatusFlags.None;
            }
        }
    }
}
