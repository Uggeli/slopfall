using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>Which signature an innate prior is ABOUT. L1 carries only Self (in-group = "those who
    /// look like me"); Kind(X) out-group targets arrive with Phase D, when episodic attribution makes
    /// them non-redundant and monsters gain a perceivable Kind atom.</summary>
    public enum PriorTarget { Self }

    /// <summary>One innate category belief: a starting valence + confidence about a PriorTarget.</summary>
    public readonly struct InnatePrior
    {
        public readonly PriorTarget Target;
        public readonly Fixed Valence;
        public readonly Fixed Confidence;
        public InnatePrior(PriorTarget target, Fixed valence, Fixed confidence)
        { Target = target; Valence = valence; Confidence = confidence; }
    }

    /// <summary>
    /// The kind-keyed innate priors a newborn is seeded with — a small, believable STARTER set in a
    /// structure built to grow: Phase D reinforces these nodes, E transmits them, and rows/targets are
    /// added as the world calls for them. Frozen content; open structure.
    /// </summary>
    public static class InnatePriors
    {
        // CivilianNPC: innate warmth toward its own kind (same-signature kin). conf 0.3 = a lean, not
        // a conviction (Reinforce drifts it; an individual dossier overrides the category entirely).
        static readonly InnatePrior[] Civilian =
        {
            new InnatePrior(PriorTarget.Self, Fixed.FromDouble(0.2), Fixed.FromDouble(0.3)),
        };
        static readonly InnatePrior[] None = new InnatePrior[0];

        public static InnatePrior[] For(EntityKind kind)
            => kind == EntityKind.CivilianNPC ? Civilian : None;
    }
}
