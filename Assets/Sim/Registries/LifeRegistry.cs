using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// One agent's place on the lifespan curve (L2 — docs/living_world_L2_lifecycle.md).
    /// Atoms' entry→persist→decay→exit, the exit clock: age advances each game-year
    /// and the mortality hazard rises past LifespanYears until the agent dies of age.
    public sealed class LifeData
    {
        public double AgeYears;
        public double LifespanYears;
    }
}
