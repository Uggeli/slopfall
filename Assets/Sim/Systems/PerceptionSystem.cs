using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Rung 2, second stage: refine raw senses (SensedRegistry) into percepts
    /// worth acting on — the interrupts that short-circuit deliberation. Sensing
    /// (range + line-of-sight) is SenseSystem's job; this only interprets who is
    /// perceived, through the lens of how the perceiver feels about them:
    ///   - a friend passing by → GreetingEvent (both interrupt into a Chat) plus
    ///     small mutual warmth;
    ///   - someone resented (regard ≤ −0.3) → DislikeNearbyEvent, which
    ///     NeedsSystem turns into social discomfort. The first directed emotion:
    ///     who is nearby now changes how you feel.
    /// Strangers are sensed but raise no percept yet — wariness/curiosity toward
    /// the unknown is a later directed emotion.
    public sealed class PerceptionSystem : ISystem
    {
        const int SenseEveryTicks = 5;          // aligned with SenseSystem
        const double GreetCooldownGameMinutes = 90;
        const double DislikeCooldownGameMinutes = 30;
        const double GreetRegardBar = 0.3;
        const double GreetFamiliarityBar = 0.25;
        const double DislikeBar = -0.3;

        SimulationContext _ctx;
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

            if (tick % SenseEveryTicks != 0) return;    // SenseSystem just refreshed SensedRegistry

            foreach (var kv in _ctx.Sensed.All)
            {
                var self = kv.Key;
                if (!_ctx.Relations.TryGet(self, out var relations)) continue;

                var sensed = kv.Value;
                for (int i = 0; i < sensed.Count; i++)
                {
                    var other = sensed[i];
                    if (!relations.Of.TryGetValue(other, out var rel)) continue;   // stranger: no percept yet

                    // Greeting is mutual — handle each pair once (lower id drives
                    // it). Dislike is DIRECTED: every observer checks their own
                    // feelings, so A may flinch from B while B is happy to see A.
                    if (other.Value > self.Value
                        && rel.Regard >= GreetRegardBar && rel.Familiarity >= GreetFamiliarityBar)
                        TryGreet(self, other);
                    else if (rel.Regard <= DislikeBar)
                        TryDislike(self, other);
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
    }
}
