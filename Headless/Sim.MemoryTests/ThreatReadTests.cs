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
            // a small perceiver (0.4) facing a fanged 0.5 body
            double t = SubjectiveSystem.ThreatRead(Bag((AtomName.Fanged, 1.0), (AtomName.Size, 0.5)), 0.4, out double v);
            Assert.True(t > 0.0 && t <= 1.0);
            Assert.True(v < 0.0);                                   // a thing that scares you reads aversive
        }

        [Fact]
        public void Veto_IsTheMaxCue_FangedDominatesFast()
        {
            double both = SubjectiveSystem.ThreatRead(Bag((AtomName.Fanged, 1.0), (AtomName.Fast, 1.0), (AtomName.Size, 0.5)), 0.4, out _);
            double fastOnly = SubjectiveSystem.ThreatRead(Bag((AtomName.Fast, 1.0), (AtomName.Size, 0.5)), 0.4, out _);
            Assert.True(both > fastOnly);                           // Fanged (worse) sets the veto
        }

        [Fact]
        public void PerceiverSize_Modulates_BigPerceiverFearsLess()
        {
            var beast = Bag((AtomName.Fanged, 1.0), (AtomName.Size, 0.5));
            double small = SubjectiveSystem.ThreatRead(beast, 0.3, out _);   // small prey: my size is the denominator
            double big   = SubjectiveSystem.ThreatRead(beast, 0.9, out _);   // big perceiver: relative menace shrinks
            Assert.True(small > big);
        }

        [Fact]
        public void SameSizeUnarmed_ReadsNoThreat()
        {
            // a 0.4 human perceiving another 0.4 human (no weapons): equals don't menace
            double t = SubjectiveSystem.ThreatRead(Bag((AtomName.Civilian, 1.0), (AtomName.Size, 0.4)), 0.4, out double v);
            Assert.Equal(0.0, t);
            Assert.Equal(0.0, v);
        }

        [Fact]
        public void MuchBiggerUnarmedBody_MenacesViaMass()
        {
            // a 0.4 perceiver vs a big unarmed body (0.9): mass-menace fires (rel > 1)
            double t = SubjectiveSystem.ThreatRead(Bag((AtomName.Size, 0.9)), 0.4, out _);
            Assert.True(t > 0.0);
        }

        [Fact]
        public void NeutralBag_NoSize_ReadsNoThreat()
        {
            double t = SubjectiveSystem.ThreatRead(Bag((AtomName.Civilian, 1.0), (AtomName.Resident, 1.0)), 0.4, out double v);
            Assert.Equal(0.0, t);
            Assert.Equal(0.0, v);
        }
    }
}
