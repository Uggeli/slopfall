using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>How far an utterance carries — hearing-radius tiers (smallest → largest). The speech
    /// noise tiers of the general noise model (P3 moves the radius onto ActivitySpec.NoiseLevel).</summary>
    public enum CommChannel { Whisper, Talk, Shout }

    /// <summary>The speech-act family — determines how reception routes the content (Searle).</summary>
    public enum SpeechAct { Inform, Request, Offer, Express }

    /// <summary>One thing an agent says: a typed speech-act carrying an atom-bag payload on a channel.
    /// <see cref="Audience"/> is who the speaker AIMS at (None = undirected) — NOT a reach gate;
    /// reception is the hearing radius, so any agent in range overhears (eavesdrops).</summary>
    public struct Utterance : IEvent
    {
        public EntityId Speaker, Audience;
        public CommChannel Channel;
        public SpeechAct Act;
        public AtomBag Content;
        public Fixed Confidence;
    }

    /// <summary>The hearing sense's output: agent <see cref="Hearer"/> perceived this utterance.</summary>
    public struct HeardUtterance : IEvent { public EntityId Hearer; public Utterance Said; }

    /// <summary>
    /// The hearing raw-sense, beside SenseSystem's sight: for each Utterance this tick, every agent
    /// within the channel's audible radius hears it (addressee + eavesdroppers), never the speaker.
    /// LOS-relaxed — sound rounds corners. Utterances are sparse, so a linear scan per utterance is
    /// fine for now (bucket it if speech gets chatty). Emits HeardUtterance; routing is reception's job.
    /// </summary>
    public sealed class HearingSystem : SimSystem
    {
        readonly BehaviorRegistry _behavior;
        readonly PositionRegistry _position;

        // Audible radii by channel (world units). Config — tuned later; P3 sources this from NoiseLevel.
        const float WhisperRadius = 3f, TalkRadius = 8f, ShoutRadius = 25f;

        public HearingSystem(EventBus events, BehaviorRegistry behavior, PositionRegistry position) : base(events)
        { _behavior = behavior; _position = position; }

        public override void Update(long tick)
        {
            var said = Events.GetEvents<Utterance>();
            for (int i = 0; i < said.Length; i++)
            {
                var u = said[i];
                if (!_position.TryGet(u.Speaker, out var sp) || sp == null) continue;
                float r = Radius(u.Channel), r2 = r * r;
                foreach (var kv in _behavior.All)
                {
                    var hearer = kv.Key;
                    if (hearer == u.Speaker) continue;                              // never hear yourself
                    if (!_position.TryGet(hearer, out var hp) || hp == null) continue;
                    float dx = hp.X - sp.X, dz = hp.Z - sp.Z;
                    if (dx * dx + dz * dz > r2) continue;                           // out of audible range
                    Events.Publish(new HeardUtterance { Hearer = hearer, Said = u });
                }
            }
        }

        static float Radius(CommChannel ch)
        {
            switch (ch)
            {
                case CommChannel.Whisper: return WhisperRadius;
                case CommChannel.Shout: return ShoutRadius;
                default: return TalkRadius;
            }
        }
    }
}
