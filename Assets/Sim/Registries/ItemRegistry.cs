using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace DaggerfallWorkshop.Sim
{
    /// The fixed atom vocabulary (docs/items_and_inventory.md, "an item is a bundle
    /// of atoms"). A FIXED enum with a stable ordering is the determinism contract —
    /// never iterate a hash-bag of atoms in nondeterministic order. v1 populates only
    /// the handful the witnessed-theft slice needs; the rest (Drinkable, Wieldable,
    /// Wearable, Perishable, Quality, Material, …) are reserved rows that drop in as
    /// behaviors pull them. Storage is fixed fields on ItemData (open question 1,
    /// resolved toward the struct for determinism), so this enum is the
    /// signature/ordering authority, not the store.
    public enum AtomKind
    {
        // Affordance atoms — what you can DO with it (generate ads, the CAN side).
        Edible = 0,
        Valuable = 1,
        // Property atoms — what it IS (feed V() + economy + stacking).
        Weight = 2,
        // Further affordance atoms — activated as the goods/industry pull them.
        Drinkable = 3,   // refreshment if drunk (ale)
        Wearable = 4,    // warmth / protection / status if worn (clothes)
        // The Ownable atom and the identity atoms (Owner/Provenance/Name) are
        // modelled as dedicated ItemData fields, not enum rows — they are
        // always-present structure, not optional affordances.
    }

    /// Where an item physically IS — distinct from who OWNS it (the Owner field).
    /// held-by ≠ owned-by is exactly the "stolen" state the fungible Good model
    /// cannot represent (docs/items_and_inventory.md, "Location ≠ ownership").
    public enum ItemLocationKind
    {
        Destroyed = 0,
        OnGround,
        InBuilding,
        CarriedBy,
        EquippedBy,
    }

    /// One discrete item instance: a bundle of atoms (fixed fields; 0/None/null =
    /// "atom absent"), an owner, a provenance, and a physical location. A new item
    /// KIND is new atom DATA, not new code. Reference type — registries swap whole
    /// rows (matching ConscienceData / RelationsData).
    public sealed class ItemData
    {
        // Identity atoms (per-instance). A Name makes a thing unique, so it never
        // stacks; the ABSENCE of distinguishing atoms is what makes items fungible.
        public string Name;          // null = generic / fungible
        public EntityId OriginOwner; // provenance: who it began with…
        public long OriginTick;      // …and when (heirloom-ness / "stolen" lineage)

        // Affordance atoms (0 = absent).
        public double Edible;        // nutrition served if eaten
        public double Drinkable;     // refreshment served if drunk
        public double Wearable;      // warmth / protection served if worn
        public double Valuable;      // worth in coin

        // Property atoms.
        public double Weight;

        // Ownable atom — whose it is. Does NOT move on theft (only Location does);
        // location returning to owner is recovery, owner→heir on death is inheritance.
        public EntityId Owner;

        // Physical location — where it is right now.
        public ItemLocationKind LocationKind;
        public EntityId Holder;      // CarriedBy / EquippedBy
        public int Building;         // InBuilding (BuildingRegistry index)
        public float X, Z;           // OnGround
        public int EquipSlot;        // EquippedBy (reserved); -1 otherwise
    }
}
