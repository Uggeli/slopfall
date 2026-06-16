using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Who an agent works for. A resident's Labor wage is paid by their employer
    /// (a keeper/business) — a TRANSFER, not minted coin — so resident income
    /// recirculates the town's money instead of inflating it. Assigned at load;
    /// the employer-can't-pay case is real (no revenue → no wage). Wage rate
    /// lives on the activity for now; this just records the relationship.
    public sealed class EmploymentData
    {
        public EntityId Employer;       // the keeper/business that pays this agent (None if public-employed)
        public OwnerId PublicOwner;     // set → on the public payroll (a guard): salaried from this treasury (E3)
    }

    public sealed class EmploymentRegistry
    {
        readonly ConcurrentDictionary<EntityId, EmploymentData> _d = new ConcurrentDictionary<EntityId, EmploymentData>();

        public void Set(EntityId id, EmploymentData data) => _d[id] = data;
        public bool TryGet(EntityId id, out EmploymentData data) => _d.TryGetValue(id, out data);
        public void Remove(EntityId id) { _d.TryRemove(id, out var _); }
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, EmploymentData>> All => _d;
    }
}
