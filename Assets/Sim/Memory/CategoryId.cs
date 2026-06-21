using System;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Reference to the MEANINGS category node a record diffs against. None = novelty: the
    /// percept matched no category, so the record stores its atoms verbatim (nothing to diff).
    /// </summary>
    public readonly struct CategoryId : IEquatable<CategoryId>
    {
        public static readonly CategoryId None = new CategoryId(0);

        public readonly int Value;

        public CategoryId(int value) { Value = value; }

        public bool IsNone => Value == 0;

        public bool Equals(CategoryId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CategoryId o && Equals(o);
        public override int GetHashCode() => Value;
        public override string ToString() => IsNone ? "CategoryId.None" : "CategoryId(" + Value + ")";

        public static bool operator ==(CategoryId a, CategoryId b) => a.Value == b.Value;
        public static bool operator !=(CategoryId a, CategoryId b) => a.Value != b.Value;
    }
}
