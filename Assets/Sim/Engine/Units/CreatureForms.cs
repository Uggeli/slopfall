using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>
    /// Per-creature-kind FORM atoms (weapons + body size) a creature broadcasts, so a perceiver can
    /// read threat from them (tone × size, in SubjectiveSystem). Two looks in L1: the form-scary Beast
    /// (fanged) and the weapon-less Drifter (looks harmless); the table gains rows — with their own
    /// sizes — as monster-type variety is introduced. Frozen, beside the other catalogs.
    /// </summary>
    public static class CreatureForms
    {
        // (atom, value): presence weapons at Fixed.One; Size graded (0..1). Frozen placeholders.
        public static readonly (AtomTypeId Type, Fixed Value)[] Beast =
        {
            (AtomName.Fanged.ToId(), Fixed.One),
            (AtomName.Fast.ToId(),   Fixed.One),
            (AtomName.Size.ToId(),   Fixed.FromDouble(0.5)),
        };

        // Phase F/L1: the Drifter looks like an ordinary person — a body Size, NO weapon atoms.
        // Mechanically a creature (it still attacks); perceptually harmless until learned.
        public static readonly (AtomTypeId Type, Fixed Value)[] Drifter =
        {
            (AtomName.Size.ToId(), Fixed.FromDouble(0.4)),
        };
    }
}
