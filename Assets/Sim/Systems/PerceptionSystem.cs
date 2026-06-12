using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Rung 2 of Atoms' three "what to do now" mechanisms: percepts that
    /// short-circuit deliberation. Every few ticks each civilian senses who
    /// shares their street (spatial hash, 12 m radius):
    ///   - a friend passing by → GreetingEvent (OddSystem interrupts both
    ///     into a Chat) + small mutual warmth;
    ///   - someone resented (regard ≤ −0.3) → DislikeNearbyEvent, which
    ///     NeedsSystem turns into social discomfort. The first directed
    ///     emotion: who is nearby now changes how you feel.
    public sealed class PerceptionSystem : ISystem
    {
        const int SenseEveryTicks = 5;
        const float SenseRadius = 12f;
        const float CellSize = 16f;
        const double GreetCooldownGameMinutes = 90;
        const double DislikeCooldownGameMinutes = 30;
        const double GreetRegardBar = 0.3;
        const double GreetFamiliarityBar = 0.25;
        const double DislikeBar = -0.3;

        SimulationContext _ctx;
        readonly Dictionary<long, List<EntityId>> _buckets = new Dictionary<long, List<EntityId>>();
        readonly Dictionary<EntityId, double> _nextGreetAt = new Dictionary<EntityId, double>();
        readonly Dictionary<EntityId, double> _nextDislikeAt = new Dictionary<EntityId, double>();
        double _gameMinutes;

        public void Init(SimulationContext ctx) { _ctx = ctx; }
        public void ProcessEvents() { }

        public void Update(long tick)
        {
            var clock = _ctx.WorldClock.Current;
            if (clock.Year == 0) return;
            double dt = _ctx.Time.TickIntervalSeconds * clock.TimeScale / 60.0;
            if (dt <= 0) return;
            _gameMinutes += dt;

            if (tick % SenseEveryTicks != 0) return;

            // Spatial hash over civilians who are out and about. People busy
            // indoors (Doing at a building) don't street-watch.
            foreach (var list in _buckets.Values) list.Clear();
            foreach (var kv in _ctx.Behavior.All)
            {
                if (!IsOutAndAbout(kv.Value)) continue;
                if (!_ctx.Position.TryGet(kv.Key, out var pos)) continue;
                long cell = CellKey(pos.X, pos.Z);
                if (!_buckets.TryGetValue(cell, out var list))
                {
                    list = new List<EntityId>();
                    _buckets[cell] = list;
                }
                list.Add(kv.Key);
            }

            foreach (var kv in _buckets)
            {
                var list = kv.Value;
                for (int i = 0; i < list.Count; i++)
                    Sense(list[i]);
            }
        }

        static bool IsOutAndAbout(BehaviorData b)
        {
            if (b.Phase == ActivityPhase.Moving) return true;
            return b.Activity == ActivityKind.Wander
                || b.Activity == ActivityKind.Visit
                || b.Activity == ActivityKind.Idle;
        }

        void Sense(EntityId self)
        {
            if (!_ctx.Position.TryGet(self, out var pos)) return;
            if (!_ctx.Relations.TryGet(self, out var relations)) return;

            int cx = (int)(pos.X / CellSize), cy = (int)(pos.Z / CellSize);
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!_buckets.TryGetValue(Key(cx + dx, cy + dy), out var list)) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var other = list[i];
                        if (other == self) continue;
                        if (!relations.Of.TryGetValue(other, out var rel)) continue;
                        if (!_ctx.Position.TryGet(other, out var otherPos)) continue;

                        float ddx = otherPos.X - pos.X, ddz = otherPos.Z - pos.Z;
                        if (ddx * ddx + ddz * ddz > SenseRadius * SenseRadius) continue;

                        // Greeting is mutual — handle each pair once. Dislike
                        // is DIRECTED (A may resent B while B likes A), so
                        // every observer checks their own feelings.
                        if (other.Value > self.Value
                            && rel.Regard >= GreetRegardBar && rel.Familiarity >= GreetFamiliarityBar)
                            TryGreet(self, other);
                        else if (rel.Regard <= DislikeBar)
                            TryDislike(self, other);
                    }
                }
            }
        }

        void TryGreet(EntityId a, EntityId b)
        {
            if (_nextGreetAt.TryGetValue(a, out var atA) && _gameMinutes < atA) return;
            if (_nextGreetAt.TryGetValue(b, out var atB) && _gameMinutes < atB) return;
            _nextGreetAt[a] = _gameMinutes + GreetCooldownGameMinutes;
            _nextGreetAt[b] = _gameMinutes + GreetCooldownGameMinutes;

            _ctx.Events.Emit(new GreetingEvent { A = a, B = b });
            _ctx.Events.Emit(new RelationImpulseEvent { Who = a, Other = b, RegardDelta = 0.02, FamiliarityDelta = 0.01 });
            _ctx.Events.Emit(new RelationImpulseEvent { Who = b, Other = a, RegardDelta = 0.02, FamiliarityDelta = 0.01 });
        }

        void TryDislike(EntityId who, EntityId whom)
        {
            if (_nextDislikeAt.TryGetValue(who, out var at) && _gameMinutes < at) return;
            _nextDislikeAt[who] = _gameMinutes + DislikeCooldownGameMinutes;
            _ctx.Events.Emit(new DislikeNearbyEvent { Who = who, Whom = whom });
        }

        static long CellKey(float x, float z) => Key((int)(x / CellSize), (int)(z / CellSize));
        static long Key(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;
    }
}
