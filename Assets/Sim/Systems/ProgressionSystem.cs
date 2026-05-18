using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Tracks skill-advancements-since-last-level and promotes entity level
    /// once the threshold is reached. Sole writer of ProgressionRegistry;
    /// emits LevelUpEvent on each promotion.
    ///
    /// Classic DFU advancement requires per-level skill increments scaling
    /// with current level. We approximate that with a simple linear ramp:
    ///   next_level_at = level * 15 skill points
    /// — refined as the real progression math ports over.
    public sealed class ProgressionSystem : ISystem
    {
        SimulationContext _ctx;
        readonly List<SkillAdvancedEvent> _pending = new List<SkillAdvancedEvent>();

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
            ctx.Events.Subscribe<SkillAdvancedEvent>(e => _pending.Add(e));
        }

        public void ProcessEvents()
        {
            for (int i = 0; i < _pending.Count; i++)
                Apply(_pending[i]);
            _pending.Clear();
        }

        public void Update(long tick) { }

        void Apply(SkillAdvancedEvent e)
        {
            ProgressionData prog;
            if (!_ctx.Progression.TryGet(e.Entity, out prog) || prog == null)
                prog = new ProgressionData { Level = 1, SkillPointsThisLevel = 0 };

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

            _ctx.Progression.Set(e.Entity, new ProgressionData
            {
                Level = level,
                SkillPointsThisLevel = points,
            });

            for (int i = 0; i < promotions; i++)
                _ctx.Events.Emit(new LevelUpEvent { Entity = e.Entity, NewLevel = level - (promotions - 1 - i) });
        }
    }
}
