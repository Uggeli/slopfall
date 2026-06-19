using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Family-tree HOOK (faction work, later). One row of lineage per NPC: the family
    /// they belong to, their shared surname, and links to parents / spouse / children.
    ///
    /// Today the loaders populate only the cheap, certain parts — a per-household
    /// FamilyId, the shared Surname, and a spouse link for a settled couple — leaving
    /// the parentage/children edges empty for a future genealogy/faction system to fill
    /// (births writing Father/Mother + the parents' Children, marriages writing Spouse).
    /// Held by reference so that system can mutate edges in place at load/seed time.
    public sealed class LineageData
    {
        public int FamilyId = -1;                 // household grouping; -1 = unset
        public string Surname = "";               // family name (also embedded in IdentityData.Name)
        public EntityId Father = EntityId.None;
        public EntityId Mother = EntityId.None;
        public EntityId Spouse = EntityId.None;
        public List<EntityId> Children = new List<EntityId>();
    }
}
