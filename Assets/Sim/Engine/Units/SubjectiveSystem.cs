using System.Collections.Generic;
using DaggerfallWorkshop.Sim.Memory;
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
        readonly WorldClockRegistry _clock;
        readonly SensedRegistry _sensed;
        readonly SubjectiveViewRegistry _subjective;
        readonly RelationsRegistry _relations;
        readonly AffectsRegistry _affects;
        readonly BehaviorRegistry _behavior;
        readonly PersonalityRegistry _personality;
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
            BehaviorRegistry behavior,
            PersonalityRegistry personality,
            SocialCooldownRegistry cooldowns,
            PerceivableRegistry perceivable,
            AgentMemoryRegistry agentMem) : base(events)
        {
            _clock = clock;
            _sensed = sensed;
            _subjective = subjective;
            _relations = relations;
            _affects = affects;
            _behavior = behavior;
            _personality = personality;
            _cooldowns = cooldowns;
            _perceivable = perceivable;
            _agentMem = agentMem;
        }

        const double SizeMassMenace = 0.3;   // FROZEN: raw menace a large body carries even unarmed

        /// <summary>The prey-veto: Threat = max over aversive CUES, read RELATIVE to the perceiver's
        /// own size (its disposition emerges from its own form — a big/armed body fears equal menace
        /// less). rel = targetSize / perceiverSize. Built as a max over cues so the L2 behaviour cue
        /// and the Phase-C/D reputation cue drop in as further entries. NO oracle.</summary>
        public static double ThreatRead(AtomBag bag, double perceiverSize, out double threatValence)
        {
            threatValence = 0;
            if (bag == null || bag.Count == 0) return 0;
            if (perceiverSize < 0.01) perceiverSize = 0.01;          // floor: never divide by zero
            bool hasSize = bag.TryGet(AtomName.Size.ToId(), out var sizeFx);
            double targetSize = hasSize ? sizeFx.ToDouble() : 1.0;
            double rel = targetSize / perceiverSize;                 // my own size is the denominator
            double threat = 0;
            for (int i = 0; i < bag.Count; i++)                      // capacity cues: weapon tone x relative size
            {
                AtomTone tone = AtomCatalog.For(bag[i].Type).Tone;
                double menace = -tone.Valence.ToDouble();            // > 0 only for an aversive (weapon) atom
                if (menace <= 0) continue;
                double cue = menace * tone.Arousal.ToDouble() * rel;
                if (cue > threat) { threat = cue; threatValence = tone.Valence.ToDouble(); }
            }
            if (hasSize)                                             // mass-menace: only a BIGGER body menaces
            {
                double mass = SizeMassMenace * (rel > 1.0 ? rel - 1.0 : 0.0);
                if (mass > threat) { threat = mass; threatValence = -mass; }
            }
            if (threat > 1.0) threat = 1.0;
            if (threatValence < -1.0) threatValence = -1.0;
            return threat;
        }

        /// interpret(self, other): what `other` means to `self` right now. A pure read,
        /// callable for sensed OR remembered entities. S1: valence is the dossier regard,
        /// recognition/trust the familiarity, attention a simple salience. S2 adds affect to
        /// attention. For strangers, valence is the agent's own learned category via
        /// RecognizedValence (fed by MemoryReinforceSystem).
        ///
        /// Static so OddSystem can read an entity it isn't sensing (a place's occupants).
        public static EntityRead Interpret(
            RelationsRegistry relations, AffectsRegistry affects,
            BehaviorRegistry behavior, PersonalityRegistry personality,
            PerceivableRegistry perceivable, AgentMemoryRegistry agentMem,
            EntityId self, EntityId other)
        {
            // Threat = the prey-veto over perceived aversive cues, read RELATIVE to the perceiver's
            // own size (disposition emerges from its own form). NO _creatures oracle.
            double threat = 0, threatValence = 0;
            if (perceivable != null)
            {
                double selfSize = perceivable.Bag(self).TryGet(AtomName.Size.ToId(), out var ssz) ? ssz.ToDouble() : 1.0;
                threat = ThreatRead(perceivable.Bag(other), selfSize, out threatValence);
            }

            double familiarity = 0, baseValence;
            if (relations.TryGet(self, out var rels) && rels.Of.TryGetValue(other, out var rel))
            {
                baseValence = rel.Regard;          // I know THEM — judge by my history (dossier)
                familiarity = rel.Familiarity;
            }
            else
            {
                // A stranger — the agent's OWN learned category over their perceived signature
                // (fed by MemoryReinforceSystem). No role-scalar stereotype anymore.
                double newV = 0;
                if (agentMem != null && perceivable != null
                    && agentMem.TryGet(self, out var mem)
                    && mem.Meanings.RecognizedValence(perceivable.Signature(other), out var lv, out _))
                    newV = lv.ToDouble();
                baseValence = newV;
            }
            double valence = baseValence + affects.ValenceToward(self, other);
            // beggar-stigma: a beggar is read through the perceiver's Warmth (kept; B7).
            if (behavior.TryGet(other, out var ob) && ob != null
                && ob.Phase == ActivityPhase.Doing && ob.Activity == ActivityKind.Beg
                && personality.TryGet(self, out var p) && p != null)
                valence += (p.Trait(TraitIndex.Warmth) - 0.5) * StigmaScale;

            // The prey-veto: a perceived threat forces the read aversive, overriding the social blend.
            if (threat > 0 && threatValence < valence) valence = threatValence;

            return new EntityRead
            {
                Other = other,
                Valence = valence,
                Recognition = familiarity,
                Trust = familiarity,
                Attention = 0.1 + System.Math.Abs(valence) + familiarity + threat,   // a threat grabs attention
                Threat = threat,
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
                    var read = Interpret(_relations, _affects,
                                         _behavior, _personality, _perceivable, _agentMem,
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
