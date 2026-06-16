using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Consolidation (Atoms memory doc, proto-subset): folds the outcome of each
    /// interaction into the agent's learned category for the OTHER's KIND (role),
    /// so experience generalises — "keepers are stingy", "guards are kind". The
    /// awake fold runs on every recognised interaction (the doc's FOLD-is-awake);
    /// nodes decay toward neutral over time (forgetting). Sole writer of
    /// MeaningsRegistry. interpret() reads it to judge a STRANGER by their kind.
    /// See docs/cognitive_substrate_S3_meanings.md.
    public sealed class MeaningsSystem : ISystem
    {
        public const int UnknownSignature = -1;
        // FROZEN placeholders.
        const double LearnRate = 0.05;          // how fast a category shifts toward the latest outcome
        const double ConfGain = 0.05;           // confidence accrued per interaction (caps at 1)
        const double DecayPerHour = 0.004;      // slow forgetting toward neutral / no-opinion
        const double Epsilon = 0.001;

        // outcome of an interaction with a kind: good/bad experience, in [-1, 1]
        const double GrantOutcome = 1.0, RefuseOutcome = -1.0, GreetOutcome = 0.5, DislikeOutcome = -0.5;

        SimulationContext _ctx;
        readonly List<HelpGrantedEvent> _granted = new List<HelpGrantedEvent>();
        readonly List<HelpRefusedEvent> _refused = new List<HelpRefusedEvent>();
        readonly List<GreetingEvent> _greets = new List<GreetingEvent>();
        readonly List<DislikeNearbyEvent> _dislikes = new List<DislikeNearbyEvent>();
        readonly List<EntityId> _keys = new List<EntityId>();
        double _gameMinutesSinceDecay;

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
            ctx.Events.Subscribe<HelpGrantedEvent>(e => _granted.Add(e));
            ctx.Events.Subscribe<HelpRefusedEvent>(e => _refused.Add(e));
            ctx.Events.Subscribe<GreetingEvent>(e => _greets.Add(e));
            ctx.Events.Subscribe<DislikeNearbyEvent>(e => _dislikes.Add(e));
        }

        public void ProcessEvents()
        {
            for (int i = 0; i < _granted.Count; i++)
                Fold(_granted[i].Asker, SignatureOf(_ctx, _granted[i].Giver), GrantOutcome);
            _granted.Clear();

            for (int i = 0; i < _refused.Count; i++)
                Fold(_refused[i].Asker, SignatureOf(_ctx, _refused[i].Refuser), RefuseOutcome);
            _refused.Clear();

            for (int i = 0; i < _greets.Count; i++)
            {
                Fold(_greets[i].A, SignatureOf(_ctx, _greets[i].B), GreetOutcome);
                Fold(_greets[i].B, SignatureOf(_ctx, _greets[i].A), GreetOutcome);
            }
            _greets.Clear();

            for (int i = 0; i < _dislikes.Count; i++)
                Fold(_dislikes[i].Who, SignatureOf(_ctx, _dislikes[i].Whom), DislikeOutcome);
            _dislikes.Clear();
        }

        public void Update(long tick)
        {
            var clock = _ctx.WorldClock.Current;
            if (clock.Year == 0) return;
            double gameMinutes = _ctx.Time.TickIntervalSeconds * clock.TimeScale / 60.0;
            _gameMinutesSinceDecay += gameMinutes;
            if (_gameMinutesSinceDecay < 60.0) return;          // forget on a ~1 game-hour cadence
            double gameHours = _gameMinutesSinceDecay / 60.0;
            _gameMinutesSinceDecay = 0.0;

            _keys.Clear();
            foreach (var kv in _ctx.Meanings.All) _keys.Add(kv.Key);
            _keys.Sort((a, b) => a.Value.CompareTo(b.Value));   // key order → determinism
            for (int k = 0; k < _keys.Count; k++)
            {
                if (!_ctx.Meanings.TryGet(_keys[k], out var m)) continue;
                _dropKeys.Clear();
                foreach (var kv in m.Nodes)
                {
                    var node = kv.Value;
                    node.Valence = Decay.TowardBaseline(node.Valence, 0.0, DecayPerHour, gameHours);
                    node.Confidence = Decay.TowardBaseline(node.Confidence, 0.0, DecayPerHour, gameHours);
                    if (node.Confidence < Epsilon) _dropKeys.Add(kv.Key);
                }
                for (int d = 0; d < _dropKeys.Count; d++) m.Nodes.Remove(_dropKeys[d]);
            }
        }

        readonly List<int> _dropKeys = new List<int>();

        void Fold(EntityId self, int sig, double outcome)
        {
            if (!_ctx.Meanings.TryGet(self, out var m)) { m = new MeaningsData(); _ctx.Meanings.Set(self, m); }
            if (!m.Nodes.TryGetValue(sig, out var node)) { node = new CategoryNode { Signature = sig }; m.Nodes[sig] = node; }
            node.Valence += LearnRate * (outcome - node.Valence);    // running mean toward the latest outcome
            node.Confidence += ConfGain;
            if (node.Confidence > 1.0) node.Confidence = 1.0;
        }

        /// The category key for an entity — its KIND. Proto: the resident role
        /// (keeper / resident / guard); richer signatures (career, appearance,
        /// learned individual prototypes) are later.
        public static int SignatureOf(SimulationContext ctx, EntityId other)
            => ctx.Residency.TryGet(other, out var r) && r != null ? (int)r.Role : UnknownSignature;

        /// What the holder expects of `other`'s KIND — the learned stereotype,
        /// weighted by confidence. 0 if no learned category (the common early
        /// case). Read by interpret() only when the other isn't known individually.
        public static double CategoryValence(SimulationContext ctx, EntityId self, EntityId other)
        {
            if (!ctx.Meanings.TryGet(self, out var m)) return 0;
            int sig = SignatureOf(ctx, other);
            return m.Nodes.TryGetValue(sig, out var node) ? node.Valence * node.Confidence : 0;
        }
    }
}
