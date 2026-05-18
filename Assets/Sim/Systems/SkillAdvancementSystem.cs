using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Accumulates skill exp on SkillUsedEvent and promotes a skill when it
    /// reaches the per-tier threshold. Emits SkillAdvancedEvent on each
    /// promotion. Sole writer of StatsRegistry.Skills / SkillExp.
    ///
    /// DFU's classic advancement is exponential: each skill point requires
    /// (CurrentValue + 1) * AdvancementMultiplier exp. We replicate that with
    /// a single multiplier constant — refined as real skill uses port over.
    public sealed class SkillAdvancementSystem : ISystem
    {
        const int AdvancementMultiplier = 35;

        SimulationContext _ctx;
        readonly List<SkillUsedEvent> _pending = new List<SkillUsedEvent>();

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
            ctx.Events.Subscribe<SkillUsedEvent>(e => _pending.Add(e));
        }

        public void ProcessEvents()
        {
            for (int i = 0; i < _pending.Count; i++)
                Apply(_pending[i]);
            _pending.Clear();
        }

        public void Update(long tick) { }

        void Apply(SkillUsedEvent e)
        {
            if (e.Magnitude <= 0) return;
            if (!_ctx.Stats.TryGet(e.Entity, out var stats) || stats == null) return;
            if (string.IsNullOrEmpty(e.Skill)) return;

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

            var newStats = CloneStats(stats);
            newStats.Skills[e.Skill] = current;
            newStats.SkillExp[e.Skill] = exp;
            _ctx.Stats.Set(e.Entity, newStats);

            for (int i = 0; i < promotions; i++)
                _ctx.Events.Emit(new SkillAdvancedEvent { Entity = e.Entity, Skill = e.Skill, NewValue = current - (promotions - 1 - i) });
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
