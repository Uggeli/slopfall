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
        public int GateIndex = -1;      // assigned gate post in the settlement (−1 = no gates / civilian fallback)
        public bool NightShift;         // true = night watch, false = day watch (assigned by guard index)
        public float GateX, GateZ;      // world position of the assigned gate post
    }
}
