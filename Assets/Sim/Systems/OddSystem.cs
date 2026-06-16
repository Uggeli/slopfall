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
        SimulationContext _ctx;
        bool _reDecideAll;

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
            ctx.Events.Subscribe<DawnSimEvent>(e => _reDecideAll = true);
            ctx.Events.Subscribe<DuskSimEvent>(e => _reDecideAll = true);
        }

        // Decisions only — arrivals, interrupts, and the activity clock are
        // ExecutionSystem's now (the sole writer of BehaviorRegistry).
        public void ProcessEvents() { }

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
                double currentRemaining = 0;

                BehaviorData behavior;
                if (_ctx.Behavior.TryGet(id, out behavior))
                {
                    if (behavior.Phase == ActivityPhase.Doing)
                    {
                        // ExecutionSystem advances the activity clock; here we
                        // only read it to decide whether to re-evaluate. Per-entity
                        // cap (48..79 min) staggers re-decisions into a trickle
                        // instead of town-wide lockstep waves.
                        currentRemaining = behavior.RemainingGameMinutes - gameMinutes;
                        double since = behavior.SinceDecisionGameMinutes + gameMinutes;
                        double cap = 48 + (Hash(id.Value, 0) & 0x1F);
                        if (currentRemaining <= 0 || since >= cap)
                            decide = true;
                    }
                    // Moving entities keep walking unless dawn/dusk re-decides.
                }
                else
                {
                    decide = true;
                }

                if (decide)
                    Decide(id, kv.Value, behavior, currentRemaining, clock.Hour, tick);
            }
        }

        void Decide(EntityId id, ResidencyData residency, BehaviorData current, double currentRemaining, int hour, long tick)
        {
            if (!_ctx.Needs.TryGet(id, out var needs)) return;
            if (!_ctx.Buildings.TryGet(residency.BuildingIndex, out var home)) return;
            if (!_ctx.Position.TryGet(id, out var pos)) return;

            // Personality shapes everything below; absent rows (unit harnesses)
            // behave like the old global constants.
            _ctx.Personality.TryGet(id, out var person);
            var w = person != null ? person.Weights : ActivityCatalog.Weights;
            double chronotype = person != null ? person.Trait(TraitIndex.Chronotype) : 0.5;
            bool night = IsNightFor(hour, chronotype);

            // Variety gates: foul weather empties the streets and fills the
            // taverns; festival days close the shops and double the revelry.
            var weather = _ctx.Weather.Current.Kind;
            bool wet = weather == WeatherKind.Rain || weather == WeatherKind.Thunder || weather == WeatherKind.Snow;
            bool gloomy = weather == WeatherKind.Overcast || weather == WeatherKind.Fog;
            double outdoor = wet ? 0.25 : (gloomy ? 0.7 : 1.0);
            double cozy = wet ? 1.3 : 1.0;
            bool holiday = _ctx.Holiday.CurrentId > 0;

            // Hysteresis: the activity in progress defends its slot with a
            // multiplicative bonus, or hourly re-evaluation flickers between
            // whichever deficit is momentarily largest (Atoms: enter-high /
            // exit-low; a challenger must clearly beat the incumbent).
            ActivityKind incumbent = (current != null && current.Phase == ActivityPhase.Doing)
                ? current.Activity : ActivityKind.None;
            int incumbentBuilding = current != null ? current.TargetBuilding : -2;
            const double Sticky = 1.4;

            // Prepotency hysteresis: an agent already in a leisure/growth activity
            // judges the cull at the higher Enter threshold (keep playing until a
            // deficiency clearly bites); otherwise at the lower Exit.
            var incumbentSpec = incumbent != ActivityKind.None ? ActivityCatalog.SpecFor(incumbent) : null;
            bool incumbentLeisure = incumbentSpec != null && incumbentSpec.Prepotent;

            // --- The marketplace: Collect (ActionDiscovery, precondition-filtered),
            // Score each ad uniformly with V, argmax. No hand-built candidate
            // blocks and no per-verb branching: a new action is a catalog row. ---
            // Membrane (S1): the decider reads a place partly through how it
            // interprets who is there — via the one SubjectiveSystem.Interpret.
            var sc = new ScoreContext
            {
                Needs = needs.V, W = w, Person = person,
                Hour = hour, Night = night, Wet = wet, Holiday = holiday,
                Outdoor = outdoor, Cozy = cozy, Prepotency = PrepotencyGate(needs.V, incumbentLeisure),
                Px = pos.X, Pz = pos.Z,
                Self = id,
                IsGuard = _ctx.Employment.TryGet(id, out var emp0) && emp0 != null && !emp0.PublicOwner.IsNone,
            };

            var ads = ActionDiscovery.GatherAds(_ctx, id);
            Ad best = default;
            double bestScore = double.NegativeInfinity;
            bool any = false;
            for (int i = 0; i < ads.Count; i++)
            {
                var ad = ads[i];
                double s = V(ad, sc);
                if (s <= 0) continue;
                if (ad.Verb == incumbent && ad.Building == incumbentBuilding) s *= Sticky;
                if (!any || s > bestScore) { any = true; bestScore = s; best = ad; }
            }

            ActivityKind bestKind = any ? best.Verb : ActivityKind.Idle;   // Idle always advertises, so `any` holds
            int bestBuilding = any ? best.Building : residency.BuildingIndex;
            float bestX = any ? best.X : pos.X;
            float bestZ = any ? best.Z : pos.Z;

            // Two activities whose "where" is a decision detail, not a fixed
            // place: Wander ambles to a deterministic random offset; Idle becomes
            // shelter-at-home when it's foul out (storms empty the streets).
            if (bestKind == ActivityKind.Wander)
            {
                uint h32 = Hash(id.Value, tick);
                bestX = pos.X + ((h32 & 0xFF) / 255f - 0.5f) * 60f;
                bestZ = pos.Z + (((h32 >> 8) & 0xFF) / 255f - 0.5f) * 60f;
                bestBuilding = -1;
            }
            else if (bestKind == ActivityKind.Idle && wet)
            {
                bestX = home.X; bestZ = home.Z; bestBuilding = residency.BuildingIndex;
            }
            else if (bestKind == ActivityKind.Flee && NearestThreat(id, pos.X, pos.Z, out float tx, out float tz))
            {
                // Run directly away from the nearest threat. Breaking the percept
                // is the real relief (the controller resets fear on percept-absence).
                float ax = pos.X - tx, az = pos.Z - tz;
                float mag = (float)System.Math.Sqrt(ax * ax + az * az);
                if (mag < 1e-3f) { ax = 1; az = 0; mag = 1; }   // on top of it: pick a direction
                bestX = pos.X + ax / mag * FleeDistance;
                bestZ = pos.Z + az / mag * FleeDistance;
                bestBuilding = -1;
            }
            else if (bestKind == ActivityKind.Attack)
            {
                // Close on the threat. A guard hunts the nearest creature region-
                // wide (proactive patrol); a civilian only chases one it perceives.
                float cx, cz; bool have;
                if (sc.IsGuard) have = NearestCreature(pos.X, pos.Z, out cx, out cz);
                else have = NearestThreat(id, pos.X, pos.Z, out cx, out cz);
                if (have) { bestX = cx; bestZ = cz; bestBuilding = -1; }
            }

            var bestSpec = ActivityCatalog.SpecFor(bestKind);

            // --- Commit the choice as an Intent; ExecutionSystem reifies it. ---
            // Same activity still winning mid-flight = a resume, not a restart:
            // keep the remaining duration and don't re-announce it.
            bool resume = current != null
                && current.Activity == bestKind
                && current.Phase == ActivityPhase.Doing
                && currentRemaining > 0
                && current.TargetBuilding == bestBuilding;

            // ±15% deterministic duration jitter — uniform durations re-align
            // the whole town to shared activity boundaries within a few hours.
            double duration = bestSpec.DurationMinutes * (0.85 + 0.3 * Hash01(id.Value, tick));

            _ctx.Intent.Set(id, new IntentData
            {
                Activity = bestKind,
                Building = bestBuilding,
                X = bestX,
                Z = bestZ,
                Resume = resume,
                Duration = duration,
            });
        }

        /// Per-decision context for V — the agent's drives, personality, and the
        /// world's modulators, computed once.
        struct ScoreContext
        {
            public double[] Needs, W;
            public PersonalityData Person;
            public int Hour;
            public bool Night, Wet, Holiday;
            public double Outdoor, Cozy, Prepotency;
            public float Px, Pz;
            public EntityId Self;            // whose subjective view this is (membrane reads through it)
            public bool IsGuard;             // on the public payroll — boosts Attack (hunting threats is the job)
        }

        /// V — the value function. Uniform over EVERY ad: read the activity's
        /// modulator data (Ad.Spec), build one gate from the applicable
        /// modulators, score gap×gate (+ base). No per-verb branching — an ad is
        /// an ad. Hard gates already filtered the ad in (preconditions, Collect).
        double V(Ad ad, ScoreContext c)
        {
            var s = ad.Spec;
            double traitFactor = s.Trait >= 0
                ? s.TraitBias + s.TraitScale * System.Math.Pow(TraitOf(c, s.Trait), s.TraitExp)
                : 1.0;

            double gate = s.BaseGate
                * (c.Night ? s.NightGate : s.DayGate)
                * (s.DistanceScale > 0 ? DistFactor(ad, c, s.DistanceScale) : 1.0)
                * (s.Outdoor ? c.Outdoor : 1.0)
                * (s.Social ? Liveliness(ad) * (c.Hour >= 17 ? 1.5 : 1.0) * c.Cozy : 1.0)
                * (s.Prepotent ? c.Prepotency : 1.0)
                * (s.FearDriven ? DesperationFactor(c.Needs, NeedAxis.Fear) : 1.0)
                * (s.Kind == ActivityKind.Attack && c.IsGuard ? GuardCombatBoost : 1.0)
                * (s.RelationSensitive ? RelationFactor(RegardFieldAt(ad.Building, c)) : 1.0)
                * ConscienceFactor(_ctx.Conscience.ChargeFor(c.Self, ad.Verb))
                * (c.Holiday && s.HolidayFactor != 1.0 ? s.HolidayFactor : 1.0)
                * (s.TraitOnBase ? 1.0 : traitFactor);

            double baseRaw = s.BaseUtility * (s.TraitOnBase ? traitFactor : 1.0);
            double baseTotal = baseRaw + (c.Night ? s.NightBase : 0) + (c.Wet ? s.WetBase : 0);
            double effectiveBase = s.Growth ? baseTotal * gate : baseTotal;   // growth: base carries the score
            return OddScore.Compute(c.Needs, s.Delta, c.W, gate, effectiveBase);
        }

        double Liveliness(Ad ad)
            => ad.Building < 0 ? 1.0 : 1.0 + 0.04 * System.Math.Min(_ctx.Occupancy.PlaceCount(ad.Building), 8);

        // L3/S1 placeholders (FROZEN — no tuning until the substrate lands).
        const double RelationGain = 0.5, RelationFloor = 0.5, RelationCeil = 1.5;

        /// The social drive's TARGET FIELD, projected to a scalar (drive doc): a
        /// directed drive mints a contribution per perceived other and collapses
        /// the field by its UrgencyProjection. social = Sum, so a crowd of bonded
        /// others adds up — two friends pull harder than one — unlike fear = Max
        /// (the worst threat dominates). Each occupant contributes its interpreted
        /// valence (the one membrane SubjectiveSystem.Interpret, self excluded), so
        /// when S3 sources valence from MEANINGS this benefits automatically.
        /// RelationFactor then saturates the result, so a big crowd can't blow up
        /// the score. (Occupancy is last tick's — perceptual staleness, acceptable.)
        /// Inline (alloc-free hot path); mirrors the pure ProjectField operator.
        double RegardFieldAt(int building, ScoreContext c)
        {
            if (building < 0) return 0.0;
            var occ = _ctx.Occupancy.OccupantsOf(building);
            bool sum = DriveCatalog.Defs[NeedAxis.SocialDef].Projection == UrgencyProjection.Sum;
            double acc = 0; bool any = false;
            for (int i = 0; i < occ.Count; i++)
            {
                if (occ[i] == c.Self) continue;
                double v = SubjectiveSystem.Interpret(_ctx, c.Self, occ[i]).Valence;
                if (!any) { acc = v; any = true; }
                else if (sum) acc += v;
                else if (v > acc) acc = v;
            }
            return any ? acc : 0.0;
        }

        /// The urgency-projection operator (drive doc) in pure form: collapse a
        /// directed drive's target field to a scalar by Sum (contributions add) or
        /// Max (the strongest wins). RegardFieldAt is the alloc-free inline twin.
        public static double ProjectField(System.Collections.Generic.IReadOnlyList<double> contribs, UrgencyProjection proj)
        {
            if (contribs == null || contribs.Count == 0) return 0.0;
            double acc = contribs[0];
            for (int i = 1; i < contribs.Count; i++)
            {
                if (proj == UrgencyProjection.Sum) acc += contribs[i];
                else if (contribs[i] > acc) acc = contribs[i];
            }
            return acc;
        }

        /// Color a place's value by regard for its company: a multiplier in
        /// [Floor, Ceil] that tilts the choice but can't flip the score's sign or
        /// swamp the need gap (keeps prepotency/needs dominant). Pure for testing.
        public static double RelationFactor(double meanRegard)
        {
            double f = 1.0 + RelationGain * meanRegard;
            return f < RelationFloor ? RelationFloor : (f > RelationCeil ? RelationCeil : f);
        }

        const double ShameFloor = 0.05;

        /// A conscience charge on a verb penalises it at the marketplace join
        /// (Atoms: the superego as a sign-opposed valence source — it biases V,
        /// it does not choose). Graded — a strongly-shamed action scores a small
        /// fraction of its drive value, so a proud agent would rather do almost
        /// anything else (the proud-starves-rather-than-beg case). Pure for testing.
        public static double ConscienceFactor(double charge)
        {
            double f = 1.0 - charge;
            return f < ShameFloor ? ShameFloor : (f > 1.0 ? 1.0 : f);
        }

        // Escape-affordability (the desperation edge hunger ⊣ fear): starvation
        // grades the fear response down, so a starving agent can't afford to run.
        // FROZEN placeholders.
        const double DesperationStrength = 0.7;   // at max hunger, fear's urgency is cut to ~0.3
        const double DesperationFloor = 0.1;      // never fully silenced
        const double GuardCombatBoost = 4.0;      // a guard's Attack pull — hunting threats is the job

        /// The fear-urgency multiplier from the DesperationGraded edges into an
        /// axis (table-driven via DriveGraph). One edge today: hunger ⊣ fear.
        public static double DesperationFactor(double[] needs, int axis)
        {
            var sources = DriveGraph.DesperationSourcesByTarget[axis];
            double f = 1.0;
            for (int i = 0; i < sources.Length; i++)
                f *= 1.0 - DesperationStrength * (needs[sources[i]] / ActivityCatalog.VMax);
            return f < DesperationFloor ? DesperationFloor : f;
        }

        const float FleeDistance = 30f;   // how far the agent bolts per flee leg

        /// The position of the nearest perceived threat (a creature in the agent's
        /// subjective view), or false if none. The fear response runs from this.
        bool NearestThreat(EntityId self, float sx, float sz, out float tx, out float tz)
        {
            tx = 0; tz = 0;
            if (!_ctx.Subjective.TryGet(self, out var view) || view == null) return false;
            float best = float.MaxValue; bool found = false;
            var reads = view.Entities;
            for (int i = 0; i < reads.Count; i++)
            {
                if (reads[i].Threat <= 0) continue;
                if (!_ctx.Position.TryGet(reads[i].Other, out var tp) || tp == null) continue;
                float dx = tp.X - sx, dz = tp.Z - sz;
                float d2 = dx * dx + dz * dz;
                if (d2 < best) { best = d2; tx = tp.X; tz = tp.Z; found = true; }
            }
            return found;
        }

        /// The position of the nearest creature region-wide (CreatureRegistry) —
        /// a guard's hunt target, seen or not. Deterministic id tie-break.
        bool NearestCreature(float sx, float sz, out float tx, out float tz)
        {
            tx = 0; tz = 0;
            float best = float.MaxValue; bool found = false; int bestId = int.MaxValue;
            foreach (var kv in _ctx.Creatures.All)
            {
                if (!_ctx.Position.TryGet(kv.Key, out var tp) || tp == null) continue;
                float dx = tp.X - sx, dz = tp.Z - sz;
                float d2 = dx * dx + dz * dz;
                if (d2 < best || (d2 == best && kv.Key.Value < bestId))
                { best = d2; tx = tp.X; tz = tp.Z; bestId = kv.Key.Value; found = true; }
            }
            return found;
        }

        static double TraitOf(ScoreContext c, int idx) => c.Person != null ? c.Person.Trait(idx) : 0.5;

        static double DistFactor(Ad ad, ScoreContext c, double scale)
        {
            float dx = ad.X - c.Px, dz = ad.Z - c.Pz;
            double dist = System.Math.Sqrt(dx * dx + dz * dz);
            return 1.0 / (1.0 + dist / scale);
        }

        /// Prepotency (Atoms, two-regime): leisure only wins when deficiency
        /// drives are quiet — graded suppression as the loudest deficiency
        /// rises, hard cull at the threshold ("a starving bunny can't binky").
        /// The deficiency sources are read from the table (DriveGraph.HardCullSources
        /// = hunger + energy + fear today), so adding a prepotent pole extends the
        /// gate for free.
        ///
        /// Hysteresis (V2b): the cull threshold depends on whether the agent is
        /// CURRENTLY in leisure — it must clearly cross the higher Enter to START
        /// suppressing, and fall back below the lower Exit to STOP, so behavior
        /// doesn't flicker at the boundary.
        public const double CullEnterThreshold = 0.7;
        public const double CullExitThreshold = 0.6;

        public static double PrepotencyGate(double[] v, bool incumbentLeisure)
        {
            double loudest = 0;
            var sources = DriveGraph.HardCullSources;
            for (int i = 0; i < sources.Length; i++)
                if (v[sources[i]] > loudest) loudest = v[sources[i]];
            double threshold = incumbentLeisure ? CullEnterThreshold : CullExitThreshold;
            return loudest >= threshold ? 0 : 1.0 - loudest / threshold;
        }

        static bool IsNight(int hour) => hour >= 21 || hour < 6;

        /// Chronotype shifts the personal night window: larks (0) live
        /// 19:30–04:30, owls (1) live 22:30–07:30, the center matches the old
        /// global 21–06.
        public static bool IsNightFor(int hour, double chronotype)
        {
            double shift = (chronotype - 0.5) * 3.0;            // -1.5 .. +1.5 h
            double h = hour + 0.5;                               // mid-hour sample
            double nightStart = 21 + shift, nightEnd = 6 + shift;
            return h >= nightStart || h < nightEnd;
        }

        static uint Hash(int idValue, long tick)
        {
            uint x = (uint)(idValue * 2654435761u) ^ (uint)(tick * 40503u);
            x ^= x >> 13; x *= 0x5bd1e995; x ^= x >> 15;
            return x;
        }

        static double Hash01(int idValue, long tick) => Hash(idValue, tick) / (double)uint.MaxValue;
    }
}
