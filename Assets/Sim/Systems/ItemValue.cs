namespace DaggerfallWorkshop.Sim
{
    /// What an item is WORTH — the market price everyone agrees on, and the
    /// subjective worth to a particular entity. "What makes a thing matter to an
    /// entity" is the real question behind items: theft temptation, the sting of
    /// grievance, and (later) gifting / inheritance all read THIS, not a flat price
    /// tag. See docs/items_and_inventory.md + the items design direction.
    ///
    /// Three axes — value is drive- and relation-relative, never absolute (Atoms):
    ///   monetary   — the Valuable atom: market price, the same for everyone.
    ///   utility    — does it serve THIS entity's loud needs right now? An edible is
    ///                worth more to the starving. Drive-relative.
    ///   provenance — is it bound to this entity (genuinely theirs, long-held)? What
    ///                turns a cheap thing into an heirloom and makes its loss sting
    ///                past its price. EMERGENT — not a seeded "heirloom" tag.
    /// v1 ships monetary fully; utility reads Edible→Hunger; provenance reads the
    /// ownership/origin fields + how long held. Weights are FROZEN placeholders
    /// until behavior tunes them. Pure + static for testing.
    public static class ItemValue
    {
        const double UtilityWeight = 0.5;        // a loud need can rival the price tag
        const double ProvenanceWeight = 0.4;     // sentiment can outweigh a modest price
        const double ProvenanceFullDays = 365.0; // held ~a year → a full sentimental bond
        const double KeepsakeFloor = 0.2;        // even a worthless long-held thing means something

        /// Market price — intrinsic, viewer-independent (the Valuable atom).
        public static double Market(ItemData item) => item == null ? 0 : item.Valuable;

        /// Drive-relative usefulness to the holder of `needs`: each affordance is
        /// worth its strength scaled by the need it would relieve. v1: Edible →
        /// Hunger. (Drinkable→Thirst, Wieldable→safety … fold in via a table as
        /// those atoms / needs arrive.)
        public static double Utility(ItemData item, double[] needs)
        {
            if (item == null || needs == null) return 0;
            double u = 0;
            if (item.Edible > 0) u += item.Edible * needs[NeedAxis.Hunger];
            return u;
        }

        /// Bond to `viewer`: only the owner feels it, and only for what is genuinely
        /// theirs (they are its origin owner — inherited / long-held), rising with
        /// how long it's been held toward a full bond. A stranger feels none — which
        /// is exactly why a thief values loot at market while the victim values it
        /// far more. heldGameDays is supplied by the caller (the clock); pure here.
        public static double Provenance(ItemData item, EntityId viewer, double heldGameDays)
        {
            if (item == null || item.Owner.IsNone || item.Owner != viewer || item.OriginOwner != viewer)
                return 0;
            double bond = heldGameDays / ProvenanceFullDays;
            if (bond < 0) bond = 0;
            else if (bond > 1) bond = 1;
            return bond * (Market(item) + KeepsakeFloor);
        }

        /// Subjective worth of `item` to `viewer`: market + utility-to-their-needs +
        /// what it means to them. The single number ads (want) and grievance read.
        public static double To(ItemData item, EntityId viewer, double[] needs, double heldGameDays)
        {
            if (item == null) return 0;
            return Market(item)
                 + UtilityWeight * Utility(item, needs)
                 + ProvenanceWeight * Provenance(item, viewer, heldGameDays);
        }
    }
}
