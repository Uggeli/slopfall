using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// The membrane (Atoms row 2): turns raw senses (SensedRegistry) into a
    /// per-agent SubjectiveView — recognition / valence / trust / attention for
    /// each perceived entity — the single interpretation surface the decider and
    /// the interrupt layer both read. Replaces PerceptionSystem: greeting and
    /// dislike now fall out of the interpreted reads rather than bespoke regard
    /// thresholds. Sole writer of SubjectiveViewRegistry.
    ///
    /// interpret() is also exposed statically so the decider can read an entity
    /// it isn't sensing (a place's occupants). S1 sources valence from the dossier
    /// (RelationsRegistry); S2 adds affect to attention; S3 sources from MEANINGS.
    /// See docs/cognitive_substrate_S1_membrane.md.
    public sealed class SubjectiveSystem : ISystem
    {
        const int SenseEveryTicks = 5;          // aligned with SenseSystem
        const int MaxReads = 8;                 // capacity-limited attention (top-K)
        const double GreetCooldownGameMinutes = 90;
        const double DislikeCooldownGameMinutes = 30;
        const double GreetValenceBar = 0.3;
        const double GreetRecognitionBar = 0.25;
        const double DislikeValenceBar = -0.3;

        SimulationContext _ctx;
        readonly Dictionary<EntityId, double> _nextGreetAt = new Dictionary<EntityId, double>();
        readonly Dictionary<EntityId, double> _nextDislikeAt = new Dictionary<EntityId, double>();
        double _gameMinutes;

        public void Init(SimulationContext ctx) { _ctx = ctx; }
        public void ProcessEvents() { }

        /// interpret(self, other): what `other` means to `self` right now. A pure
        /// read, callable for sensed OR remembered entities. S1: valence is the
        /// dossier regard, recognition/trust the familiarity, attention a simple
        /// salience. (S2 adds affect to attention; S3 makes valence a MEANINGS
        /// lookup adjusted by the per-entity dossier delta.)
        public static EntityRead Interpret(SimulationContext ctx, EntityId self, EntityId other)
        {
            double regard = 0, familiarity = 0;
            if (ctx.Relations.TryGet(self, out var rels) && rels.Of.TryGetValue(other, out var rel))
            {
                regard = rel.Regard;
                familiarity = rel.Familiarity;
            }
            // Chronic opinion (dossier) + acute feeling (Affects, S2): "what I
            // think of you" plus "how I feel about you right now" (the latter
            // fading). Emotion-as-controller — this valence colors the decider's
            // place-lens and the greet/dislike percepts.
            double valence = regard + ctx.Affects.ValenceToward(self, other);
            return new EntityRead
            {
                Other = other,
                Valence = valence,
                Recognition = familiarity,
                Trust = familiarity,
                Attention = 0.1 + System.Math.Abs(valence) + familiarity,
            };
        }

        public void Update(long tick)
        {
            var clock = _ctx.WorldClock.Current;
            if (clock.Year == 0) return;
            double dt = _ctx.Time.TickIntervalSeconds * clock.TimeScale / 60.0;
            if (dt <= 0) return;
            _gameMinutes += dt;

            if (tick % SenseEveryTicks != 0) return;   // SenseSystem just refreshed senses

            foreach (var kv in _ctx.Sensed.All)
            {
                var self = kv.Key;
                var sensed = kv.Value;
                if (sensed.Count == 0) { _ctx.Subjective.Remove(self); continue; }

                var view = new SubjectiveViewData();
                for (int i = 0; i < sensed.Count; i++)
                {
                    var read = Interpret(_ctx, self, sensed[i]);
                    view.Entities.Add(read);
                    InterpretPercepts(self, read);     // greet/dislike fall out of the read
                }
                TrimToTopK(view.Entities);
                _ctx.Subjective.Set(self, view);
            }
        }

        /// Greet a recognized, well-regarded other (mutual — the lower id drives,
        /// so the pair greets once); flinch from a strongly-disliked one (directed
        /// — each observer checks its own read). Both derived from interpret().
        void InterpretPercepts(EntityId self, EntityRead r)
        {
            if (r.Other.Value > self.Value && r.Valence >= GreetValenceBar && r.Recognition >= GreetRecognitionBar)
                TryGreet(self, r.Other);
            else if (r.Valence <= DislikeValenceBar)
                TryDislike(self, r.Other);
        }

        void TryGreet(EntityId a, EntityId b)
        {
            if (_nextGreetAt.TryGetValue(a, out var atA) && _gameMinutes < atA) return;
            if (_nextGreetAt.TryGetValue(b, out var atB) && _gameMinutes < atB) return;
            _nextGreetAt[a] = _gameMinutes + GreetCooldownGameMinutes;
            _nextGreetAt[b] = _gameMinutes + GreetCooldownGameMinutes;

            // Just announce the greeting; AffectsSystem turns it into mutual
            // affection + the regard impulse (S2 — one place owns the magnitude).
            _ctx.Events.Emit(new GreetingEvent { A = a, B = b });
        }

        void TryDislike(EntityId who, EntityId whom)
        {
            if (_nextDislikeAt.TryGetValue(who, out var at) && _gameMinutes < at) return;
            _nextDislikeAt[who] = _gameMinutes + DislikeCooldownGameMinutes;
            _ctx.Events.Emit(new DislikeNearbyEvent { Who = who, Whom = whom });
        }

        /// Capacity-limited attention: keep the MaxReads highest-attention reads
        /// (deterministic: attention desc, id asc tie-break).
        static void TrimToTopK(List<EntityRead> reads)
        {
            if (reads.Count <= MaxReads) return;
            reads.Sort((x, y) =>
            {
                int c = y.Attention.CompareTo(x.Attention);
                return c != 0 ? c : x.Other.Value.CompareTo(y.Other.Value);
            });
            reads.RemoveRange(MaxReads, reads.Count - MaxReads);
        }
    }
}
