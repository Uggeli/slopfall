using System.Collections.Generic;
using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of DaggerfallWorkshop.Sim.OddSystem — the decision core (ODD
    // wedge v1). Per civilian, when the current activity ends (or dawn/dusk forces a
    // rethink), gather candidate activities from town context and argmax the propagated
    // ODD gap score, then Publish a single IntentSetIntent. Holds NO tick state: reads
    // the Engine registries read-only, decides per agent (independent → parallel-safe),
    // and emits whole-value intents. IntentRegistry is the sole applier.
    //
    // The legacy system reached into a SimulationContext for two helpers —
    // ActionDiscovery.GatherAds and SubjectiveSystem.Interpret — both hard-bound to the
    // legacy registry surface (SimulationContext). They are reproduced here INLINE
    // against the Engine registries (the same move the Engine NeedsSystem makes for its
    // ctx-bound bits), preserving the discovery + interpretation logic EXACTLY. The pure
    // data helpers (OddTree, OddScore, ActivityCatalog, AffordanceCatalog, ActionCatalog,
    // GoodsCatalog, DriveGraph, DriveCatalog, NeedAxis, TraitIndex) resolve unqualified
    // from the enclosing DaggerfallWorkshop.Sim namespace and are reused verbatim.
    //
    // Cadence: the _reDecideAll flag is gone. A dawn/dusk re-think is read from this
    // tick's DawnEvent/DuskEvent batch (GetEvents); absent that, each agent re-decides
    // on the same per-agent schedule the old logic used (activity expiry + staggered cap).
    public sealed class OddSystem : SimSystem
    {
        readonly ResidencyRegistry _residency;
        readonly BehaviorRegistry _behavior;
        readonly NeedsRegistry _needs;
        readonly BuildingRegistry _buildings;
        readonly PositionRegistry _position;
        readonly PersonalityRegistry _personality;
        readonly WeatherRegistry _weather;
        readonly HolidayRegistry _holiday;
        readonly EmploymentRegistry _employment;
        readonly CoinRegistry _coin;
        readonly LarderRegistry _larder;
        readonly OccupancyRegistry _occupancy;
        readonly ConscienceRegistry _conscience;
        readonly SubjectiveViewRegistry _subjective;
        readonly CreatureRegistry _creatures;
        readonly AffectsRegistry _affects;
        readonly RelationsRegistry _relations;
        readonly MeaningsRegistry _meanings;
        readonly PlaceMemoryRegistry _placeMemory;
        readonly AgentMemoryRegistry _agentMemory;
        readonly StockRegistry _stock;
        readonly ItemRegistry _items;
        readonly WorldClockRegistry _worldClock;
        readonly SharedActivityRegistry _shared;
        readonly int _seed;

        public OddSystem(
            EventBus events,
            ResidencyRegistry residency,
            BehaviorRegistry behavior,
            NeedsRegistry needs,
            BuildingRegistry buildings,
            PositionRegistry position,
            PersonalityRegistry personality,
            WeatherRegistry weather,
            HolidayRegistry holiday,
            EmploymentRegistry employment,
            CoinRegistry coin,
            LarderRegistry larder,
            OccupancyRegistry occupancy,
            ConscienceRegistry conscience,
            SubjectiveViewRegistry subjective,
            CreatureRegistry creatures,
            AffectsRegistry affects,
            RelationsRegistry relations,
            MeaningsRegistry meanings,
            PlaceMemoryRegistry placeMemory,
            AgentMemoryRegistry agentMemory,
            StockRegistry stock,
            ItemRegistry items,
            WorldClockRegistry worldClock,
            SharedActivityRegistry shared,
            int seed) : base(events)
        {
            _residency = residency;
            _behavior = behavior;
            _needs = needs;
            _buildings = buildings;
            _position = position;
            _personality = personality;
            _weather = weather;
            _holiday = holiday;
            _employment = employment;
            _coin = coin;
            _larder = larder;
            _occupancy = occupancy;
            _conscience = conscience;
            _subjective = subjective;
            _creatures = creatures;
            _affects = affects;
            _relations = relations;
            _meanings = meanings;
            _placeMemory = placeMemory;
            _agentMemory = agentMemory;
            _stock = stock;
            _items = items;
            _worldClock = worldClock;
            _shared = shared;
            _seed = seed;
        }

        public override void Update(long tick)
        {
            var clock = _worldClock.Current;
            if (clock.Year == 0) return;

            double gameMinutes = clock.DeltaGameSeconds / 60.0;

            // Dawn/dusk forces a town-wide rethink — read this tick's signal batch
            // instead of the old _reDecideAll flag set by an event subscription.
            bool reDecideAll =
                Events.GetEvents<DawnEvent>().Length > 0 || Events.GetEvents<DuskEvent>().Length > 0;

            // Build hit set once per tick: sleeping agents hit this tick must wake.
            var hitThisTick = new HashSet<EntityId>();
            foreach (ref readonly var d in Events.GetEvents<DamageEvent>())
                hitThisTick.Add(d.Target);

            foreach (var kv in _residency.All)
            {
                var id = kv.Key;
                bool decide = reDecideAll;
                double currentRemaining = 0;

                BehaviorData behavior;
                if (_behavior.TryGet(id, out behavior))
                {
                    // Sleep carve-out: sleeping agents suppress percept-cadence preemption;
                    // they wake ONLY on a hit, the dawn/dusk rethink, duration expiry, or
                    // the staggered cap. The continue below ensures preemptTick never fires.
                    if (behavior.Activity == ActivityKind.Sleep)
                    {
                        double sinceSleep = behavior.SinceDecisionGameMinutes + gameMinutes;
                        double capSleep = 48 + (Hash(id.Value, 0) & 0x1F);
                        bool wake = SleepShouldWake(
                            wasHit: hitThisTick.Contains(id),
                            reDecideAll: reDecideAll,
                            durationExpired: behavior.RemainingGameMinutes - gameMinutes <= 0,
                            capReached: sinceSleep >= capSleep);
                        if (wake)
                            Decide(id, kv.Value, behavior, behavior.RemainingGameMinutes - gameMinutes, clock.Hour, tick);
                        continue;   // percept-cadence preemption never reaches a sleeper
                    }

                    // Served-but-still-Queued window: during the one-tick promotion,
                    // _shared.IsServed(id) is true while Phase is still Queued (SharedActivitySystem
                    // flips it to Doing this tick). Deciding now would race that flip, so skip.
                    // ONLY for Queued agents — a Doing agent that is still IsServed MUST still
                    // re-decide on expiry to complete service and free the slot.
                    if (behavior.Phase == ActivityPhase.Queued && _shared.IsServed(id))
                        continue;

                    if (behavior.Phase == ActivityPhase.Doing)
                    {
                        currentRemaining = behavior.RemainingGameMinutes - gameMinutes;
                        double since = behavior.SinceDecisionGameMinutes + gameMinutes;
                        double cap = 48 + (Hash(id.Value, 0) & 0x1F);
                        if (currentRemaining <= 0 || since >= cap)
                            decide = true;
                    }
                    else if (behavior.Phase == ActivityPhase.Queued)
                    {
                        // Cadence-limited re-decide ONLY: trigger on the staggered preemptTick.
                        // The since>=cap trigger is intentionally gone — nothing advances a
                        // Queued agent's SinceDecisionGameMinutes (ExecutionSystem's countdown
                        // only touches Doing agents), so since>=cap could become permanently
                        // true and, with the bare-return stay path, would re-decide every tick.
                        // preemptTick caps re-decides at one per PreemptEveryTicks; dawn/dusk
                        // still forces a rethink via reDecideAll above.
                        int stagger = (int)(Hash(id.Value, 1) % PreemptEveryTicks);
                        bool preemptTick = (tick % PreemptEveryTicks) == stagger;
                        if (preemptTick)
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
            if (!_needs.TryGet(id, out var needs)) return;
            if (!_buildings.TryGet(residency.BuildingIndex, out var home) || home == null) return;
            if (!_position.TryGet(id, out var pos) || pos == null) return;

            _personality.TryGet(id, out var person);
            var w = person != null ? person.Weights : ActivityCatalog.Weights;
            double chronotype = person != null ? person.Trait(TraitIndex.Chronotype) : 0.5;
            bool night = IsNightFor(hour, chronotype);

            var weather = _weather.Current.Kind;
            bool wet = weather == WeatherKind.Rain || weather == WeatherKind.Thunder || weather == WeatherKind.Snow;
            bool gloomy = weather == WeatherKind.Overcast || weather == WeatherKind.Fog;
            double outdoor = wet ? 0.25 : (gloomy ? 0.7 : 1.0);
            double cozy = wet ? 1.3 : 1.0;
            bool holiday = _holiday.CurrentId > 0;

            ActivityKind incumbent = (current != null && current.Phase == ActivityPhase.Doing)
                ? current.Activity : ActivityKind.None;
            int incumbentBuilding = current != null ? current.TargetBuilding : -2;
            const double Sticky = 1.4;

            var incumbentSpec = incumbent != ActivityKind.None ? ActivityCatalog.SpecFor(incumbent) : null;
            bool incumbentLeisure = incumbentSpec != null && incumbentSpec.Prepotent;

            var sc = new ScoreContext
            {
                Needs = needs.V, W = w, Person = person,
                Hour = hour, Night = night, Wet = wet, Holiday = holiday,
                Outdoor = outdoor, Cozy = cozy, Prepotency = PrepotencyGate(needs.V, incumbentLeisure),
                Px = pos.X, Pz = pos.Z,
                Self = id,
                IsGuard = _employment.TryGet(id, out var emp0) && emp0 != null && !emp0.PublicOwner.IsNone,
            };

            var ads = GatherAds(id);
            if (sc.IsGuard) InjectGuardAds(id, sc, ads, hour);

            var verbs = new List<ActivityKind>();
            var verbAd = new List<Ad>();
            var verbScore = new List<double>();
            var verbIndex = new Dictionary<ActivityKind, int>();
            for (int i = 0; i < ads.Count; i++)
            {
                var ad = ads[i];
                double s = V(ad, sc);
                if (s <= 0 && ActionCatalog.Enables(ad.Verb).Length == 0) continue;
                if (ad.Verb == incumbent && ad.Building == incumbentBuilding) s *= Sticky;
                if (verbIndex.TryGetValue(ad.Verb, out int vi))
                {
                    if (s > verbScore[vi]) { verbScore[vi] = s; verbAd[vi] = ad; }
                }
                else
                {
                    verbIndex[ad.Verb] = verbs.Count;
                    verbs.Add(ad.Verb); verbAd.Add(ad); verbScore.Add(s);
                }
            }

            double coin = _coin.Get(id);
            var roots = new List<int>();
            for (int vi = 0; vi < verbs.Count; vi++)
            {
                var ad = verbAd[vi];
                var spec = ad.Spec;
                double saleCost = spec != null && spec.SaleReliefAxis >= 0 && spec.SalePrice > 0
                    ? spec.SaleUnits * spec.SalePrice : 0;
                bool larderGated = spec != null && spec.LarderGated;
                double larder = larderGated ? _larder.Get(ad.Building) : 1;
                if (!IsRootEligible(coin, saleCost, larderGated, larder, SaleStockAvailable(ad))) continue;
                roots.Add(vi);
            }

            var buffer = new OddNode[verbs.Count + 1];
            int n = OddTree.Build(buffer, verbs.Count, roots,
                a => EnabledIndices(verbs[a], verbIndex),
                _ => true,
                a => verbScore[a]);
            OddTree.Propagate(buffer, n, LookaheadDecay);
            int winner = OddTree.Traverse(buffer, n);
            // F2: terminal revalidation. Traverse returns a root verb; re-validate its preconditions
            // against THIS tick (stock/larder/coin can have changed since Build) and drop to the
            // next-best valid root, else Object Zero (winner == -1 falls through below).
            winner = RevalidateWinner(winner, verbAd, verbScore, roots, coin);
            if (SnapshotEnabled && (n > roots.Count + 1 || !Snapshots.ContainsKey(id)))
                Snapshots[id] = FormatTree(buffer, n, verbs);
            if (SnapshotWatch.ContainsKey(id))
                StructuredSnapshots[id] = BuildSnapshot(
                    buffer, n, verbs, _worldClock.Current.Hour, _worldClock.Current.Minute);

            bool any = winner >= 0;
            Ad best = any ? verbAd[winner] : default;

            ActivityKind bestKind = any ? best.Verb : ActivityKind.Idle;
            int bestBuilding = any ? best.Building : residency.BuildingIndex;
            float bestX = any ? best.X : pos.X;
            float bestZ = any ? best.Z : pos.Z;
            ItemId bestItem = any ? best.Item : ItemId.None;

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
                float ax = pos.X - tx, az = pos.Z - tz;
                float mag = (float)System.Math.Sqrt(ax * ax + az * az);
                if (mag < 1e-3f) { ax = 1; az = 0; mag = 1; }
                bestX = pos.X + ax / mag * FleeDistance;
                bestZ = pos.Z + az / mag * FleeDistance;
                bestBuilding = -1;
            }
            else if (bestKind == ActivityKind.Attack)
            {
                float cx, cz; bool have;
                if (sc.IsGuard) have = NearestCreature(pos.X, pos.Z, out cx, out cz);
                else have = NearestThreat(id, pos.X, pos.Z, out cx, out cz);
                if (have) { bestX = cx; bestZ = cz; bestBuilding = -1; }
            }

            var bestSpec = ActivityCatalog.SpecFor(bestKind);

            bool resume = current != null
                && current.Activity == bestKind
                && current.Phase == ActivityPhase.Doing
                && currentRemaining > 0
                && current.TargetBuilding == bestBuilding;

            double duration = bestSpec.DurationMinutes * (0.85 + 0.3 * Hash01(id.Value, tick));

            if (current != null && _shared.AnchorOf(id, out var heldAnchor))
            {
                // Same shop+Buy always keeps its place; otherwise stay unless the winner is
                // meaningfully better than continuing to wait (hysteresis = balking gate).
                bool stay = ShouldStayInQueue(true, heldAnchor.Building, bestKind, bestBuilding);
                if (!stay)
                {
                    double currentScore = verbIndex.TryGetValue(current.Activity, out var ci) ? verbScore[ci] : 0.0;
                    // winner == -1 when no ad scored positively (e.g. a queued buyer who can
                    // no longer afford Buy and nothing else scored). Guard the lookup.
                    double winnerScore = winner >= 0 ? verbScore[winner] : 0.0;
                    stay = !ShouldSwitchCommitment(true, currentScore, winnerScore, Hysteresis);
                }
                if (stay)
                {
                    // Stay in line: publish NOTHING. SharedActivitySystem is the SOLE writer
                    // of a Queued agent's BehaviorData (slot placement + the served→Doing
                    // promotion flip). If OddSystem re-committed the row here it would race
                    // those writes in the same tick (BehaviorRegistry applies BehaviorSetIntent
                    // last-write-wins in nondeterministic parallel order). A bare return cannot
                    // storm because the Queued re-decide branch in Update is cadence-limited to
                    // preemptTick (every PreemptEveryTicks) — nothing advances a Queued agent's
                    // SinceDecisionGameMinutes (ExecutionSystem's countdown only touches Doing).
                    return;
                }
                Events.Publish(new QueueLeaveIntent { Agent = id });   // leave the line, fall through
            }

            Events.Publish(new IntentSetIntent
            {
                Id = id,
                Data = new IntentData
                {
                    Activity = bestKind,
                    Building = bestBuilding,
                    X = bestX,
                    Z = bestZ,
                    Resume = resume,
                    Duration = duration,
                    Item = bestItem,
                },
            });
        }

        // F2: re-validate the chosen winner against the current tick. Reuses IsRootEligible
        // (the same gate that built the root set). If the winner is stale, pick the highest-
        // scoring root still valid now; -1 => caller falls to Object Zero (already handled).
        int RevalidateWinner(int winner, List<Ad> verbAd, List<double> verbScore, List<int> roots, double coin)
        {
            if (winner < 0) return winner;
            if (RootValidNow(verbAd[winner], coin)) return winner;
            int best = -1; double bestScore = double.NegativeInfinity;
            for (int i = 0; i < roots.Count; i++)
            {
                int vi = roots[i];
                if (!RootValidNow(verbAd[vi], coin)) continue;
                if (verbScore[vi] > bestScore) { bestScore = verbScore[vi]; best = vi; }
            }
            return best;
        }

        bool RootValidNow(Ad ad, double coin)
        {
            var spec = ad.Spec;
            double saleCost = spec != null && spec.SaleReliefAxis >= 0 && spec.SalePrice > 0
                ? spec.SaleUnits * spec.SalePrice : 0;
            bool larderGated = spec != null && spec.LarderGated;
            double larder = larderGated ? _larder.Get(ad.Building) : 1;
            return IsRootEligible(coin, saleCost, larderGated, larder, SaleStockAvailable(ad));
        }

        // --- Inline reproduction of ActionDiscovery.GatherAds against the Engine
        // registries (logic preserved verbatim; see ActionDiscovery in the parent
        // namespace for the annotated original). ---

        List<Ad> GatherAds(EntityId agent)
        {
            var ads = new List<Ad>();

            float px = 0f, pz = 0f;
            if (_position.TryGet(agent, out var pos) && pos != null) { px = pos.X; pz = pos.Z; }
            foreach (var verb in AffordanceCatalog.Innate)
                ads.Add(new Ad { Verb = verb, Building = -1, X = px, Z = pz, Spec = ActivityCatalog.SpecFor(verb) });

            // Jobs-at-workplace — gathered BEFORE building ads on purpose (Enables-DAG
            // ordering; see ActionDiscovery).
            if (_employment.TryGet(agent, out var emp) && emp != null && !emp.Employer.IsNone
                && _residency.TryGet(emp.Employer, out var empRes) && empRes != null
                && _buildings.TryGet(empRes.BuildingIndex, out var empBldg) && empBldg != null)
            {
                var verb = empBldg.Kind == BuildingKind.Farm ? ActivityKind.Farm
                         : empBldg.Kind == BuildingKind.Fishery ? ActivityKind.Fish
                         : empBldg.Kind == BuildingKind.Mine ? ActivityKind.Mine
                         : empBldg.Kind == BuildingKind.Pasture
                           || empBldg.Kind == BuildingKind.Weaver
                           || empBldg.Kind == BuildingKind.ClothingStore ? ActivityKind.Weave
                         : ActivityKind.Labor;
                ads.Add(new Ad
                {
                    Verb = verb, Building = empRes.BuildingIndex,
                    X = empBldg.X, Z = empBldg.Z, Spec = ActivityCatalog.SpecFor(verb),
                });
            }

            foreach (var building in _placeMemory.Known(agent))
                Discover(agent, building, ads);

            var carried = new List<ItemId>();
            _items.CarriedBy(agent, carried);
            if (carried.Count > 0)
            {
                int homeB = _residency.TryGet(agent, out var hres) && hres != null ? hres.BuildingIndex : -1;
                float hx = px, hz = pz;
                if (homeB >= 0 && _buildings.TryGet(homeB, out var hb) && hb != null) { hx = hb.X; hz = hb.Z; }
                for (int i = 0; i < carried.Count; i++)
                {
                    if (!_items.TryGet(carried[i], out var item) || item == null) continue;
                    if (item.Edible > 0)
                        ads.Add(new Ad { Verb = ActivityKind.UseItem, Building = -1, X = px, Z = pz,
                                         Spec = ActivityCatalog.SpecFor(ActivityKind.UseItem), Item = carried[i] });
                    if (homeB >= 0)
                        ads.Add(new Ad { Verb = ActivityKind.StoreItem, Building = homeB, X = hx, Z = hz,
                                         Spec = ActivityCatalog.SpecFor(ActivityKind.StoreItem), Item = carried[i] });
                }
            }

            int adHour = _worldClock.Current.Hour;
            bool adHoliday = _holiday.CurrentId > 0;
            bool isKeeper = _residency.TryGet(agent, out var res) && res != null && res.Role == ResidentRole.Keeper;
            // F1: stock no longer culls the pool — it gates ROOT-eligibility (see Decide).
            // Only structural preconditions (hours/holiday/keeper) remove an ad here, so
            // stock-gated ads stay in verbs/verbIndex for Enables propagation.
            ads.RemoveAll(a => !ActivityCatalog.PreconditionsMet(a.Spec, adHour, adHoliday, isKeeper));

            return ads;
        }

        // Dynamic Object Zero (guards): role/context-aware ads injected into the
        // marketplace before consolidation. Posting/patrol/interception/abandonment
        // all emerge from scoring — no state machine.
        //
        // GuardDutyUtility is calibrated to beat the innate Attack ad under the guard's
        // constant vigilance-floor fear (GuardCombatBoost=4 × VigilanceFloor=0.15 →
        // routine Attack ≈ 0.035-0.06), so the guard holds post over idle combat-stance.
        // GuardAttackFloor (0.02) < GuardDutyUtility (0.065): floor alone does NOT beat duty,
        // so a distant creature is ignored; only when proximity bonus pushes the total above
        // duty (within ~3 m) does the guard break post to engage — intercept at the gate,
        // not on sight. Range: [0.02, 0.08] with GuardAttackGain=0.06.
        const double GuardDutyUtility  = 0.065; // beats routine vigilance-floor Attack; see note above
        const double GuardAttackFloor  = 0.02;  // below duty: guard holds post; only engages when creature is within ~3m (at the gate)
        const double GuardAttackGain   = 0.06;  // proximity bonus adds urgency; nearest creature preferred
        const float  GuardSenseRange   = 12f;   // matches SenseSystem sight radius

        const int    PreemptEveryTicks = 5;     // re-value queued commitments ~every 5 ticks (tunable)
        const double Hysteresis        = 0.15;  // winner must beat current by 15% to balk (anti-thrash)

        void InjectGuardAds(EntityId id, ScoreContext sc, List<Ad> ads, int hour)
        {
            if (!_employment.TryGet(id, out var emp) || emp == null || emp.PublicOwner.IsNone) return;
            bool civicNight = hour < 6 || hour >= 18;

            // On-shift duty ad at the assigned gate post (skip if this guard has no gate).
            // Uses GuardDutyUtility (not the catalog BaseUtility) so the duty ad beats the
            // innate Attack score that GuardCombatBoost + vigilance-floor fear produces.
            if (emp.GateIndex >= 0 && emp.NightShift == civicNight)
            {
                var verb = civicNight ? ActivityKind.StandWatch : ActivityKind.Patrol;
                var catalogSpec = ActivityCatalog.SpecFor(verb);
                ads.Add(new Ad
                {
                    Verb = verb, Building = -1, X = emp.GateX, Z = emp.GateZ,
                    Spec = new ActivityCatalog.Spec
                    {
                        Kind = verb,
                        DurationMinutes = catalogSpec.DurationMinutes,
                        BaseUtility = GuardDutyUtility,
                    },
                });
            }

            // Sensed creature → proximity-boosted Attack ad. Δ=0 so it scores purely
            // on BaseUtility (independent of the guard's own Fear): floor + proximity,
            // always above duty. Targeting is resolved to NearestCreature downstream.
            if (_subjective.TryGet(id, out var view) && view != null
                && _position.TryGet(id, out var gp) && gp != null)
            {
                float best2 = GuardSenseRange * GuardSenseRange;
                bool have = false; float tx = 0, tz = 0;
                for (int i = 0; i < view.Entities.Count; i++)
                {
                    var e = view.Entities[i];
                    if (e.Threat <= 0) continue;
                    if (!_position.TryGet(e.Other, out var cp) || cp == null) continue;
                    float dx = cp.X - gp.X, dz = cp.Z - gp.Z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 < best2) { best2 = d2; tx = cp.X; tz = cp.Z; have = true; }
                }
                if (have)
                {
                    double prox = 1.0 - System.Math.Sqrt(best2) / GuardSenseRange;   // 0..1, 1 at point-blank
                    double util = GuardAttackFloor + prox * GuardAttackGain;
                    ads.Add(new Ad
                    {
                        Verb = ActivityKind.Attack, Building = -1, X = tx, Z = tz,
                        Spec = new ActivityCatalog.Spec
                        {
                            Kind = ActivityKind.Attack,
                            DurationMinutes = 10,
                            BaseUtility = util,
                        },
                    });
                }
            }
        }

        void Discover(EntityId agent, int buildingIndex, List<Ad> into)
        {
            if (!_buildings.TryGet(buildingIndex, out var b) || b == null) return;

            bool own = _residency.TryGet(agent, out var res) && res != null && res.BuildingIndex == buildingIndex;
            if (own)
            {
                foreach (var verb in AffordanceCatalog.Home)
                    Offer(into, verb, buildingIndex, b);
                if (res.Role == ResidentRole.Keeper)
                    foreach (var verb in AffordanceCatalog.Workplace)
                        Offer(into, verb, buildingIndex, b);
            }
            else
            {
                foreach (var verb in AffordanceCatalog.Public(b.Kind))
                    Offer(into, verb, buildingIndex, b);
            }
        }

        bool SaleStockAvailable(Ad a)
        {
            var spec = a.Spec;
            if (spec == null || spec.SaleReliefAxis < 0) return true;
            if (a.Building < 0 || !_buildings.TryGet(a.Building, out var b) || b == null) return true;
            if (!GoodsCatalog.SaleGoodFor(b.Kind, spec.Kind, out var good)) return false;
            return _stock.Get(a.Building, good) > 0;
        }

        static void Offer(List<Ad> into, ActivityKind verb, int building, BuildingRow b)
        {
            into.Add(new Ad
            {
                Verb = verb,
                Building = building,
                X = b.X,
                Z = b.Z,
                Spec = ActivityCatalog.SpecFor(verb),
            });
        }

        // --- ODD planner glue (unchanged from the legacy decider). ---

        public const double LookaheadDecay = 0.6;

        static IReadOnlyList<int> EnabledIndices(ActivityKind verb, Dictionary<ActivityKind, int> index)
        {
            var en = ActionCatalog.Enables(verb);
            if (en.Length == 0) return System.Array.Empty<int>();
            var list = new List<int>(en.Length);
            for (int i = 0; i < en.Length; i++)
                if (index.TryGetValue(en[i], out int vi)) list.Add(vi);
            return list;
        }

        // --- Decision snapshot (observability), unchanged. ---
        public static bool SnapshotEnabled = false;
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<EntityId, string> Snapshots
            = new System.Collections.Concurrent.ConcurrentDictionary<EntityId, string>();

        // --- Structured decision snapshot for the live viewer (observability). ---
        // Watched ids are set by the web layer; only watched agents pay the capture cost.
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<EntityId, byte> SnapshotWatch
            = new System.Collections.Concurrent.ConcurrentDictionary<EntityId, byte>();
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<EntityId, OddSnapshotData> StructuredSnapshots
            = new System.Collections.Concurrent.ConcurrentDictionary<EntityId, OddSnapshotData>();

        /// Project the BFS OddNode buffer into a flat, serializable snapshot. Node 0 is
        /// the root sentinel (Verb=null). Chosen mirrors Traverse: the highest-Total
        /// root child (first one wins ties), as a node index; -1 if the root has none.
        public static OddSnapshotData BuildSnapshot(
            OddNode[] buf, int n, IReadOnlyList<ActivityKind> verbs, int hour, int minute)
        {
            var nodes = new OddSnapNode[n];
            for (int i = 0; i < n; i++)
            {
                var node = buf[i];
                nodes[i] = new OddSnapNode
                {
                    Parent = node.ParentIndex,
                    Verb = node.Action >= 0 ? verbs[node.Action].ToString() : null,
                    Direct = node.DirectScore,
                    Prop = node.PropagatedScore,
                    Total = node.Total,
                    Terminal = node.IsTerminal,
                };
            }
            int chosen = -1;
            if (n > 1 && buf[0].ChildStart <= buf[0].ChildEnd)
            {
                chosen = buf[0].ChildStart;
                for (int c = buf[0].ChildStart + 1; c <= buf[0].ChildEnd; c++)
                    if (buf[c].Total > buf[chosen].Total) chosen = c;
            }
            return new OddSnapshotData { Hour = hour, Minute = minute, Chosen = chosen, Nodes = nodes };
        }

        static string FormatTree(OddNode[] buf, int n, List<ActivityKind> verbs)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 1; i < n; i++)
            {
                int depth = 0, p = buf[i].ParentIndex;
                while (p > 0) { depth++; p = buf[p].ParentIndex; }
                sb.Append(' ', depth * 2).Append(verbs[buf[i].Action].ToString())
                  .Append(" direct=").Append(buf[i].DirectScore.ToString("0.000"))
                  .Append(" prop=").Append(buf[i].PropagatedScore.ToString("0.000"))
                  .Append(" total=").Append(buf[i].Total.ToString("0.000"));
                if (buf[i].ParentIndex == 0) sb.Append("  [root]");
                sb.Append('\n');
            }
            return sb.ToString();
        }

        struct ScoreContext
        {
            public double[] Needs, W;
            public PersonalityData Person;
            public int Hour;
            public bool Night, Wet, Holiday;
            public double Outdoor, Cozy, Prepotency;
            public float Px, Pz;
            public EntityId Self;
            public bool IsGuard;
        }

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
                * PlaceMemoryFactor(c.Self, ad)
                * ConscienceFactor(_conscience.ChargeFor(c.Self, ad.Verb))
                * (c.Holiday && s.HolidayFactor != 1.0 ? s.HolidayFactor : 1.0)
                * (s.TraitOnBase ? 1.0 : traitFactor);

            double baseRaw = s.BaseUtility * (s.TraitOnBase ? traitFactor : 1.0);
            double baseTotal = baseRaw + (c.Night ? s.NightBase : 0) + (c.Wet ? s.WetBase : 0);
            double effectiveBase = s.Growth ? baseTotal * gate : baseTotal;
            return OddScore.Compute(c.Needs, s.Delta, c.W, gate, effectiveBase);
        }

        double Liveliness(Ad ad)
            => ad.Building < 0 ? 1.0 : 1.0 + 0.04 * System.Math.Min(_occupancy.PlaceCount(ad.Building), 8);

        const double RelationGain = 0.5, RelationFloor = 0.5, RelationCeil = 1.5;

        double RegardFieldAt(int building, ScoreContext c)
        {
            if (building < 0) return 0.0;
            var occ = _occupancy.OccupantsOf(building);
            bool sum = DriveCatalog.Defs[NeedAxis.SocialDef].Projection == UrgencyProjection.Sum;
            double acc = 0; bool any = false;
            for (int i = 0; i < occ.Count; i++)
            {
                if (occ[i] == c.Self) continue;
                double v = Interpret(c.Self, occ[i]).Valence;
                if (!any) { acc = v; any = true; }
                else if (sum) acc += v;
                else if (v > acc) acc = v;
            }
            return any ? acc : 0.0;
        }

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

        public static double RelationFactor(double meanRegard)
        {
            double f = 1.0 + RelationGain * meanRegard;
            return f < RelationFloor ? RelationFloor : (f > RelationCeil ? RelationCeil : f);
        }

        const double ShameFloor = 0.05;

        public static double ConscienceFactor(double charge)
        {
            double f = 1.0 - charge;
            return f < ShameFloor ? ShameFloor : (f > 1.0 ? 1.0 : f);
        }

        // PLACES scoring — the dense coupling: every decision consults the agent's rich place memory.
        const double DangerAversion = 0.9, DangerFloor = 0.1;   // remembered danger lowers the gate, never to zero
        const double ProvisionPenalty = 0.4;                    // remembered-empty provisioning: a gentle deprioritise

        /// A queued agent keeps its place only if the same shop+Buy still wins; any
        /// other winner means it leaves the line. Pure — unit-tested in isolation.
        public static bool ShouldStayInQueue(bool inQueue, int heldBuilding,
            ActivityKind winnerKind, int winnerBuilding)
            => inQueue && winnerKind == ActivityKind.Buy && winnerBuilding == heldBuilding;

        /// The interrupt rule: a committed agent abandons what it's doing only when the best
        /// alternative is meaningfully better — beats the current commitment's score by the
        /// hysteresis margin. A free agent picks the winner. Pure — unit-tested in isolation.
        public static bool ShouldSwitchCommitment(bool committed, double currentScore,
            double winnerScore, double hysteresis)
            => !committed || winnerScore > currentScore * (1.0 + hysteresis);

        /// <summary>Remembered danger d∈[0,1] → a clamped aversion factor: a fully-deadly place is
        /// avoided hard (DangerFloor) but never zeroed, so a desperate need still overrides it.</summary>
        public static double PlaceAversion(double danger)
        {
            double f = 1.0 - DangerAversion * danger;
            return f < DangerFloor ? DangerFloor : (f > 1.0 ? 1.0 : f);
        }

        /// Sleep suppresses percepts: a sleeper wakes only on a hit, the dawn/dusk
        /// rethink, sleep-duration expiry, or the staggered cap — NEVER on the percept
        /// preemption cadence (note: no preemptTick parameter). Pure — unit-tested.
        public static bool SleepShouldWake(bool wasHit, bool reDecideAll,
            bool durationExpired, bool capReached)
            => wasHit || reDecideAll || durationExpired || capReached;

        /// <summary>Remembered provisioning p∈[0,1] → a gentle preference: a place remembered as empty
        /// is mildly deprioritised (soft prior; the hard larder gate stays ground truth).</summary>
        public static double ProvisionPreference(double prov)
        {
            double f = 1.0 - ProvisionPenalty * (1.0 - prov);
            double floor = 1.0 - ProvisionPenalty;
            return f < floor ? floor : (f > 1.0 ? 1.0 : f);
        }

        /// <summary>The PLACES gate factor for an ad: avoid remembered-dangerous buildings, prefer
        /// remembered-provisioned ones. 1.0 (no effect) for building-less or unremembered ads.</summary>
        double PlaceMemoryFactor(EntityId self, Ad ad)
        {
            if (ad.Building < 0 || !_agentMemory.TryGet(self, out var mem)) return 1.0;
            if (!mem.Stores.Places.TryGet(new MemoryKey(ad.Building), out var rec)) return 1.0;
            double f = 1.0;
            if (rec.DeltaBag.TryGet(PlaceAtoms.Danger, out var d)) f *= PlaceAversion(d.ToDouble());
            if (ad.Spec != null && ad.Spec.LarderGated && rec.DeltaBag.TryGet(PlaceAtoms.Provisions, out var p))
                f *= ProvisionPreference(p.ToDouble());
            return f;
        }

        const double DesperationStrength = 0.7;
        const double DesperationFloor = 0.1;
        const double GuardCombatBoost = 4.0;

        public static double DesperationFactor(double[] needs, int axis)
        {
            var sources = DriveGraph.DesperationSourcesByTarget[axis];
            double f = 1.0;
            for (int i = 0; i < sources.Length; i++)
                f *= 1.0 - DesperationStrength * (needs[sources[i]] / ActivityCatalog.VMax);
            return f < DesperationFloor ? DesperationFloor : f;
        }

        const float FleeDistance = 30f;

        bool NearestThreat(EntityId self, float sx, float sz, out float tx, out float tz)
        {
            tx = 0; tz = 0;
            if (!_subjective.TryGet(self, out var view) || view == null) return false;
            float best = float.MaxValue; bool found = false;
            var reads = view.Entities;
            for (int i = 0; i < reads.Count; i++)
            {
                if (reads[i].Threat <= 0) continue;
                if (!_position.TryGet(reads[i].Other, out var tp) || tp == null) continue;
                float dx = tp.X - sx, dz = tp.Z - sz;
                float d2 = dx * dx + dz * dz;
                if (d2 < best) { best = d2; tx = tp.X; tz = tp.Z; found = true; }
            }
            return found;
        }

        bool NearestCreature(float sx, float sz, out float tx, out float tz)
        {
            tx = 0; tz = 0;
            float best = float.MaxValue; bool found = false; int bestId = int.MaxValue;
            foreach (var kv in _creatures.All)
            {
                if (!_position.TryGet(kv.Key, out var tp) || tp == null) continue;
                float dx = tp.X - sx, dz = tp.Z - sz;
                float d2 = dx * dx + dz * dz;
                if (d2 < best || (d2 == best && kv.Key.Value < bestId))
                { best = d2; tx = tp.X; tz = tp.Z; bestId = kv.Key.Value; found = true; }
            }
            return found;
        }

        // --- Inline reproduction of SubjectiveSystem.Interpret + MeaningsSystem
        // category lookup against the Engine registries (logic preserved verbatim). ---

        const double ThreatValence = -1.0;
        const double StigmaScale = 0.5;

        EntityRead Interpret(EntityId self, EntityId other)
        {
            if (_creatures.Contains(other))
                return new EntityRead
                {
                    Other = other, Valence = ThreatValence,
                    Recognition = 1.0, Trust = 1.0,
                    Attention = 1.0 + System.Math.Abs(ThreatValence),
                    Threat = 1.0,
                };

            double familiarity = 0, baseValence;
            if (_relations.TryGet(self, out var rels) && rels != null && rels.Of.TryGetValue(other, out var rel))
            {
                baseValence = rel.Regard;
                familiarity = rel.Familiarity;
            }
            else
            {
                baseValence = CategoryValence(self, other);
            }
            double valence = baseValence + _affects.ValenceToward(self, other);
            if (_behavior.TryGet(other, out var ob) && ob != null
                && ob.Phase == ActivityPhase.Doing && ob.Activity == ActivityKind.Beg
                && _personality.TryGet(self, out var p) && p != null)
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

        // MeaningsSystem.SignatureOf/CategoryValence reproduced (the resident-role
        // category lookup), reading the Engine MeaningsRegistry + ResidencyRegistry.
        const int UnknownSignature = -1;

        int SignatureOf(EntityId other)
            => _residency.TryGet(other, out var r) && r != null ? (int)r.Role : UnknownSignature;

        double CategoryValence(EntityId self, EntityId other)
        {
            if (!_meanings.TryGet(self, out var m) || m == null) return 0;
            int sig = SignatureOf(other);
            return m.Nodes.TryGetValue(sig, out var node) ? node.Valence * node.Confidence : 0;
        }

        static double TraitOf(ScoreContext c, int idx) => c.Person != null ? c.Person.Trait(idx) : 0.5;

        static double DistFactor(Ad ad, ScoreContext c, double scale)
        {
            float dx = ad.X - c.Px, dz = ad.Z - c.Pz;
            double dist = System.Math.Sqrt(dx * dx + dz * dz);
            return 1.0 / (1.0 + dist / scale);
        }

        /// A sale/larder/coin gate is ROOT-eligibility only — the ad stays in the pool for Enables
        /// propagation even when it can't be pursued standalone this tick. saleCost = SaleUnits*SalePrice
        /// (0 if not a coin-sink). Pure — unit-tested.
        public static bool IsRootEligible(double coin, double saleCost, bool larderGated, double larder, bool inStock)
        {
            if (!inStock) return false;                 // F1: stock gates root-eligibility, not pool membership
            if (saleCost > 0 && coin < saleCost) return false;
            if (larderGated && larder <= 0) return false;
            return true;
        }

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

        public static bool IsNightFor(int hour, double chronotype)
        {
            double shift = (chronotype - 0.5) * 3.0;
            double h = hour + 0.5;
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

    /// One node of a serialized ODD decision tree (see OddSystem.BuildSnapshot).
    public struct OddSnapNode
    {
        public int Parent;     // index into the snapshot's Nodes array; -1 only for node 0
        public string Verb;    // ActivityKind name; null for the root sentinel
        public double Direct;
        public double Prop;
        public double Total;
        public bool Terminal;
    }

    /// A captured ODD decision tree for one agent at one decision instant.
    public sealed class OddSnapshotData
    {
        public int Hour;
        public int Minute;
        public int Chosen;        // index into Nodes of the chosen root action; -1 = none
        public OddSnapNode[] Nodes;
    }
}
