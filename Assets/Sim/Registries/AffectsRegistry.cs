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

    /// Per-agent emotional state — the directed-drive / target-field layer (the
    /// scalar NeedsRegistry poles are the levels; these are what those poles are
    /// ABOUT). Sole writer: AffectsSystem. Read by SubjectiveSystem.Interpret so
    /// acute feeling colors perception on top of chronic regard.
    public sealed class AffectsRegistry
    {
        readonly ConcurrentDictionary<EntityId, AffectsData> _d = new ConcurrentDictionary<EntityId, AffectsData>();

        public void Set(EntityId id, AffectsData data) => _d[id] = data;
        public bool TryGet(EntityId id, out AffectsData data) => _d.TryGetValue(id, out data);
        public void Remove(EntityId id) { _d.TryRemove(id, out var _); }
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, AffectsData>> All => _d;

        /// Signed acute valence the holder feels toward `other` right now (sum of
        /// directed affects). 0 if none — the common case.
        public double ValenceToward(EntityId holder, EntityId other)
        {
            if (!_d.TryGetValue(holder, out var data)) return 0;
            double v = 0;
            var list = data.Active;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Target == other) v += Sign(list[i].Kind) * list[i].Intensity;
            return v;
        }

        public static double Sign(AffectKind k)
        {
            switch (k)
            {
                case AffectKind.Affection:
                case AffectKind.Gratitude:
                    return +1;
                case AffectKind.Resentment:
                case AffectKind.Aversion:
                case AffectKind.Fear:
                    return -1;
                default:
                    return 0;   // Loneliness is undirected
            }
        }
    }
}
