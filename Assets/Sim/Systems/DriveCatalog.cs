namespace DaggerfallWorkshop.Sim
{
    /// How a drive's residual discharges — the drive doc's three routes, NOT the
    /// old engine enum. The old `Derived` was really "a deplete drive whose level
    /// lives in an external store"; that distinction now lives in LevelSource, a
    /// separate axis, so Satisfaction can name only the discharge route.
    /// (Relations decay toward a baseline — a fourth route — but relations are the
    /// social fabric (L1), not drives, so it isn't a DriveDef option here.)
    public enum SatisfactionModel
    {
        Deplete,         // an action writes the level down (hunger, energy, social)
        ResetOnPercept,  // residual tracks a percept; discharge = its absence (safety/fear — V2b)
        None,            // no discharge node — bottomless (curiosity / growth B-needs — later)
    }

    /// Where a drive's LEVEL is read from — the drive doc's pole/derived split
    /// made explicit. Only Stored poles are ticked by Metabolism (NeedsSystem);
    /// the rest are derived reads of a conserved/external quantity each tick, so
    /// nothing is stored that can desync from its source ("drives are minted, not
    /// stored; nothing kept but poles and memory").
    public enum LevelSource
    {
        Stored,        // a real body pole in NeedsData.V, ticked by DriftPerHour (hunger, energy, social)
        DerivedCoin,   // read from the purse (CoinRegistry): poverty = 1 − coin
        DerivedLarder, // read from the home larder: provisions running low (wired in the famine phase)
        DerivedThreat, // the fear controller: Max-projection of the perceived-threat field + vigilance floor (V2b)
    }

    /// How a directed drive's target FIELD collapses to scalar urgency. Moot for
    /// undirected/scalar drives (one implicit target). Authored per-drive so it's
    /// a real fact, not a global choice: fear=Max (the worst threat dominates —
    /// three foxes must not sum to panic), social=Sum (a crowd adds bonding pull).
    public enum UrgencyProjection { Max, Sum }

    /// A prepotency DAG edge OUT of a drive (this ⊣ Target). Two kinds:
    ///  - HardCull: deficiency ⊣ growth — past threshold the gated drive's ads
    ///    can't manifest ("a starving bunny can't binky"); graded below it.
    ///  - DesperationGraded: hunger ⊣ safety — starvation overrides fear but
    ///    stays graded, never culls, or a starving animal couldn't flee (V2b).
    public enum GateKind { HardCull, DesperationGraded }

    public struct GateEdge { public int Target; public GateKind Kind; }

    /// Authored definition of one drive — the drive doc's four fields plus the
    /// pole/derived split:
    ///   drive = ( ScoreField, Projection, Satisfaction, Gates )  +  LevelSource
    /// One row per NeedAxis: the single source of truth for scoring weight,
    /// metabolism rate, discharge route, urgency projection, and prepotency.
    /// ActivityCatalog.Weights / DriftPerHour project out of this table.
    public struct DriveDef
    {
        public string Name;
        public double ScoreField;             // the V weight (was Weight)
        public double DriftPerHour;           // metabolism tick — Stored poles only (derived levels = 0)
        public UrgencyProjection Projection;
        public SatisfactionModel Satisfaction;
        public LevelSource Level;
        public GateEdge[] Gates;              // prepotency edges out of this drive (empty ⇒ gates nothing)
    }

    public static class DriveCatalog
    {
        public static readonly DriveDef[] Defs = new DriveDef[NeedAxis.Count];

        // Every deficiency pole hard-culls the genuinely DISCRETIONARY/growth drives
        // DIRECTLY (drive doc proto p1: never only transitively). F3: that set is
        // social *leisure* only — goods/coin are INSTRUMENTAL to hunger (the
        // work->buy->eat chain needs them), so a deficiency must not cull them; they
        // are gated by the chain's affordability + larder/stock roots (F1) instead.
        // Shared by every deficiency source (hunger, energy, and fear).
        static readonly GateEdge[] DeficiencyGates =
        {
            new GateEdge { Target = NeedAxis.SocialDef, Kind = GateKind.HardCull },
        };

        // Hunger additionally rules the DESPERATION edge hunger ⊣ safety: a starving
        // animal's fear is graded DOWN (overridden), never culled — or it couldn't
        // flee at all. This is what makes escape-affordability emergent (V2b).
        static readonly GateEdge[] HungerGates =
        {
            new GateEdge { Target = NeedAxis.SocialDef, Kind = GateKind.HardCull },
            new GateEdge { Target = NeedAxis.Fear,      Kind = GateKind.DesperationGraded },
        };

        static DriveCatalog()
        {
            // Body poles — Stored, ticked, prepotent deficiency sources.
            Defs[NeedAxis.Hunger] = new DriveDef
            {
                Name = "hunger", ScoreField = 1.2, DriftPerHour = 0.04,
                Projection = UrgencyProjection.Max, Satisfaction = SatisfactionModel.Deplete,
                Level = LevelSource.Stored, Gates = HungerGates,
            };
            Defs[NeedAxis.EnergyDef] = new DriveDef
            {
                Name = "energy", ScoreField = 1.0, DriftPerHour = 0.05,
                Projection = UrgencyProjection.Max, Satisfaction = SatisfactionModel.Deplete,
                Level = LevelSource.Stored, Gates = DeficiencyGates,
            };
            // Social: a Stored loneliness pole today; becomes a directed Sum-field
            // drive in D2 (its projection is already authored). Gates nothing.
            Defs[NeedAxis.SocialDef] = new DriveDef
            {
                Name = "social", ScoreField = 0.6, DriftPerHour = 0.03,
                Projection = UrgencyProjection.Sum, Satisfaction = SatisfactionModel.Deplete,
                Level = LevelSource.Stored, Gates = System.Array.Empty<GateEdge>(),
            };
            // Coin: NOT a real pole — instrumental, a derived read of the purse.
            // DEMOTED to a weak discretionary pull (0.5 → 0.1): the honest
            // coin-seeking is hunger → provisions → coin, i.e. ODD-tree propagation
            // (V3); until that lands, a strong CoinDef was a synthetic "poverty =
            // maxed need" that made the whole town beg. Now hunger (prepotent) and
            // GoodsDef (the larder) carry the want-for-coin; this is just the
            // leftover pull for drink/wares. FROZEN (tuned with the famine, not here).
            Defs[NeedAxis.CoinDef] = new DriveDef
            {
                Name = "coin", ScoreField = 0.0, DriftPerHour = 0.0,   // RETIRED: coin is purely instrumental now — valued only by the ODD chain (work→buy→need), never as a standalone "poverty" want. Poverty is contextual (what you can't buy locally at local prices), not a hardcoded 1−coin.
                Projection = UrgencyProjection.Max, Satisfaction = SatisfactionModel.Deplete,
                Level = LevelSource.DerivedCoin, Gates = System.Array.Empty<GateEdge>(),
            };
            // Goods: "provisions running low" — drives shopping/stealing. Now a
            // DERIVED read of the household larder (single source of truth): an
            // empty pantry is a loud restock pull, a full one silent. A stored copy
            // would desync from the larder EatHome actually draws.
            Defs[NeedAxis.GoodsDef] = new DriveDef
            {
                Name = "goods", ScoreField = 0.5, DriftPerHour = 0.0,
                Projection = UrgencyProjection.Max, Satisfaction = SatisfactionModel.Deplete,
                Level = LevelSource.DerivedLarder, Gates = System.Array.Empty<GateEdge>(),
            };
            // Fear: the first DIRECTED drive (V2b). Its level is the Max-projection
            // of a perceived-threat field (worst threat dominates — foxes don't sum
            // to panic) plus a personality vigilance floor, computed by NeedsSystem's
            // fear controller; ResetOnPercept (decays to the floor when no threat is
            // seen). A prepotent deficiency source itself: it hard-culls leisure, and
            // is graded down by hunger (the desperation edge above). Strong weight so
            // a real threat dominates selection (→ Flee).
            Defs[NeedAxis.Fear] = new DriveDef
            {
                Name = "fear", ScoreField = 1.3, DriftPerHour = 0.0,
                Projection = UrgencyProjection.Max, Satisfaction = SatisfactionModel.ResetOnPercept,
                Level = LevelSource.DerivedThreat, Gates = DeficiencyGates,
            };
            // Attire: clothing wears out. A Stored/Deplete pole like hunger, but with a
            // FAR slower metabolism — a garment lasts ~10 days of wear (drift 0.004/hr ≈
            // 0.1/day), where hunger drifts ~1.0/day. Its relief is DIRECT, not
            // larder-gated: textile work (Weave) clothes you in-kind (the self-reward
            // that motivates the looms, the Farm pattern), and buying clothes relieves it
            // too. Weight below the body poles (you get ragged before you starve), but a
            // real want, so the textile sector has genuine demand to serve. Gates nothing
            // — and isn't a cull target: the body poles (ScoreField 1.0-1.3) dominate the
            // gap math over Attire (0.6) naturally, so a hungry weaver eats before weaving.
            Defs[NeedAxis.Attire] = new DriveDef
            {
                Name = "attire", ScoreField = 0.6, DriftPerHour = 0.004,
                Projection = UrgencyProjection.Max, Satisfaction = SatisfactionModel.Deplete,
                Level = LevelSource.Stored, Gates = System.Array.Empty<GateEdge>(),
            };
        }
    }
}
