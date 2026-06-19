using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the old DaggerfallWorkshop.Sim.SkillAdvancementSystem.
    // Accumulates skill exp on SkillUsedEvent and promotes a skill when it reaches the
    // per-tier threshold. Emits a StatsSetIntent (StatsRegistry is the sole applier) and
    // a SkillAdvancedEvent per promotion.
    //
    // DFU's classic advancement is exponential: each skill point requires
    // (CurrentValue + 1) * AdvancementMultiplier exp. We replicate that with a single
    // multiplier constant.
    //
    // Old (spool + apply each event in order) → new (GetEvents): under double-buffered
    // events every SkillUsedEvent reads the SAME settled StatsData, so to preserve the
    // old order-sensitive accumulation (a second use of a skill this tick saw the first
    // use's added exp) we fold all of this tick's uses per entity through one working
    // StatsData copy, then emit a single StatsSetIntent per touched entity. Promotion
    // SkillAdvancedEvents fire exactly as before, in use order.
    public sealed class SkillAdvancementSystem : SimSystem
    {
        const int AdvancementMultiplier = 35;

        readonly StatsRegistry _stats;

        public SkillAdvancementSystem(EventBus events, StatsRegistry stats) : base(events)
        {
            _stats = stats;
        }

        public override void Update(long tick)
        {
            var uses = Events.GetEvents<SkillUsedEvent>();
            if (uses.Length == 0) return;

            var work = new Dictionary<EntityId, StatsData>();

            for (int u = 0; u < uses.Length; u++)
            {
                var e = uses[u];
                if (e.Magnitude <= 0) continue;
                if (string.IsNullOrEmpty(e.Skill)) continue;
                if (!TryWorking(work, e.Entity, out var stats) || stats == null) continue;

                int current = stats.Skills.TryGetValue(e.Skill, out var cv) ? cv : 0;
                int exp = stats.SkillExp.TryGetValue(e.Skill, out var ev) ? ev : 0;
                exp += e.Magnitude;

                int next = (current + 1) * AdvancementMultiplier;
                int promotions = 0;
                while (exp >= next)
                {
                    exp -= next;
                    current++;
                    promotions++;
                    next = (current + 1) * AdvancementMultiplier;
                }

                stats.Skills[e.Skill] = current;
                stats.SkillExp[e.Skill] = exp;

                for (int i = 0; i < promotions; i++)
                    Events.Publish(new SkillAdvancedEvent { Entity = e.Entity, Skill = e.Skill, NewValue = current - (promotions - 1 - i) });
            }

            foreach (var kv in work)
                Events.Publish(new StatsSetIntent { Id = kv.Key, Data = kv.Value });
        }

        // Returns a mutable working copy for the entity, cloned from the settled stats on
        // first touch (so the per-tick accumulation lands in one set). False (null) if the
        // entity has no stats row.
        bool TryWorking(Dictionary<EntityId, StatsData> work, EntityId id, out StatsData stats)
        {
            if (work.TryGetValue(id, out stats)) return stats != null;
            if (!_stats.TryGet(id, out var stored) || stored == null) { stats = null; return false; }
            stats = CloneStats(stored);
            work[id] = stats;
            return true;
        }

        static StatsData CloneStats(StatsData src)
        {
            var copy = new StatsData();
            for (int i = 0; i < src.Stats.Length && i < copy.Stats.Length; i++)
                copy.Stats[i] = src.Stats[i];
            foreach (var kv in src.Skills)
                copy.Skills[kv.Key] = kv.Value;
            foreach (var kv in src.SkillExp)
                copy.SkillExp[kv.Key] = kv.Value;
            return copy;
        }
    }
}
