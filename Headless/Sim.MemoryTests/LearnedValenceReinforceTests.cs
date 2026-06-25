using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class LearnedValenceReinforceTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        [Fact]
        public void Signature_ReturnsIdentityAtomsOnly()
        {
            var e = new EventBus();
            var p = new PerceivableRegistry(e);
            var id = new EntityId(7);
            p.Seed(id, PerceivableAtoms.Kind(EntityKind.CivilianNPC), Fixed.One);   // identity
            p.Seed(id, PerceivableAtoms.Activity(ActivityKind.Beg), Fixed.One);      // state
            var sig = p.Signature(id);
            // A signature is identity atoms only — never a transient activity/somatic atom.
            Assert.All(sig.Atoms, a => Assert.True(AtomCatalog.For(a.Type).IsIdentity,
                "signature carried a non-identity atom: " + AtomCatalog.NameOf(a.Type)));
            Assert.Contains(sig.Atoms, a => a.Type.Value == PerceivableAtoms.Kind(EntityKind.CivilianNPC).Value);
        }

        [Fact]
        public void RecognizedValence_KnownCategory_ReturnsValenceConfidence()
        {
            var m = new MeaningsStore(16, MeaningsConfig.Default);
            var sig = Bag((1001, 1.0));
            var id = m.AddNode(sig, Fixed.FromDouble(-0.5), Fixed.FromDouble(0.8), false);
            Assert.True(m.RecognizedValence(sig, out var v, out var c));
            Assert.Equal(Fixed.FromDouble(-0.5), v);
            Assert.Equal(Fixed.FromDouble(0.8), c);
        }

        [Fact]
        public void RecognizedValence_Unknown_FalseAndZero()
        {
            var m = new MeaningsStore(16, MeaningsConfig.Default);
            Assert.False(m.RecognizedValence(Bag((9999, 1.0)), out var v, out var c));
            Assert.Equal(Fixed.Zero, v);
            Assert.Equal(Fixed.Zero, c);
        }

        [Fact]
        public void ReinforceIntent_MovesRecognizedCategoryValence()
        {
            var e = new EventBus();
            var r = new AgentMemoryRegistry(e, AgentMemoryConfig.Default);
            r.Seed(new EntityId(1));
            var sig = Bag((1001, 1.0));
            r.TryGet(new EntityId(1), out var mem);
            mem.Meanings.AddNode(sig, Fixed.Zero, Fixed.FromDouble(0.5), false);   // a known category, neutral

            for (int i = 0; i < 10; i++)
                e.Publish(new MemoryReinforceIntent { Perceiver = new EntityId(1), Signature = sig, Outcome = Fixed.FromDouble(1.0) });
            e.Tick(); r.Update(0);

            r.TryGet(new EntityId(1), out mem);
            mem.Meanings.RecognizedValence(sig, out var v, out _);
            Assert.True(v.ToDouble() > 0.3);   // reinforced positive
        }

        [Fact]
        public void ReinforceIntent_UnrecognizedSignature_NoOp()
        {
            var e = new EventBus();
            var r = new AgentMemoryRegistry(e, AgentMemoryConfig.Default);
            r.Seed(new EntityId(1));
            e.Publish(new MemoryReinforceIntent { Perceiver = new EntityId(1), Signature = Bag((9999, 1.0)), Outcome = Fixed.One });
            e.Tick(); r.Update(0);
            r.TryGet(new EntityId(1), out var mem);
            Assert.Equal(0, mem.Meanings.Count);   // nothing minted/changed
        }
    }
}
