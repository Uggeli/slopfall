using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    public enum AffectKind { Affection, Gratitude, Resentment, Aversion, Loneliness, Fear }

    /// A directed emotion: the residual the agent feels toward Target right now.
    /// Atoms drive doc: emotion is a drive's residual; the target field is what it
    /// is about. ACUTE and decaying — distinct from the chronic dossier regard
    /// (RelationData): "I'm angry at them right now" vs "I dislike them in
    /// general." See docs/cognitive_substrate_S2_emotion.md.
    public struct Affect
    {
        public EntityId Target;
        public AffectKind Kind;
        public double Intensity;     // >= 0; the sign comes from Kind (see Sign)
    }

    public sealed class AffectsData
    {
        public List<Affect> Active = new List<Affect>();
    }
}
