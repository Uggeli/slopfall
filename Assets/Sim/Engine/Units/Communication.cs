using System.Collections.Generic;
using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>How far an utterance carries — hearing-radius tiers (smallest → largest). The speech
    /// noise tiers of the general noise model (P3 moves the radius onto ActivitySpec.NoiseLevel).</summary>
    public enum CommChannel { Whisper, Talk, Shout }

    /// <summary>The speech-act family — determines how reception routes the content (Searle).</summary>
    public enum SpeechAct { Inform, Request, Offer, Express }

    /// <summary>Shared communication helpers — the channel↔noise unification (P3). A channel is just a
    /// speech NOISE TIER; the hearing sense derives its audible radius from that noise level, so speech
    /// and (later) any noisy action share one audibility model (spec decision 13).</summary>
    public static class Communication
    {
        /// <summary>Loudness ∈[0,1] of a speech channel — the speech tiers of the general noise model.
        /// Whisper barely carries; Shout fills the square. The tier values are chosen so HearingSystem's
        /// NoiseToRadius reproduces the P1 effective radii (~3 / 8 / 25) — the danger soak isn't perturbed.</summary>
        public static double NoiseOf(CommChannel ch)
        {
            switch (ch)
            {
                case CommChannel.Whisper: return 0.15;
                case CommChannel.Shout:   return 0.85;
                default:                  return 0.45;   // Talk — "talk-ish"
            }
        }
    }

    /// <summary>One thing an agent says: a typed speech-act carrying an atom-bag payload on a channel.
    /// <see cref="Audience"/> is who the speaker AIMS at (None = undirected) — NOT a reach gate;
    /// reception is the hearing radius, so any agent in range overhears (eavesdrops).</summary>
    public struct Utterance : IEvent
    {
        public EntityId Speaker, Audience;
        public CommChannel Channel;
        public SpeechAct Act;
        public int SubjectBuilding;   // the place the content is ABOUT (for place facts); <0 = none
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

        // Noise→radius calibration (P3): radius = MinRadius·e^(Steepness·noise). Tuned so the speech
        // tiers (Whisper 0.15, Talk 0.45, Shout 0.85; Communication.NoiseOf) reproduce the P1 effective
        // radii ~3 / 8 / 25 — keeping P2's danger soak unperturbed while unifying audibility on NoiseLevel.
        const float MinRadius = 1.905f, Steepness = 3.03f;

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

        /// <summary>An utterance's audible radius now derives from its channel's noise level — the
        /// channel↔noise unification (P3). Non-speech noisy actions can later feed the same helper.</summary>
        static float Radius(CommChannel ch) => NoiseToRadius(Communication.NoiseOf(ch));

        /// <summary>Map a loudness ∈[0,1] to an audible radius (world units). Monotone in noise —
        /// a louder source reaches farther. The single audibility curve for speech and (later) any sound.</summary>
        public static float NoiseToRadius(double noise)
        {
            if (noise < 0) noise = 0; else if (noise > 1) noise = 1;
            return (float)(MinRadius * System.Math.Exp(Steepness * noise));
        }
    }

    /// <summary>
    /// Reception router: dispatches each HeardUtterance by act. P2 handles Inform of PLACE facts —
    /// a relayed place atom becomes a SECOND-HAND PlaceObserveIntent for the hearer, its strength
    /// scaled by belief = trust(hearer→speaker) × the speaker's confidence. Trust is floored so even a
    /// stranger partly heeds a shouted warning. (Request/Offer/Express and entity-fact relay are later
    /// handlers on this same router.)
    /// </summary>
    public sealed class CommunicationSystem : SimSystem
    {
        readonly RelationsRegistry _relations;

        public CommunicationSystem(EventBus events, RelationsRegistry relations) : base(events)
        { _relations = relations; }

        public override void Update(long tick)
        {
            var heard = Events.GetEvents<HeardUtterance>();
            for (int i = 0; i < heard.Length; i++)
            {
                var u = heard[i].Said;
                if (u.Act != SpeechAct.Inform || u.SubjectBuilding < 0 || u.Content == null) continue;
                Fixed belief = BeliefScale(heard[i].Hearer, u.Speaker, u.Confidence);
                var atoms = u.Content.Atoms;
                for (int k = 0; k < atoms.Count; k++)
                {
                    if (!AtomCatalog.For(atoms[k].Type).Shareable) continue;   // P2: only PLACE facts relay
                    Events.Publish(new PlaceObserveIntent
                    {
                        Agent = heard[i].Hearer, Building = u.SubjectBuilding, Atom = atoms[k].Type,
                        Value = atoms[k].Value, SecondHand = true, TrustScale = belief
                    });
                }
            }
        }

        /// belief = clamp(0.5 + 0.5·regard, 0.1, 1) × confidence — strangers half-heed, friends fully,
        /// enemies barely; a confident teller is believed more.
        Fixed BeliefScale(EntityId hearer, EntityId speaker, Fixed confidence)
        {
            double regard = 0.0;
            if (_relations.TryGet(hearer, out var rd) && rd != null && rd.Of != null
                && rd.Of.TryGetValue(speaker, out var r) && r != null) regard = r.Regard;
            double t = 0.5 + 0.5 * regard;
            if (t < 0.1) t = 0.1; else if (t > 1.0) t = 1.0;
            double conf = confidence.ToDouble();
            if (conf < 0) conf = 0; else if (conf > 1) conf = 1;
            return Fixed.FromDouble(t * conf);
        }
    }

    /// <summary>
    /// READ-ONLY diagnostic accumulator (like MetricsSystem) that bridges the sim's tick rate to the
    /// slower snapshot pump. The pump publishes at ~5 Hz but the sim ticks far faster and the EventBus
    /// flips every tick (each tick's GetEvents&lt;Utterance&gt;() holds ONLY that tick's emissions), so
    /// reading the bus once per publish would drop almost everything. This registry appends EVERY tick's
    /// utterances to a bounded buffer; WorldRunner.Build drains+clears it into each frame. It NEVER
    /// publishes intents or mutates sim state, so the determinism fingerprint is unchanged.
    ///
    /// <para>Thread-safety: in the web host, Build() (which calls Drain) and Step() (which runs this
    /// registry's Update) both run on the SOLE sim thread (WorldRunner.Loop), so they never overlap.
    /// We still lock the buffer — it is shared mutable state, the no-overlap guarantee is subtle, and a
    /// future caller draining from another thread (or the run-flat-out path publishing between steps)
    /// must stay safe. The lock is uncontended on the hot path, so it costs nothing.</para>
    /// </summary>
    public sealed class UtteranceLogRegistry : Registry
    {
        /// Default cap: a few hundred utterances is far more than one ~5 Hz frame's worth even when the
        /// town is chatty; oldest fall off if the pump ever stalls. Overflow is logged once-ish below.
        const int DefaultCapacity = 512;

        readonly object _lock = new object();
        readonly Queue<Utterance> _buffer;
        readonly int _capacity;
        bool _warnedOverflow;

        public UtteranceLogRegistry(EventBus events, int capacity = DefaultCapacity) : base(events)
        {
            _capacity = capacity < 1 ? 1 : capacity;
            _buffer = new Queue<Utterance>(_capacity);
        }

        public override void Update(long tick)
        {
            var said = Events.GetEvents<Utterance>();
            if (said.Length == 0) return;
            lock (_lock)
            {
                for (int i = 0; i < said.Length; i++)
                {
                    if (_buffer.Count >= _capacity)
                    {
                        _buffer.Dequeue();   // drop oldest
                        if (!_warnedOverflow)
                        {
                            _warnedOverflow = true;
                            System.Console.WriteLine(
                                $"[UtteranceLog] buffer overflow at tick {tick} (cap {_capacity}) — dropping oldest; the pump may be stalled.");
                        }
                    }
                    _buffer.Enqueue(said[i]);
                }
            }
        }

        /// <summary>Return + CLEAR every buffered utterance (oldest → newest). Called once per published
        /// frame by WorldRunner.Build, so a frame carries exactly what was spoken since the last drain.</summary>
        public List<Utterance> Drain()
        {
            lock (_lock)
            {
                if (_buffer.Count == 0) return EmptyDrain;
                var outList = new List<Utterance>(_buffer.Count);
                outList.AddRange(_buffer);
                _buffer.Clear();
                return outList;
            }
        }

        static readonly List<Utterance> EmptyDrain = new List<Utterance>();

        /// <summary>An utterance's atom-bag content as a flat list of (atom-type-id, value) pairs — the
        /// wire shape for the snapshot frame. Sorted ascending by atom type (AtomBag's invariant); a
        /// null/empty bag yields an empty list. Pure helper so the wire shape is unit-testable.</summary>
        public static List<(int atom, double value)> ToFlatContent(AtomBag bag)
        {
            if (bag == null || bag.Count == 0) return new List<(int, double)>();
            var flat = new List<(int, double)>(bag.Count);
            var atoms = bag.Atoms;
            for (int i = 0; i < atoms.Count; i++)
                flat.Add((atoms[i].Type.Value, atoms[i].Value.ToDouble()));
            return flat;
        }
    }
}
