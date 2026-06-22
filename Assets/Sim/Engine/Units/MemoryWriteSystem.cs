using System.Collections.Generic;
using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>
    /// Awake perception → memory. Stateless: each agent is "due" on a staggered cadence (a pure
    /// function of tick + agent id, no per-agent timer). When due, it reads the agent's top-K sensed
    /// entities, splits each perceived bag into a signature (identity atoms, for recognition) and a
    /// full percept (for surprise), and emits a MemoryPerceiveIntent the AgentMemoryRegistry applies.
    /// </summary>
    public sealed class MemoryWriteSystem : SimSystem
    {
        readonly SensedRegistry _sensed;
        readonly PerceivableRegistry _perceivable;
        readonly AgentMemoryConfig _cfg;

        public MemoryWriteSystem(EventBus events, SensedRegistry sensed, PerceivableRegistry perceivable, AgentMemoryConfig cfg)
            : base(events) { _sensed = sensed; _perceivable = perceivable; _cfg = cfg; }

        public override void Update(long tick)
        {
            int cadence = _cfg.PerceiveCadenceTicks;
            foreach (var kv in _sensed.All)
            {
                EntityId agent = kv.Key;
                long phase = (uint)agent.Value % (uint)cadence;
                if ((tick + phase) % cadence != 0) continue;   // not this agent's turn

                List<EntityId> seen = kv.Value;
                if (seen == null) continue;
                int k = seen.Count < _cfg.AttentionK ? seen.Count : _cfg.AttentionK;
                for (int i = 0; i < k; i++)
                {
                    EntityId other = seen[i];
                    // THINGS = entity dossiers, keyed on IDENTITY: categories must form on the
                    // signature (kind/race/role), not identity+activity, or minted prototypes carry
                    // the transient activity atom and recognition (by identity) never matches. The
                    // activity (what they're DOING) belongs in EVENTS — deferred.
                    AtomBag signature = _perceivable.Signature(other);
                    if (signature.Count == 0) continue;        // no identity atoms (e.g. a monster in v1)
                    Events.Publish(new MemoryPerceiveIntent
                    {
                        Perceiver = agent, Perceived = other,
                        Signature = signature, Percept = signature, Arousal = Fixed.Zero,
                    });
                }
            }
        }
    }
}
