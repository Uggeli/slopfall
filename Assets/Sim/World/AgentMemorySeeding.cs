using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim
{
    /// <summary>
    /// Composable load-time agent-memory seeding — the init-time analogue of perception.
    /// Each <c>SeedX</c> stamps ONE kind of innate knowledge into an agent's private memory
    /// from what it structurally knows at spawn. "Owner-stamps-its-own": each step reads the
    /// registry that owns that fact (PlaceMemory for places, later Ownership/Reputation/...).
    ///
    /// <para><see cref="SeedAgent"/> is the single entry point callers use — it runs every
    /// step. New knowledge kinds slot in beside <see cref="SeedPlaces"/> here and every caller
    /// picks them up with no change (spec decision 4: seeding is a general agent-init path).</para>
    ///
    /// <para>Seeding draws no RNG and writes only to the (Phase-1 read-free) memory stores, so
    /// it is behaviourally inert until ODD reads the store in Phase 2 — the spawn/RNG order and
    /// the soak baseline stay byte-identical.</para>
    /// </summary>
    public static class AgentMemorySeeding
    {
        /// <summary>Seed every innate-knowledge layer for one agent. Call once per agent AFTER
        /// its structural knowledge (PlaceMemory residency + town knowledge) is finalised.</summary>
        public static void SeedAgent(SimWorld world, EntityId agent)
        {
            SeedPlaces(world, agent);
            SeedPriors(world, agent);            // Phase C: innate kind-keyed category beliefs
            // Future SeedX (slot in here — callers need no change):
            //   SeedOwnerships(world, agent);    // the buildings/goods this agent owns
            //   SeedReputations(world, agent);   // standings it already holds
            //   SeedSocial(world, agent);        // kin / household ties known from birth
        }

        /// <summary>Innate place knowledge: for every building the agent KNOWS, stamp that
        /// building's kind atom into PLACES memory ("that's the tavern / the general store").
        /// The learned layer (provisions, danger) accretes on top of this at runtime.</summary>
        /// <summary>Innate species priors: stamp this agent's kind-keyed innate category beliefs into
        /// MEANINGS. L1 = in-group warmth, keyed by the agent's OWN signature (so it matches same-kind
        /// kin under the store's near-exact recognition). The learned layer drifts these at runtime;
        /// an individual dossier overrides the category entirely.</summary>
        public static void SeedPriors(SimWorld world, EntityId agent)
        {
            if (!world.Identity.TryGet(agent, out var ident) || ident == null) return;
            foreach (var prior in InnatePriors.For(ident.Kind))
            {
                AtomBag proto = prior.Target == PriorTarget.Self
                    ? world.Perceivable.Signature(agent)
                    : AtomBag.Empty;
                if (proto.Count == 0) continue;                     // nothing to key on → skip
                world.AgentMemory.SeedInnatePrior(agent, proto, prior.Valence, prior.Confidence);
            }
        }

        public static void SeedPlaces(SimWorld world, EntityId agent)
        {
            foreach (var building in world.PlaceMemory.Known(agent))
                if (world.Buildings.TryGet(building, out var row) && row != null && row.Kind != BuildingKind.None)
                    world.AgentMemory.SeedPlace(agent, building, PlaceAtoms.Kind(row.Kind), Fixed.One);
        }
    }
}
