namespace DaggerfallWorkshop.Sim
{
    public enum SatisfactionModel
    {
        Deplete,             // drifts up over time, pushed down by activities (hunger, energy, social)
        Derived,             // computed from another quantity each tick (coin deficit = 1 − coin)
        DecayTowardBaseline, // erodes toward a baseline unless refreshed (relations — L1, SocialSystem)
    }

    /// Authored definition of one need/drive axis. Atoms says a drive is a few
    /// fields in a table — this makes that literally true, formalizing constants
    /// today scattered across ActivityCatalog.Weights, ActivityCatalog.DriftPerHour,
    /// and OddSystem.PrepotencyGate. Values match those constants exactly, so the
    /// pipeline rewrite (E0b-4) that switches onto this table is behavior-neutral
    /// until we deliberately retune. Indexed by NeedAxis.
    public struct DriveDef
    {
        public string Name;
        public double Weight;            // scoring weight  (was ActivityCatalog.Weights)
        public double DriftPerHour;      // metabolism tick (was ActivityCatalog.DriftPerHour)
        public SatisfactionModel Satisfaction;
        public bool Prepotent;           // a deficiency drive that suppresses leisure when loud
    }

    public static class DriveCatalog
    {
        public static readonly DriveDef[] Defs = new DriveDef[NeedAxis.Count];

        static DriveCatalog()
        {
            Defs[NeedAxis.Hunger]    = new DriveDef { Name = "hunger", Weight = 1.2, DriftPerHour = 0.04, Satisfaction = SatisfactionModel.Deplete, Prepotent = true };
            Defs[NeedAxis.EnergyDef] = new DriveDef { Name = "energy", Weight = 1.0, DriftPerHour = 0.05, Satisfaction = SatisfactionModel.Deplete, Prepotent = true };
            Defs[NeedAxis.SocialDef] = new DriveDef { Name = "social", Weight = 0.6, DriftPerHour = 0.03, Satisfaction = SatisfactionModel.Deplete, Prepotent = false };
            Defs[NeedAxis.CoinDef]   = new DriveDef { Name = "coin",   Weight = 0.5, DriftPerHour = 0.0,  Satisfaction = SatisfactionModel.Derived, Prepotent = false };
            Defs[NeedAxis.GoodsDef]  = new DriveDef { Name = "goods",  Weight = 0.5, DriftPerHour = 0.03, Satisfaction = SatisfactionModel.Deplete, Prepotent = false };
        }
    }
}
