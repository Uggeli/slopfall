using System.Collections.Generic;
using DaggerfallWorkshop.Utility;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of DaggerfallWorkshop.Sim.SubjectiveSystem — the membrane (Atoms row
    // 2). Turns raw senses (SensedRegistry) into a per-agent SubjectiveView and emits the
    // greeting / dislike percepts that fall out of the interpreted reads. Sole writer of
    // SubjectiveViewRegistry (via SubjectiveSetIntent — whole-value, rebuilt each sense-tick).
    //
    // Conversion notes (vs the old ISystem):
    //   * Init/ProcessEvents/Update collapse into Update(long tick). No tick-state fields.
    //   * The self-accumulated _gameMinutes counter is gone: "now" is read off WorldClock as
    //     absolute game-minutes (DaggerfallDateTime.ToSeconds()/60), the monotonic clock the
    //     old counter approximated. The paused guard (dt <= 0) is preserved via DeltaGameSeconds.
    //   * The _nextGreetAt / _nextDislikeAt dictionaries are HOMED in SocialCooldownRegistry;
    //     this system reads it read-only and emits SocialCooldownSetIntent to push the window.
    //     An intra-Update scratch overlay reproduces the old immediate-mutation behavior so a
    //     pair greets at most once per tick even before the intent lands next tick.
    //   * Interpret() stays a public static (OddSystem calls it for un-sensed occupants) but
    //     now takes the engine registries instead of a SimulationContext.
    //
    // Domain logic (interpret math, greet/dislike bars, top-K trim) preserved EXACTLY.
    public sealed class SubjectiveSystem : SimSystem
    {
        const int SenseEveryTicks = 5;          // aligned with SenseSystem
        const int MaxReads = 8;                 // capacity-limited attention (top-K)
        const double GreetCooldownGameMinutes = 90;
        const double DislikeCooldownGameMinutes = 30;
        const double GreetValenceBar = 0.3;
        const double GreetRecognitionBar = 0.25;
        const double DislikeValenceBar = -0.3;
        const double StigmaScale = 0.5;         // S4: how hard disposition (Warmth) colors the read of a beggar
        const double ThreatValence = -1.0;      // innate dread of a hostile creature (V2)

        readonly WorldClockRegistry _clock;
        readonly SensedRegistry _sensed;
        readonly SubjectiveViewRegistry _subjective;
        readonly RelationsRegistry _relations;
        readonly AffectsRegistry _affects;
        readonly MeaningsRegistry _meanings;
        readonly BehaviorRegistry _behavior;
        readonly PersonalityRegistry _personality;
        readonly CreatureRegistry _creatures;
        readonly ResidencyRegistry _residency;
        readonly SocialCooldownRegistry _cooldowns;
        readonly PerceivableRegistry _perceivable;
        readonly AgentMemoryRegistry _agentMem;

        public SubjectiveSystem(
            EventBus events,
            WorldClockRegistry clock,
            SensedRegistry sensed,
            SubjectiveViewRegistry subjective,
            RelationsRegistry relations,
            AffectsRegistry affects,
            MeaningsRegistry meanings,
            BehaviorRegistry behavior,
            PersonalityRegistry personality,
            CreatureRegistry creatures,
            ResidencyRegistry residency,
            SocialCooldownRegistry cooldowns,
            PerceivableRegistry perceivable,
            AgentMemoryRegistry agentMem) : base(events)
        {
            _clock = clock;
            _sensed = sensed;
            _subjective = subjective;
            _relations = relations;
            _affects = affects;
            _meanings = meanings;
            _behavior = behavior;
            _personality = personality;
            _creatures = creatures;
            _residency = residency;
            _cooldowns = cooldowns;
            _perceivable = perceivable;
            _agentMem = agentMem;
        }

        /// <summary>Confidence-weighted blend of the old role-scalar valence and the new learned
        /// category valence: confidence 0 = old (today's behavior), 1 = fully learned.</summary>
        public static double BlendValence(double oldV, double newV, double confidence)
            => oldV * (1.0 - confidence) + newV * confidence;

        /// interpret(self, other): what `other` means to `self` right now. A pure read,
        /// callable for sensed OR remembered entities. S1: valence is the dossier regard,
        /// recognition/trust the familiarity, attention a simple salience. S2 adds affect to
        /// attention; S3 makes valence a MEANINGS lookup adjusted by the per-entity dossier
        /// delta. An innate threat reads strongly aversive with Threat clarity = 1 (V2).
        ///
        /// Static so OddSystem can read an entity it isn't sensing (a place's occupants).
        public static EntityRead Interpret(
            CreatureRegistry creatures, RelationsRegistry relations, AffectsRegistry affects,
            MeaningsRegistry meanings, BehaviorRegistry behavior, PersonalityRegistry personality,
            ResidencyRegistry residency, PerceivableRegistry perceivable, AgentMemoryRegistry agentMem,
            EntityId self, EntityId other)
        {
            // A creature is read by what it IS (a CreatureRegistry member), not a dossier: an
            // innate, recognized threat. No social history applies.
            if (creatures.Contains(other))
                return new EntityRead
                {
                    Other = other, Valence = ThreatValence,
                    Recognition = 1.0, Trust = 1.0,
                    Attention = 1.0 + System.Math.Abs(ThreatValence),   // threats grab attention
                    Threat = 1.0,
                };

            double familiarity = 0, baseValence;
            if (relations.TryGet(self, out var rels) && rels.Of.TryGetValue(other, out var rel))
            {
                baseValence = rel.Regard;          // I know THEM — judge by my history with them (dossier)
                familiarity = rel.Familiarity;
            }
            else
            {
                // A stranger — judge by their KIND. Blend the old role-scalar with the agent's OWN
                // learned category (the new MeaningsStore), weighted by how confident that learning
                // is: confidence 0 = today's behavior, 1 = fully the agent's perceived/reinforced read.
                double oldV = MeaningsSystem.CategoryValence(meanings, residency, self, other);
                double newV = 0, conf = 0;
                if (agentMem != null && perceivable != null
                    && agentMem.TryGet(self, out var mem)
                    && mem.Meanings.RecognizedValence(perceivable.Signature(other), out var lv, out var lc))
                { newV = lv.ToDouble(); conf = lc.ToDouble(); }
                baseValence = BlendValence(oldV, newV, conf);
            }
            // + acute feeling (Affects, S2). Emotion-as-controller: this valence colors the
            // decider's place-lens and the greet/dislike percepts.
            double valence = baseValence + affects.ValenceToward(self, other);
            // S4 stigma: a beggar (Doing Beg — observable in the public molecule) is read
            // through the perceiver's disposition — a cold soul disdains, a warm one pities.
            if (behavior.TryGet(other, out var ob) && ob != null
                && ob.Phase == ActivityPhase.Doing && ob.Activity == ActivityKind.Beg
                && personality.TryGet(self, out var p) && p != null)
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

        public override void Update(long tick)
        {
            var clock = _clock.Current;
            if (clock.Year == 0) return;
            double dt = clock.DeltaGameSeconds / 60.0;
            if (dt <= 0) return;                       // paused — no game-time passed

            if (tick % SenseEveryTicks != 0) return;   // SenseSystem just refreshed senses

            // "now" as absolute game-minutes — the monotonic clock the old _gameMinutes
            // counter approximated, now read off WorldClock rather than carried tick to tick.
            double now = NowGameMinutes(clock);

            // Intra-tick scratch overlay over the registry cooldowns: the old TryGreet/
            // TryDislike mutated their dictionaries immediately, so a later pair in the SAME
            // Update saw the fresh window. We reproduce that here, then publish the row
            // updates as SocialCooldownSetIntents (one per touched entity, last-write-wins).
            var scratchGreet = new Dictionary<EntityId, double>();
            var scratchDislike = new Dictionary<EntityId, double>();

            foreach (var kv in _sensed.All)
            {
                var self = kv.Key;
                var sensed = kv.Value;
                if (sensed.Count == 0)
                {
                    // Old code: Subjective.Remove(self). The view registry has no Remove —
                    // emit an EMPTY view (clear-on-empty), matching other converted systems.
                    Events.Publish(new SubjectiveSetIntent { Id = self, Data = new SubjectiveViewData() });
                    continue;
                }

                var view = new SubjectiveViewData();
                for (int i = 0; i < sensed.Count; i++)
                {
                    var read = Interpret(_creatures, _relations, _affects, _meanings,
                                         _behavior, _personality, _residency, _perceivable, _agentMem,
                                         self, sensed[i]);
                    view.Entities.Add(read);
                    InterpretPercepts(self, read, now, scratchGreet, scratchDislike);
                }
                TrimToTopK(view.Entities);
                Events.Publish(new SubjectiveSetIntent { Id = self, Data = view });
            }

            // Flush the touched cooldown rows. Each entity that greeted or registered a
            // dislike this tick gets a whole-value SocialCooldownSetIntent carrying its
            // (possibly newly-overlaid) next-greet / next-dislike times.
            FlushCooldowns(scratchGreet, scratchDislike);
        }

        /// Greet a recognized, well-regarded other (mutual — the lower id drives, so the pair
        /// greets once); flinch from a strongly-disliked one (directed — each observer checks
        /// its own read). Both derived from interpret(). Threats are not social others.
        void InterpretPercepts(EntityId self, EntityRead r, double now,
                               Dictionary<EntityId, double> scratchGreet,
                               Dictionary<EntityId, double> scratchDislike)
        {
            if (r.Threat > 0) return;
            if (r.Other.Value > self.Value && r.Valence >= GreetValenceBar && r.Recognition >= GreetRecognitionBar)
                TryGreet(self, r.Other, now, scratchGreet);
            else if (r.Valence <= DislikeValenceBar)
                TryDislike(self, r.Other, now, scratchDislike);
        }

        void TryGreet(EntityId a, EntityId b, double now, Dictionary<EntityId, double> scratchGreet)
        {
            if (NextGreetAt(a, scratchGreet) > now) return;
            if (NextGreetAt(b, scratchGreet) > now) return;
            scratchGreet[a] = now + GreetCooldownGameMinutes;
            scratchGreet[b] = now + GreetCooldownGameMinutes;

            // Just announce the greeting; AffectsSystem turns it into mutual affection + the
            // regard impulse (S2 — one place owns the magnitude).
            Events.Publish(new GreetingEvent { A = a, B = b });
        }

        void TryDislike(EntityId who, EntityId whom, double now, Dictionary<EntityId, double> scratchDislike)
        {
            if (NextDislikeAt(who, scratchDislike) > now) return;
            scratchDislike[who] = now + DislikeCooldownGameMinutes;
            Events.Publish(new DislikeNearbyEvent { Who = who, Whom = whom });
        }

        // Cooldown reads: the intra-tick scratch overlay shadows the settled registry value,
        // mirroring the old `>= now` semantics (gate only when strictly in the future).
        double NextGreetAt(EntityId id, Dictionary<EntityId, double> scratch)
            => scratch.TryGetValue(id, out var v) ? v : _cooldowns.NextGreetAt(id);

        double NextDislikeAt(EntityId id, Dictionary<EntityId, double> scratch)
            => scratch.TryGetValue(id, out var v) ? v : _cooldowns.NextDislikeAt(id);

        void FlushCooldowns(Dictionary<EntityId, double> scratchGreet, Dictionary<EntityId, double> scratchDislike)
        {
            if (scratchGreet.Count == 0 && scratchDislike.Count == 0) return;

            // Union of touched entities — each gets ONE whole-row intent preserving the other
            // axis from its current settled value.
            var touched = new HashSet<EntityId>();
            foreach (var k in scratchGreet.Keys) touched.Add(k);
            foreach (var k in scratchDislike.Keys) touched.Add(k);

            foreach (var id in touched)
            {
                _cooldowns.TryGet(id, out var cur);     // default(SocialCooldownData) if absent
                var data = cur;
                if (scratchGreet.TryGetValue(id, out var g)) data.NextGreetAt = g;
                if (scratchDislike.TryGetValue(id, out var d)) data.NextDislikeAt = d;
                Events.Publish(new SocialCooldownSetIntent { Id = id, Data = data });
            }
        }

        /// Absolute game-minutes from the world clock — the monotonic timestamp cooldowns
        /// compare against. Built from the same DaggerfallDateTime the TimeSystem advances.
        static double NowGameMinutes(WorldClockData c)
        {
            var dt = new DaggerfallDateTime
            {
                Year = c.Year, Month = c.Month, Day = c.Day,
                Hour = c.Hour, Minute = c.Minute, Second = c.Second,
            };
            return dt.ToSeconds() / 60.0;
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
