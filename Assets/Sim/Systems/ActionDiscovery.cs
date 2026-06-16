using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// The CAN side of the marketplace: given an agent and a thing it knows or
    /// senses, what activities can it perform there? This is the resolver that
    /// turns AffordanceCatalog (kind → verbs) into concrete, agent-relative ads —
    /// the per-thing "what can I do to this" that OddSystem hardcodes today.
    ///
    /// Central catalog + resolver: affordances are derived from kind + role +
    /// (later) relationship, with no per-instance storage. GatherAds reproduces
    /// OddSystem's candidate set as data, so the DeliberationSystem rewrite can
    /// consume it with no behavior change. Pure function — no mutation, trivially
    /// testable. See docs/action_catalog.md.
    public static class ActionDiscovery
    {
        /// Activities a specific building offers this agent: its public
        /// affordances (by kind) plus the agent-relative ones if it's their home
        /// or workplace.
        public static void Discover(SimulationContext ctx, EntityId agent, int buildingIndex, List<Ad> into)
        {
            if (!ctx.Buildings.TryGet(buildingIndex, out var b) || b == null) return;

            bool own = ctx.Residency.TryGet(agent, out var res) && res.BuildingIndex == buildingIndex;
            if (own)
            {
                // Your own home/shop affords living and working — not being
                // visited or patronised as a customer. (A keeper lives above the
                // shop, so it's both home and workplace.)
                foreach (var verb in AffordanceCatalog.Home)
                {
                    // Recall (Atoms what_is_memory): don't even offer EatHome if the
                    // agent REMEMBERS the pantry was bare — the ad doesn't come to
                    // mind, so a hungry agent reaches past it to Buy/Work/Farm/Steal
                    // instead of livelocking on a meal it knows it can't have. A
                    // remembered ProvisionsHere==0 suppresses it; never-seen does not
                    // (a fresh agent checks home once). Stale by design — corrected
                    // on the next visit.
                    if (verb == ActivityKind.EatHome
                        && ctx.PlaceMemory.Recall(agent, buildingIndex, PlaceFact.ProvisionsHere, out var pf)
                        && pf.Value <= 0)
                        continue;
                    Offer(into, verb, buildingIndex, b);
                }
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

        /// Every ad available to an agent right now: the innate floor (Idle/Wander)
        /// plus the activities offered by every place it knows (full town, for a
        /// resident). Person-directed ads (Chat/Ask from sensed agents) fold in
        /// when the social mechanisms join the marketplace.
        public static List<Ad> GatherAds(SimulationContext ctx, EntityId agent)
        {
            var ads = new List<Ad>();

            float px = 0f, pz = 0f;
            if (ctx.Position.TryGet(agent, out var pos)) { px = pos.X; pz = pos.Z; }
            foreach (var verb in AffordanceCatalog.Innate)
                ads.Add(new Ad { Verb = verb, Building = -1, X = px, Z = pz, Spec = ActivityCatalog.SpecFor(verb) });

            foreach (var building in ctx.PlaceMemory.Known(agent))
                Discover(ctx, agent, building, ads);

            // Jobs-at-workplace: an employed resident can work at their employer's
            // premises (wage paid from that purse — EconomySystem). The verb is the
            // workplace's trade: Farm at the fields, Fish at the shore, generic Labor
            // anywhere else — so the act is the job, at its own place.
            if (ctx.Employment.TryGet(agent, out var emp) && !emp.Employer.IsNone
                && ctx.Residency.TryGet(emp.Employer, out var empRes)
                && ctx.Buildings.TryGet(empRes.BuildingIndex, out var empBldg))
            {
                var verb = empBldg.Kind == BuildingKind.Farm ? ActivityKind.Farm
                         : empBldg.Kind == BuildingKind.Fishery ? ActivityKind.Fish
                         : empBldg.Kind == BuildingKind.Mine ? ActivityKind.Mine
                         : ActivityKind.Labor;
                ads.Add(new Ad
                {
                    Verb = verb, Building = empRes.BuildingIndex,
                    X = empBldg.X, Z = empBldg.Z, Spec = ActivityCatalog.SpecFor(verb),
                });
            }

            // Collect = structural affordances filtered by hard preconditions
            // (does the world permit this now: hours / keeper / holiday / stock).
            int hour = ctx.WorldClock.Current.Hour;
            bool holiday = ctx.Holiday.CurrentId > 0;
            bool isKeeper = ctx.Residency.TryGet(agent, out var res) && res.Role == ResidentRole.Keeper;
            ads.RemoveAll(a => !ActivityCatalog.PreconditionsMet(a.Spec, hour, holiday, isKeeper)
                               || !SaleStockAvailable(ctx, a));

            return ads;
        }

        /// A gated sale (Buy, EatTavern) can't be offered by an empty shelf —
        /// no goods, no sale. Ungated sales (Socialize: the drink is optional) and
        /// non-sale activities always pass.
        static bool SaleStockAvailable(SimulationContext ctx, Ad a)
        {
            var spec = a.Spec;
            if (spec == null || spec.SaleReliefAxis < 0) return true;
            if (a.Building < 0 || !ctx.Buildings.TryGet(a.Building, out var b) || b == null) return true;
            // A gated activity at a building that sells/holds nothing for it can't
            // happen here → cull (a wares-only shop has no provisions to steal). Buy/
            // EatTavern are only offered at venues that do stock their good, so this
            // only bites the broadly-offered Steal.
            if (!GoodsCatalog.SaleGoodFor(b.Kind, spec.Kind, out var good)) return false;
            return ctx.Stock.Get(a.Building, good) > 0;
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
    }
}
