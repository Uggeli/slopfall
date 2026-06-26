using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>Which signature an innate prior is ABOUT. Self = in-group ("those who look like me");
    /// Kind(X) = an out-group kind, keyed by that kind's perceivable Kind atom (Phase D).</summary>
    public enum PriorTarget { Self, Kind }

    /// <summary>One innate category belief: a starting valence + confidence about a PriorTarget.
    /// Kind is the believed-about EntityKind, used only when Target == Kind.</summary>
    public readonly struct InnatePrior
    {
        public readonly PriorTarget Target;
        public readonly EntityKind Kind;
        public readonly Fixed Valence;
        public readonly Fixed Confidence;
        public InnatePrior(PriorTarget target, EntityKind kind, Fixed valence, Fixed confidence)
        { Target = target; Kind = kind; Valence = valence; Confidence = confidence; }
    }

    /// <summary>
    /// The kind-keyed innate priors a newborn is seeded with — a small, believable STARTER set in a
    /// structure built to grow: Phase D reinforces these nodes, E transmits them, and rows/targets are
    /// added as the world calls for them. Frozen content; open structure.
    /// </summary>
    public static class InnatePriors
    {
        // CivilianNPC: innate warmth toward its own kind (Self) + innate wariness of monster-kind
        // (the prey-of-predator prior). conf 0.3 = a lean (Reinforce drifts it; D feeds "this kind hurt me").
        static readonly InnatePrior[] Civilian =
        {
            new InnatePrior(PriorTarget.Self, EntityKind.Unknown,      Fixed.FromDouble(0.2),  Fixed.FromDouble(0.3)),
            new InnatePrior(PriorTarget.Kind, EntityKind.EnemyMonster, Fixed.FromDouble(-0.6), Fixed.FromDouble(0.3)),
        };
        static readonly InnatePrior[] None = new InnatePrior[0];

        public static InnatePrior[] For(EntityKind kind)
            => kind == EntityKind.CivilianNPC ? Civilian : None;
    }
}
