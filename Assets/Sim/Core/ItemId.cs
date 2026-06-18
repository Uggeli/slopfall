using System;

namespace DaggerfallWorkshop.Sim
{
    /// Identity of a discrete item that EXISTS in the world — carriable, ownable,
    /// perceivable (docs/items_and_inventory.md). Distinct from EntityId
    /// (agents/creatures) so items never pollute the agent registries (Identity,
    /// Position, Behavior, …). Allocated by ItemRegistry; deterministic given the
    /// single writer's ordered passes, the same way IdentityRegistry allocates.
    public readonly struct ItemId : IEquatable<ItemId>
    {
        public static readonly ItemId None = new ItemId(0);

        public readonly int Value;

        public ItemId(int value) { Value = value; }

        public bool IsNone => Value == 0;

        public bool Equals(ItemId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ItemId o && Equals(o);
        public override int GetHashCode() => Value;
        public override string ToString() => IsNone ? "ItemId.None" : "ItemId(" + Value + ")";

        public static bool operator ==(ItemId a, ItemId b) => a.Value == b.Value;
        public static bool operator !=(ItemId a, ItemId b) => a.Value != b.Value;
    }
}
