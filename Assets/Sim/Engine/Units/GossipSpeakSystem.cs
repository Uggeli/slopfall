using System.Collections.Generic;
using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>
    /// The speaking half of the Gossip activity (P3): each agent that is <see cref="ActivityKind.Gossip"/>
    /// and Doing emits, on a staggered turn cadence, ONE Inform(Talk) utterance carrying a single PLACE
    /// fact drawn from its OWN memory — so agents voluntarily spread BOTH danger and provisions knowledge
    /// (and re-spread rumours they were told). HearingSystem (P1) delivers each turn to partners +
    /// eavesdroppers in earshot, and CommunicationSystem (P2) writes it second-hand into their memory:
    /// gossip needs only to make agents TALK; reception already exists.
    ///
    /// <para>Determinism: no shared RNG. The turn cadence is phased per agent by hash(0, id) so speakers
    /// don't all fire on the same tick, and hash(tick, id) picks which fact when an agent knows several —
    /// the same hash family OddSystem uses. The system only READS settled state and publishes events.</para>
    /// </summary>
    public sealed class GossipSpeakSystem : SimSystem
    {
        readonly BehaviorRegistry _behavior;
        readonly AgentMemoryRegistry _memory;

        // One gossip turn per agent roughly each game-minute (600 ticks/min); cheap and chatty enough
        // to spread facts without flooding the bus. Each agent's window is phased so turns stagger.
        const long TurnCadenceTicks = 600;

        public GossipSpeakSystem(EventBus events, BehaviorRegistry behavior, AgentMemoryRegistry memory) : base(events)
        { _behavior = behavior; _memory = memory; }

        public override void Update(long tick)
        {
            foreach (var kv in _behavior.All)
            {
                var bd = kv.Value;
                if (bd == null || bd.Activity != ActivityKind.Gossip || bd.Phase != ActivityPhase.Doing) continue;

                var id = kv.Key;
                // Staggered cadence: this agent takes a turn only on its phased tick in the window.
                if ((tick + (Hash(id.Value, 0) % TurnCadenceTicks)) % TurnCadenceTicks != 0) continue;

                if (!_memory.TryGet(id, out var mem) || mem == null) continue;
                if (!PickShareableFact(mem, tick, id.Value, out int building, out Atom atom)) continue;

                Events.Publish(new Utterance
                {
                    Speaker = id,
                    Audience = EntityId.None,
                    Channel = CommChannel.Talk,
                    Act = SpeechAct.Inform,
                    SubjectBuilding = building,
                    Content = AtomBag.Create(new[] { atom }),
                    Confidence = Fixed.One,
                });
            }
        }

        /// <summary>Choose one shareable PLACE fact (a Danger or Provisions atom) from the agent's own
        /// PLACES store; hash(tick,id) picks deterministically among them. Returns false if the agent
        /// knows no shareable place fact (then it stays quiet).</summary>
        static bool PickShareableFact(AgentMemory mem, long tick, int idValue, out int building, out Atom atom)
        {
            building = -1; atom = default;
            var places = mem.Stores.Places;
            // Two cheap passes (places.Count ≤ 128): count candidates, then index the hash-chosen one —
            // no allocation, fully deterministic.
            int count = 0;
            for (int pi = 0; pi < places.Count; pi++)
            {
                var rec = places[pi];
                var atoms = rec.DeltaBag.Atoms;
                for (int ai = 0; ai < atoms.Count; ai++)
                    if (IsShareable(atoms[ai].Type)) count++;
            }
            if (count == 0) return false;

            int pick = (int)(Hash(idValue, tick) % (uint)count);
            int seen = 0;
            for (int pi = 0; pi < places.Count; pi++)
            {
                var rec = places[pi];
                var atoms = rec.DeltaBag.Atoms;
                for (int ai = 0; ai < atoms.Count; ai++)
                {
                    if (!IsShareable(atoms[ai].Type)) continue;
                    if (seen == pick)
                    {
                        building = (int)rec.Key.Value;
                        atom = atoms[ai];
                        return true;
                    }
                    seen++;
                }
            }
            return false;
        }

        static bool IsShareable(AtomTypeId t) => t == PlaceAtoms.Danger || t == PlaceAtoms.Provisions;

        // The same hash family OddSystem/SenseSystem draw stochastic choices from — never a shared RNG.
        static uint Hash(int idValue, long tick)
        {
            uint x = (uint)(idValue * 2654435761u) ^ (uint)(tick * 40503u);
            x ^= x >> 13; x *= 0x5bd1e995; x ^= x >> 15;
            return x;
        }
    }
}
