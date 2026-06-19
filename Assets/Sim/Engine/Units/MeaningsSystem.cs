using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of DaggerfallWorkshop.Sim.MeaningsSystem — Consolidation (Atoms memory
    // doc, proto-subset). Folds the outcome of each interaction into the agent's learned
    // category for the OTHER's KIND (role), so experience generalises ("keepers are stingy").
    // The awake fold runs on every recognised interaction; nodes decay toward neutral over
    // time (forgetting). Sole writer of MeaningsRegistry.
    //
    // Conversion notes (vs the old ISystem):
    //   * The _granted / _refused / _greets / _dislikes spool lists + Subscribe<> are gone:
    //     each Update reads last tick's interaction events via Events.GetEvents<T>().
    //   * ProcessEvents (fold) and Update (decay) collapse into one Update(long tick), FOLD
    //     before DECAY (the old TickLoop order), both on per-entity COPY-on-write MeaningsData.
    //   * The _gameMinutesSinceDecay cadence accumulator (a tick-state field) is gone: decay
    //     reacts to NewHourEvent (the "~1 game-hour cadence" the accumulator approximated),
    //     decaying by the FROZEN per-hour rate over one game-hour each time the hour ticks.
    //     Folds still run every Update (the doc's FOLD-is-awake). Every touched entity gets a
    //     whole-value MeaningsSetIntent; the registry is the sole applier.
    //   * SignatureOf / CategoryValence stay public statics here (SubjectiveSystem.Interpret
    //     and OddSystem read them), now against the engine ResidencyRegistry / MeaningsRegistry.
    //
    // Domain constants and math preserved EXACTLY.
    public sealed class MeaningsSystem : SimSystem
    {
        public const int UnknownSignature = -1;
        // FROZEN placeholders.
        const double LearnRate = 0.05;          // how fast a category shifts toward the latest outcome
        const double ConfGain = 0.05;           // confidence accrued per interaction (caps at 1)
        const double DecayPerHour = 0.004;      // slow forgetting toward neutral / no-opinion
        const double Epsilon = 0.001;

        // outcome of an interaction with a kind: good/bad experience, in [-1, 1]
        const double GrantOutcome = 1.0, RefuseOutcome = -1.0, GreetOutcome = 0.5, DislikeOutcome = -0.5;

        // The old accumulator decayed once per ~1 game-hour; reacting to NewHourEvent decays
        // over exactly one game-hour each fire — the same forgetting rate.
        const double DecayGameHours = 1.0;

        readonly WorldClockRegistry _clock;
        readonly MeaningsRegistry _meanings;
        readonly ResidencyRegistry _residency;

        public MeaningsSystem(EventBus events, WorldClockRegistry clock,
                              MeaningsRegistry meanings, ResidencyRegistry residency)
            : base(events)
        {
            _clock = clock;
            _meanings = meanings;
            _residency = residency;
        }

        public override void Update(long tick)
        {
            var clock = _clock.Current;
            if (clock.Year == 0) return;

            // Working copies of the settled per-entity MeaningsData, materialized lazily.
            var work = new Dictionary<EntityId, MeaningsData>();

            // --- FOLD (old ProcessEvents): generalise last tick's interactions into categories ---
            var granted = Events.GetEvents<HelpGrantedEvent>();
            for (int i = 0; i < granted.Length; i++)
                Fold(work, granted[i].Asker, SignatureOf(_residency, granted[i].Giver), GrantOutcome);

            var refused = Events.GetEvents<HelpRefusedEvent>();
            for (int i = 0; i < refused.Length; i++)
                Fold(work, refused[i].Asker, SignatureOf(_residency, refused[i].Refuser), RefuseOutcome);

            var greets = Events.GetEvents<GreetingEvent>();
            for (int i = 0; i < greets.Length; i++)
            {
                Fold(work, greets[i].A, SignatureOf(_residency, greets[i].B), GreetOutcome);
                Fold(work, greets[i].B, SignatureOf(_residency, greets[i].A), GreetOutcome);
            }

            var dislikes = Events.GetEvents<DislikeNearbyEvent>();
            for (int i = 0; i < dislikes.Length; i++)
                Fold(work, dislikes[i].Who, SignatureOf(_residency, dislikes[i].Whom), DislikeOutcome);

            // --- DECAY (old Update, ~1 game-hour cadence → NewHourEvent): forget toward neutral ---
            if (Events.GetEvents<NewHourEvent>().Length > 0)
            {
                // Deterministic key order (matches the old _keys.Sort by id).
                var keys = new List<EntityId>();
                foreach (var kv in _meanings.All) keys.Add(kv.Key);
                keys.Sort((a, b) => a.Value.CompareTo(b.Value));

                var drop = new List<int>();
                for (int k = 0; k < keys.Count; k++)
                {
                    var m = WorkingCopy(work, keys[k]);   // ensures every decaying entity is emitted
                    if (m == null) continue;
                    drop.Clear();
                    foreach (var kv in m.Nodes)
                    {
                        var node = kv.Value;
                        node.Valence = Decay.TowardBaseline(node.Valence, 0.0, DecayPerHour, DecayGameHours);
                        node.Confidence = Decay.TowardBaseline(node.Confidence, 0.0, DecayPerHour, DecayGameHours);
                        if (node.Confidence < Epsilon) drop.Add(kv.Key);
                    }
                    for (int d = 0; d < drop.Count; d++) m.Nodes.Remove(drop[d]);
                }
            }

            // --- emit whole-value set intents for every entity touched this tick ---
            foreach (var kv in work)
                Events.Publish(new MeaningsSetIntent { Id = kv.Key, Data = kv.Value });
        }

        /// Fold an outcome into self's category for `sig`, in the COPY-on-write MeaningsData.
        /// Mirrors the old Fold exactly (running mean toward the outcome + confidence accrual).
        void Fold(Dictionary<EntityId, MeaningsData> work, EntityId self, int sig, double outcome)
        {
            var m = WorkingCopyOrNew(work, self);
            if (!m.Nodes.TryGetValue(sig, out var node)) { node = new CategoryNode { Signature = sig }; m.Nodes[sig] = node; }
            node.Valence += LearnRate * (outcome - node.Valence);    // running mean toward the latest outcome
            node.Confidence += ConfGain;
            if (node.Confidence > 1.0) node.Confidence = 1.0;
        }

        /// The category key for an entity — its KIND. Proto: the resident role
        /// (keeper / resident / guard). 0 if unhoused → UnknownSignature.
        public static int SignatureOf(ResidencyRegistry residency, EntityId other)
            => residency.TryGet(other, out var r) && r != null ? (int)r.Role : UnknownSignature;

        /// What the holder expects of `other`'s KIND — the learned stereotype, weighted by
        /// confidence. 0 if no learned category (the common early case). Read by
        /// SubjectiveSystem.Interpret only when the other isn't known individually.
        public static double CategoryValence(MeaningsRegistry meanings, ResidencyRegistry residency,
                                             EntityId self, EntityId other)
        {
            if (!meanings.TryGet(self, out var m)) return 0;
            int sig = SignatureOf(residency, other);
            return m.Nodes.TryGetValue(sig, out var node) ? node.Valence * node.Confidence : 0;
        }

        // --- copy-on-write helpers ---

        /// Working copy for `id` from the settled registry value, or null if the agent has no
        /// learned categories (decay has nothing to do, emits nothing for it).
        MeaningsData WorkingCopy(Dictionary<EntityId, MeaningsData> work, EntityId id)
        {
            if (work.TryGetValue(id, out var existing)) return existing;
            if (!_meanings.TryGet(id, out var settled)) return null;
            var copy = Clone(settled);
            work[id] = copy;
            return copy;
        }

        /// Like WorkingCopy but never null — the old Fold did `new MeaningsData()` + Set when
        /// the agent had no store yet.
        MeaningsData WorkingCopyOrNew(Dictionary<EntityId, MeaningsData> work, EntityId id)
        {
            if (work.TryGetValue(id, out var existing)) return existing;
            var copy = _meanings.TryGet(id, out var settled) ? Clone(settled) : new MeaningsData();
            work[id] = copy;
            return copy;
        }

        static MeaningsData Clone(MeaningsData src)
        {
            var c = new MeaningsData();
            foreach (var kv in src.Nodes)
                c.Nodes[kv.Key] = new CategoryNode      // CategoryNode is a class — deep-copy
                {
                    Signature = kv.Value.Signature,
                    Valence = kv.Value.Valence,
                    Confidence = kv.Value.Confidence,
                };
            return c;
        }
    }
}
