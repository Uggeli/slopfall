using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Per-entity boolean status, derived each tick from EffectsRegistry by
    /// StatusFlagDeriveSystem. Flag bits stay stable across ticks so AI /
    /// rendering can read them without juddering when an effect adds and
    /// re-adds the same status.
    [Flags]
    public enum StatusFlags
    {
        None        = 0,
        Paralyzed   = 1 << 0,
        Silenced    = 1 << 1,
        Blind       = 1 << 2,
        Invisible   = 1 << 3,
        Levitating  = 1 << 4,
        WaterWalk   = 1 << 5,
        WaterBreath = 1 << 6,
        FreeAction  = 1 << 7,
        OpenLock    = 1 << 8,
    }
}
