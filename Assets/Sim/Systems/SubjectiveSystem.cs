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
        const double StigmaScale = 0.5;         // S4: how hard disposition (Warmth) colors the read of a beggar

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
        /// Innate dread of a hostile creature (V2): a clear threat reads strongly
        /// aversive and carries Threat clarity = 1 (the fear drive's target field).
        const double ThreatValence = -1.0;

        public static EntityRead Interpret(SimulationContext ctx, EntityId self, EntityId other)
        {
            // A creature is read by what it IS (a CreatureRegistry member), not a
            // dossier: an innate, recognized threat (the membrane completing
            // ambiguous threats from a low-clarity percept is the fear-controller's
            // job, V2b). No social history applies.
            if (ctx.Creatures.Contains(other))
                return new EntityRead
                {
                    Other = other, Valence = ThreatValence,
                    Recognition = 1.0, Trust = 1.0,
                    Attention = 1.0 + System.Math.Abs(ThreatValence),   // threats grab attention
                    Threat = 1.0,
                };

            double familiarity = 0, baseValence;
            if (ctx.Relations.TryGet(self, out var rels) && rels.Of.TryGetValue(other, out var rel))
            {
                baseValence = rel.Regard;          // I know THEM — judge by my history with them (dossier)
                familiarity = rel.Familiarity;
            }
            else
            {
                // A stranger — judge by their KIND, the learned category (S3).
                baseValence = MeaningsSystem.CategoryValence(ctx, self, other);
            }
            // + acute feeling (Affects, S2). Emotion-as-controller: this valence
            // colors the decider's place-lens and the greet/dislike percepts.
            double valence = baseValence + ctx.Affects.ValenceToward(self, other);
            // S4 stigma: a beggar (Doing Beg — observable in the public molecule)
            // is read through the perceiver's disposition — a cold soul disdains,
            // a warm one pities. Same beggar, opposite read.
            if (ctx.Behavior.TryGet(other, out var ob) && ob != null
                && ob.Phase == ActivityPhase.Doing && ob.Activity == ActivityKind.Beg
                && ctx.Personality.TryGet(self, out var p) && p != null)
                valence += (p.Trait(TraitIndex.Warmth) - 0.5) * StigmaScale;
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
            // A threat is not a social other — it doesn't greet or earn a social
            // grudge; the fear drive (V2b) owns the response. Skip social percepts.
            if (r.Threat > 0) return;
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
