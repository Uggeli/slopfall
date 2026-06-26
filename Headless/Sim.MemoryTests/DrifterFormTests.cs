using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class DrifterFormTests
    {
        [Fact]
        public void Drifter_IsWeaponless_AndReadsHarmless()
        {
            // no weapon (capacity) atoms — only a body Size
            Assert.DoesNotContain(CreatureForms.Drifter, f => f.Type == AtomName.Fanged.ToId());
            Assert.DoesNotContain(CreatureForms.Drifter, f => f.Type == AtomName.Fast.ToId());
            Assert.Contains(CreatureForms.Drifter, f => f.Type == AtomName.Size.ToId());

            // ThreatRead over a Drifter's perceived bag (its form + its appearance) ≈ 0: it LOOKS harmless
            var bag = AtomBag.Create(
                CreatureForms.Drifter.Select(f => new Atom(f.Type, f.Value))
                    .Append(new Atom(AtomName.Drifter.ToId(), Fixed.One)).ToList());
            double t = SubjectiveSystem.ThreatRead(bag, 0.4, out _);
            Assert.True(t <= 0.0001);   // no weapon tone, not bigger than the perceiver → no form-threat
        }
    }
}
