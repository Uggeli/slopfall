using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the old DaggerfallWorkshop.Sim.ProgressionSystem. Tracks
    // skill-advancements-since-last-level and promotes entity level once the threshold is
    // reached. Emits a ProgressionSetIntent (ProgressionRegistry is the sole applier), an
    // IdentitySetIntent mirroring the new Level into the identity row on a level-up, and a
    // LevelUpEvent per promotion.
    //
    // Classic DFU advancement requires per-level skill increments scaling with current
    // level. We approximate with a simple linear ramp: next_level_at = level * 15 skill
    // points.
    //
    // Old (spool + apply each SkillAdvancedEvent in order) → new (GetEvents): under
    // double-buffered events every SkillAdvancedEvent reads the SAME settled
    // ProgressionData, so to preserve the old order-sensitive accumulation (each advance
    // added one point on top of the prior advance this tick) we fold all of an entity's
    // advances through one working ProgressionData, then emit a single ProgressionSetIntent
    // per touched entity. LevelUpEvents fire as before, in advance order. The
    // Identity.Level mirror is written once per touched entity that crossed a level
    // (whole-row replacement, the one-directional cross-registry write the old
    // SyncIdentityLevel performed).
    public sealed class ProgressionSystem : SimSystem
    {
        readonly ProgressionRegistry _progression;
        readonly IdentityRegistry _identity;

        public ProgressionSystem(EventBus events, ProgressionRegistry progression, IdentityRegistry identity) : base(events)
        {
            _progression = progression;
            _identity = identity;
        }

        public override void Update(long tick)
        {
            var advances = Events.GetEvents<SkillAdvancedEvent>();
            if (advances.Length == 0) return;

            var work = new Dictionary<EntityId, ProgressionData>();
            var leveled = new HashSet<EntityId>();   // entities that crossed a level this tick

            for (int a = 0; a < advances.Length; a++)
            {
                var e = advances[a];

                if (!work.TryGetValue(e.Entity, out var prog) || prog == null)
                {
                    if (!_progression.TryGet(e.Entity, out prog) || prog == null)
                        prog = new ProgressionData { Level = 1, SkillPointsThisLevel = 0 };
                    else
                        prog = new ProgressionData { Level = prog.Level, SkillPointsThisLevel = prog.SkillPointsThisLevel };
                    work[e.Entity] = prog;
                }

                int level = prog.Level;
                int points = prog.SkillPointsThisLevel + 1;
                int promotions = 0;

                int threshold = level * 15;
                while (points >= threshold)
                {
                    points -= threshold;
                    level++;
                    promotions++;
                    threshold = level * 15;
                }

                prog.Level = level;
                prog.SkillPointsThisLevel = points;

                if (promotions > 0)
                    leveled.Add(e.Entity);

                for (int i = 0; i < promotions; i++)
                    Events.Publish(new LevelUpEvent { Entity = e.Entity, NewLevel = level - (promotions - 1 - i) });
            }

            foreach (var kv in work)
            {
                Events.Publish(new ProgressionSetIntent { Id = kv.Key, Data = kv.Value });
                if (leveled.Contains(kv.Key))
                    SyncIdentityLevel(kv.Key, kv.Value.Level);
            }
        }

        /// One-directional cross-registry write promised by ProgressionRegistry's
        /// contract: Identity.Level mirrors ProgressionData.Level after a level-up.
        /// Whole-row replacement, emitted as an IdentitySetIntent (IdentityRegistry is the
        /// sole applier). Reads the settled identity row; skips if the entity has none.
        void SyncIdentityLevel(EntityId entity, int level)
        {
            if (!_identity.TryGet(entity, out var identity) || identity == null) return;
            Events.Publish(new IdentitySetIntent
            {
                Id = entity,
                Data = new IdentityData
                {
                    Name        = identity.Name,
                    Kind        = identity.Kind,
                    Race        = identity.Race,
                    Gender      = identity.Gender,
                    CareerIndex = identity.CareerIndex,
                    Level       = level,
                    FactionId   = identity.FactionId,
                    Team        = identity.Team,
                },
            });
        }
    }
}
