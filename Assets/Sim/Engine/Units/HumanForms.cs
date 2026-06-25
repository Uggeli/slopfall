using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>
    /// Per-human-kind FORM atoms a human agent broadcasts — its body Size today (weapons for armed
    /// roles arrive in C/L2). A perceiver reads threat RELATIVE to its own Size (SubjectiveSystem),
    /// so this is also what makes a small civilian dread a Beast. Mirrors CreatureForms. Frozen,
    /// soak-tuned; humans are unarmed (Size only) in L1.
    /// </summary>
    public static class HumanForms
    {
        // A human body, smaller than the generic Beast (0.5), so a civilian reads a Beast as
        // bigger-than-me. Frozen placeholder; the human/Beast size ratio is the fear lever.
        public static readonly (AtomTypeId Type, Fixed Value)[] Civilian =
        {
            (AtomName.Size.ToId(), Fixed.FromDouble(0.4)),
        };
    }
}
