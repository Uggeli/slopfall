namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>
    /// Asleep consolidation trigger. Stateless: a sleeping agent is "due" on a staggered cadence
    /// (pure function of tick + id); when due it emits a MemoryConsolidateIntent the
    /// AgentMemoryRegistry applies (RE-DIFF/MINT/DECAY). Decay is thus sleep-gated — awake memory
    /// never fades, and population-staggered sleep amortizes the cost.
    /// </summary>
    public sealed class ConsolidationSystem : SimSystem
    {
        readonly BehaviorRegistry _behavior;
        readonly AgentMemoryConfig _cfg;

        public ConsolidationSystem(EventBus events, BehaviorRegistry behavior, AgentMemoryConfig cfg)
            : base(events) { _behavior = behavior; _cfg = cfg; }

        public override void Update(long tick)
        {
            int cadence = _cfg.ConsolidateCadenceTicks;
            foreach (var kv in _behavior.All)
            {
                if (kv.Value == null || kv.Value.Activity != ActivityKind.Sleep) continue;
                long phase = (uint)kv.Key.Value % (uint)cadence;
                if ((tick + phase) % cadence != 0) continue;
                Events.Publish(new MemoryConsolidateIntent { Agent = kv.Key });
            }
        }
    }
}
