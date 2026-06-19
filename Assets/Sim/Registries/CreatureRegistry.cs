using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Per-creature state for the wilderness/threat layer (V2a). A creature is an
    /// entity with Identity.Kind == EnemyMonster: it carries Position + Vitals like
    /// anyone, but no Needs/Residency/Coin (it doesn't shop, sleep, or socialize),
    /// so the civilian systems that walk those registries skip it. CreatureSystem
    /// is the sole writer here and of creature Position; it wanders them and emits
    /// DamageEvent when one reaches a civilian. The membrane reads a creature as a
    /// threat (SubjectiveSystem.Interpret), which is what gives fear a target (V2b).
    public struct CreatureData
    {
        public float TargetX, TargetZ;   // current wander destination
        public long NextAttackTick;      // attack cooldown gate
        public EntityId LastAttacker;    // who last struck it — retaliation target (V2b combat)
        public long LastStruckTick;      // when, so the grudge lapses
    }
}
