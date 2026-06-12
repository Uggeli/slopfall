using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// The decision core — ODD wedge v1. Per civilian, when the current
    /// activity ends (or dawn/dusk forces a rethink), generate candidate
    /// activities from town context and argmax the ODD gap score.
    ///
    /// Ported from the ODD testbed: deficit V-vector, weighted-L2 gap scoring,
    /// Object-Zero liveness (Idle/Wander always candidates). Reinvented for
    /// DFU: the marketplace generates candidates from buildings + residency +
    /// clock instead of scene Things/affordances. Deferred, deliberately: the
    /// OddTree multi-step planner (single-step argmax until actions chain),
    /// directed emotions, trait alignment, risk-variance, semantic gates, and
    /// Atoms' prepotency hard-cull (no growth drives to gate yet).
    ///
    /// Sole writer of BehaviorRegistry. MovementSystem reports arrivals via
    /// ArrivedAtTargetEvent; this system flips Moving → Doing.
    public sealed class OddSystem : ISystem
    {
        const float ArriveImmediatelyDistance = 3f;     // meters
        const double MaxCommitGameMinutes = 60;         // re-evaluate at least hourly

        SimulationContext _ctx;
        readonly List<EntityId> _arrivals = new List<EntityId>();
        bool _reDecideAll;

        // Place caches — BuildingRegistry is immutable after town load.
        List<int> _tavernIndices;
        List<int> _landmarkIndices;

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
            ctx.Events.Subscribe<ArrivedAtTargetEvent>(e => _arrivals.Add(e.Entity));
            ctx.Events.Subscribe<DawnSimEvent>(e => _reDecideAll = true);
            ctx.Events.Subscribe<DuskSimEvent>(e => _reDecideAll = true);
        }

        public void ProcessEvents()
        {
            for (int i = 0; i < _arrivals.Count; i++)
            {
                if (_ctx.Behavior.TryGet(_arrivals[i], out var b) && b.Phase == ActivityPhase.Moving)
                {
                    _ctx.Behavior.Set(_arrivals[i], new BehaviorData
                    {
                        Activity = b.Activity,
                        Phase = ActivityPhase.Doing,
                        TargetBuilding = b.TargetBuilding,
                        TargetX = b.TargetX,
                        TargetZ = b.TargetZ,
                        RemainingGameMinutes = b.RemainingGameMinutes,
                    });
                }
            }
            _arrivals.Clear();
        }

        public void Update(long tick)
        {
            var clock = _ctx.WorldClock.Current;
            if (clock.Year == 0) return;

            double gameMinutes = _ctx.Time.TickIntervalSeconds * clock.TimeScale / 60.0;
            bool reDecideAll = _reDecideAll;
            _reDecideAll = false;

            foreach (var kv in _ctx.Residency.All)
            {
                var id = kv.Key;
                bool decide = reDecideAll;

                BehaviorData behavior;
                if (_ctx.Behavior.TryGet(id, out behavior))
                {
                    if (behavior.Phase == ActivityPhase.Doing)
                    {
                        double remaining = behavior.RemainingGameMinutes - gameMinutes;
                        double since = behavior.SinceDecisionGameMinutes + gameMinutes;
                        // Per-entity cap (48..79 min): a population that decides
                        // on one shared clock moves in lockstep waves; staggered
                        // caps spread re-decisions into a constant trickle.
                        double cap = 48 + (Hash(id.Value, 0) & 0x1F);
                        if (remaining <= 0 || since >= cap)
                        {
                            decide = true;
                        }
                        else if (!decide)
                        {
                            _ctx.Behavior.Set(id, new BehaviorData
                            {
                                Activity = behavior.Activity,
                                Phase = ActivityPhase.Doing,
                                TargetBuilding = behavior.TargetBuilding,
                                TargetX = behavior.TargetX,
                                TargetZ = behavior.TargetZ,
                                RemainingGameMinutes = remaining,
                                SinceDecisionGameMinutes = since,
                            });
                        }
                        if (decide) behavior.RemainingGameMinutes = remaining;
                    }
                    // Moving entities keep walking unless dawn/dusk re-decides.
                }
                else
                {
                    decide = true;
                }

                if (decide)
                    Decide(id, kv.Value, behavior, clock.Hour, tick);
            }
        }

        void Decide(EntityId id, ResidencyData residency, BehaviorData current, int hour, long tick)
        {
            if (!_ctx.Needs.TryGet(id, out var needs)) return;
            if (!_ctx.Buildings.TryGet(residency.BuildingIndex, out var home)) return;
            if (!_ctx.Position.TryGet(id, out var pos)) return;

            var w = ActivityCatalog.Weights;
            bool isKeeper = residency.Role == ResidentRole.Keeper;
            bool night = IsNight(hour);

            // Hysteresis: the activity in progress defends its slot with a
            // multiplicative bonus, or hourly re-evaluation flickers between
            // whichever deficit is momentarily largest (Atoms: enter-high /
            // exit-low; a challenger must clearly beat the incumbent).
            ActivityKind incumbent = (current != null && current.Phase == ActivityPhase.Doing)
                ? current.Activity : ActivityKind.None;
            const double Sticky = 1.4;

            // --- Candidate generation (the marketplace). ---
            ActivityCatalog.Spec bestSpec = ActivityCatalog.Idle;
            int bestBuilding = residency.BuildingIndex;
            float bestX = pos.X, bestZ = pos.Z;
            double bestScore = OddScore.Compute(needs.V, ActivityCatalog.Idle.Delta, w, 1.0, ActivityCatalog.Idle.BaseUtility);

            // Wander — Object-Zero sibling; offset derived from (id, tick) so
            // decisions stay deterministic regardless of iteration order.
            {
                uint h32 = Hash(id.Value, tick);
                float dx = ((h32 & 0xFF) / 255f - 0.5f) * 60f;
                float dz = (((h32 >> 8) & 0xFF) / 255f - 0.5f) * 60f;
                double gate = night ? 0.3 : 1.0;
                double s = OddScore.Compute(needs.V, ActivityCatalog.Wander.Delta, w, gate, ActivityCatalog.Wander.BaseUtility);
                if (incumbent == ActivityKind.Wander) s *= Sticky;
                if (s > bestScore) { bestScore = s; bestSpec = ActivityCatalog.Wander; bestBuilding = -1; bestX = pos.X + dx; bestZ = pos.Z + dz; }
            }

            // Sleep at home (keepers live above the shop). The night base
            // utility is circadian pressure: once EnergyDef hits zero the gap
            // vanishes, and without it nothing holds a rested sleeper in bed
            // until morning.
            {
                double gate = night ? 2.0 : 0.12;
                double circadian = night ? 0.06 : 0;
                double s = OddScore.Compute(needs.V, ActivityCatalog.Sleep.Delta, w, gate, circadian);
                if (incumbent == ActivityKind.Sleep) s *= Sticky;
                if (s > bestScore) { bestScore = s; bestSpec = ActivityCatalog.Sleep; bestBuilding = residency.BuildingIndex; bestX = home.X; bestZ = home.Z; }
            }

            // Eat at home — kitchens mostly cold in the small hours.
            {
                double gate = night ? 0.15 : 1.0;
                double s = OddScore.Compute(needs.V, ActivityCatalog.EatHome.Delta, w, gate, 0);
                if (incumbent == ActivityKind.EatHome) s *= Sticky;
                if (s > bestScore) { bestScore = s; bestSpec = ActivityCatalog.EatHome; bestBuilding = residency.BuildingIndex; bestX = home.X; bestZ = home.Z; }
            }

            // Work — keepers only, business hours. The base utility is duty:
            // a shopkeeper holds shop through the day even with a full purse,
            // rather than napping the moment coin pressure drops (routine as a
            // standing pull — Atoms' growth/duty drives will replace this).
            if (isKeeper && hour >= 8 && hour < 18)
            {
                double s = OddScore.Compute(needs.V, ActivityCatalog.Work.Delta, w, 1.3, 0.02);
                if (incumbent == ActivityKind.Work) s *= Sticky;
                if (s > bestScore) { bestScore = s; bestSpec = ActivityCatalog.Work; bestBuilding = residency.BuildingIndex; bestX = home.X; bestZ = home.Z; }
            }

            double prepotency = PrepotencyGate(needs.V);

            // Tavern offerings, while open — every tavern competes, scored by
            // distance and liveliness, so regulars and a "popular pub" emerge.
            if (hour >= 6 && hour < 23)
            {
                EnsureTaverns();
                for (int t = 0; t < _tavernIndices.Count; t++)
                {
                    if (!_ctx.Buildings.TryGet(_tavernIndices[t], out var tav)) continue;
                    float tdx = tav.X - pos.X, tdz = tav.Z - pos.Z;
                    double dist = System.Math.Sqrt(tdx * tdx + tdz * tdz);
                    double distFactor = 1.0 / (1.0 + dist / 150.0);
                    double lively = 1.0 + 0.04 * System.Math.Min(_ctx.Occupancy.PlaceCount(_tavernIndices[t]), 8);

                    double s = OddScore.Compute(needs.V, ActivityCatalog.EatTavern.Delta, w, distFactor, 0);
                    if (incumbent == ActivityKind.EatTavern && bestBuilding == _tavernIndices[t]) s *= Sticky;
                    if (s > bestScore) { bestScore = s; bestSpec = ActivityCatalog.EatTavern; bestBuilding = _tavernIndices[t]; bestX = tav.X; bestZ = tav.Z; }

                    double socialGate = ((hour >= 17) ? 1.5 : 1.0) * distFactor * lively * prepotency;
                    s = OddScore.Compute(needs.V, ActivityCatalog.Socialize.Delta, w, socialGate, 0);
                    if (incumbent == ActivityKind.Socialize) s *= Sticky;
                    if (s > bestScore) { bestScore = s; bestSpec = ActivityCatalog.Socialize; bestBuilding = _tavernIndices[t]; bestX = tav.X; bestZ = tav.Z; }
                }
            }

            // Visit — the growth drive. No deficit served; engagement is the
            // reward. Hard prepotency cull plus daylight-ish hours.
            if (prepotency > 0 && hour >= 7 && hour < 21)
            {
                EnsureLandmarks();
                if (_landmarkIndices.Count > 0)
                {
                    // Rotate the landmark per entity per ~2h block, hash-picked.
                    int pick = (int)(Hash(id.Value, tick / 1200) % (uint)_landmarkIndices.Count);
                    if (_ctx.Buildings.TryGet(_landmarkIndices[pick], out var spot))
                    {
                        float vdx = spot.X - pos.X, vdz = spot.Z - pos.Z;
                        double dist = System.Math.Sqrt(vdx * vdx + vdz * vdz);
                        double distFactor = 1.0 / (1.0 + dist / 200.0);
                        double s = OddScore.Compute(needs.V, ActivityCatalog.Visit.Delta, w, prepotency * distFactor,
                            ActivityCatalog.Visit.BaseUtility * prepotency * distFactor);
                        if (incumbent == ActivityKind.Visit) s *= Sticky;
                        if (s > bestScore) { bestScore = s; bestSpec = ActivityCatalog.Visit; bestBuilding = _landmarkIndices[pick]; bestX = spot.X; bestZ = spot.Z; }
                    }
                }
            }

            // --- Commit. ---
            // Same activity still winning mid-flight = a resume, not a restart:
            // keep the remaining duration and don't re-announce it.
            bool resume = current != null
                && current.Activity == bestSpec.Kind
                && current.Phase == ActivityPhase.Doing
                && current.RemainingGameMinutes > 0
                && current.TargetBuilding == bestBuilding;

            float ddx = bestX - pos.X, ddz = bestZ - pos.Z;
            bool atSpot = resume
                || (ddx * ddx + ddz * ddz) <= ArriveImmediatelyDistance * ArriveImmediatelyDistance;

            // ±15% deterministic duration jitter — uniform durations re-align
            // the whole town to shared activity boundaries within a few hours.
            double duration = bestSpec.DurationMinutes * (0.85 + 0.3 * Hash01(id.Value, tick));

            _ctx.Behavior.Set(id, new BehaviorData
            {
                Activity = bestSpec.Kind,
                Phase = atSpot ? ActivityPhase.Doing : ActivityPhase.Moving,
                TargetBuilding = bestBuilding,
                TargetX = resume ? current.TargetX : bestX,
                TargetZ = resume ? current.TargetZ : bestZ,
                RemainingGameMinutes = resume ? current.RemainingGameMinutes : duration,
                SinceDecisionGameMinutes = 0,
            });

            if (!resume)
                _ctx.Events.Emit(new ActivityStartedEvent { Entity = id, Activity = bestSpec.Kind, TargetBuilding = bestBuilding });
        }

        /// Prepotency (Atoms, two-regime): leisure only wins when deficiency
        /// drives are quiet — graded suppression as the loudest deficiency
        /// rises, hard cull at the threshold ("a starving bunny can't binky").
        /// Hysteresis deferred; hourly decisions + sticky damp the boundary
        /// flicker for now.
        public static double PrepotencyGate(double[] v)
        {
            double loudest = v[NeedAxis.Hunger] > v[NeedAxis.EnergyDef]
                ? v[NeedAxis.Hunger] : v[NeedAxis.EnergyDef];
            return loudest >= 0.8 ? 0 : 1.0 - loudest / 0.8;
        }

        static bool IsNight(int hour) => hour >= 21 || hour < 6;

        static uint Hash(int idValue, long tick)
        {
            uint x = (uint)(idValue * 2654435761u) ^ (uint)(tick * 40503u);
            x ^= x >> 13; x *= 0x5bd1e995; x ^= x >> 15;
            return x;
        }

        static double Hash01(int idValue, long tick) => Hash(idValue, tick) / (double)uint.MaxValue;

        void EnsureTaverns()
        {
            if (_tavernIndices != null) return;
            _tavernIndices = new List<int>();
            foreach (var kv in _ctx.Buildings.All)
                if (kv.Value.Kind == BuildingKind.Tavern)
                    _tavernIndices.Add(kv.Key);
            _tavernIndices.Sort();      // deterministic order
        }

        void EnsureLandmarks()
        {
            if (_landmarkIndices != null) return;
            _landmarkIndices = new List<int>();
            foreach (var kv in _ctx.Buildings.All)
            {
                switch (kv.Value.Kind)
                {
                    case BuildingKind.Temple:
                    case BuildingKind.GuildHall:
                    case BuildingKind.Bank:
                    case BuildingKind.GeneralStore:
                    case BuildingKind.Library:
                    case BuildingKind.Palace:
                        _landmarkIndices.Add(kv.Key);
                        break;
                }
            }
            _landmarkIndices.Sort();
        }
    }
}
