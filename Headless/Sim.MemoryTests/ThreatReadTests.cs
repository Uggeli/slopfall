using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class ThreatReadTests
    {
        static AtomBag Bag(params (AtomName name, double value)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(a.name.ToId(), Fixed.FromDouble(a.value))).ToList());

        [Fact]
        public void Weapon_ReadsGradedThreat_AndAversiveValence()
        {
            double t = SubjectiveSystem.ThreatRead(Bag((AtomName.Fanged, 1.0), (AtomName.Size, 0.5)), out double v);
            Assert.True(t > 0.0 && t <= 1.0);
            Assert.True(v < 0.0);                                   // a thing that scares you reads aversive
        }

        [Fact]
        public void Veto_IsTheMaxCue_FangedDominatesFast()
        {
            double both = SubjectiveSystem.ThreatRead(Bag((AtomName.Fanged, 1.0), (AtomName.Fast, 1.0), (AtomName.Size, 0.5)), out _);
            double fastOnly = SubjectiveSystem.ThreatRead(Bag((AtomName.Fast, 1.0), (AtomName.Size, 0.5)), out _);
            Assert.True(both > fastOnly);                           // Fanged (worse) sets the veto
        }

        [Fact]
        public void Size_Modulates_SquirrelIsNearlyHarmlessBearIsScary()
        {
            double squirrel = SubjectiveSystem.ThreatRead(Bag((AtomName.Fanged, 1.0), (AtomName.Size, 0.1)), out _);
            double bear     = SubjectiveSystem.ThreatRead(Bag((AtomName.Fanged, 1.0), (AtomName.Size, 0.9)), out _);
            Assert.True(squirrel < bear);
            Assert.True(squirrel < 0.2);                            // same fangs, tiny body → ~harmless
        }

        [Fact]
        public void NeutralBag_ReadsNoThreat()
        {
            double t = SubjectiveSystem.ThreatRead(Bag((AtomName.Civilian, 1.0), (AtomName.Resident, 1.0)), out double v);
            Assert.Equal(0.0, t);
            Assert.Equal(0.0, v);
        }
    }
}
