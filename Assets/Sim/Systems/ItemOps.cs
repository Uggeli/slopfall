namespace DaggerfallWorkshop.Sim
{
    /// The universal item verbs, as operations on an ItemData row. The registry
    /// stores references, so mutating the row in place IS the write — ItemSystem,
    /// the single writer, is the only thing that should call these during a tick
    /// (matching how StockRegistry/ConscienceData mutate in place under single
    /// ownership). Pure of the registry itself, so trivially testable.
    ///
    /// "Take" is universal: anything ownable is stealable, with no "stealable" flag
    /// — theft is just a Take whose owner isn't the taker. See the items design
    /// direction + docs/items_and_inventory.md.
    public static class ItemOps
    {
        /// Pick the item up into the taker's hands. LOCATION moves; OWNERSHIP never
        /// does (held-by ≠ owned-by is the stolen state, recoverable by returning
        /// location to owner). Returns true if this is theft — owned by someone
        /// other than the taker.
        public static bool Take(ItemData item, EntityId taker)
        {
            if (item == null) return false;
            bool theft = !item.Owner.IsNone && item.Owner != taker;
            item.LocationKind = ItemLocationKind.CarriedBy;
            item.Holder = taker;
            item.Building = -1;
            item.X = 0; item.Z = 0;
            return theft;
        }

        /// Set the item down — in a building if building >= 0, else on the ground at
        /// (x, z). Clears the holder; ownership is untouched.
        public static void Drop(ItemData item, int building, float x, float z)
        {
            if (item == null) return;
            item.Holder = EntityId.None;
            if (building >= 0)
            {
                item.LocationKind = ItemLocationKind.InBuilding;
                item.Building = building;
                item.X = 0; item.Z = 0;
            }
            else
            {
                item.LocationKind = ItemLocationKind.OnGround;
                item.Building = -1;
                item.X = x; item.Z = z;
            }
        }

        /// Charge a conscience qualm onto a crime just committed — mirrors
        /// CombatSystem.ChargeViolenceGuilt. Theft charges ActivityKind.Steal (the
        /// verb V() already penalises), so committing it deepens the agent's own
        /// aversion next time. Clamped to [0, 1].
        public static void ChargeCriminalGuilt(SimulationContext ctx, EntityId criminal, ActivityKind crime, double amount)
        {
            if (!ctx.Conscience.TryGet(criminal, out var c) || c == null)
            {
                c = new ConscienceData();
                ctx.Conscience.Set(criminal, c);
            }
            int k = (int)crime;
            c.Charge.TryGetValue(k, out var cur);
            double next = cur + amount;
            c.Charge[k] = next > 1.0 ? 1.0 : next;
        }
    }
}
