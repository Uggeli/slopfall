using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    // P2: Inform reception writes SECOND-HAND place memory — weaker than first-hand, trust-scaled,
    // never SURPRISE, never downgrading what was witnessed. Plus the full HeardUtterance → place chain.
    public class CommReceptionTests
    {
        static bool DangerMeta(AgentMemoryRegistry r, EntityId a, int building, out AtomMeta m)
        {
            m = default;
            if (!r.TryGet(a, out var mem)) return false;
            if (!mem.Stores.Places.TryGet(new MemoryKey(building), out var rec)) return false;
            var atoms = rec.DeltaBag.Atoms;
            for (int i = 0; i < atoms.Count; i++)
                if (atoms[i].Type == PlaceAtoms.Danger) { m = rec.Meta[i]; return true; }
            return false;
        }

        static void Observe(EventBus e, AgentMemoryRegistry r, EntityId a, int b, bool secondHand, double trust)
        {
            e.Publish(new PlaceObserveIntent { Agent = a, Building = b, Atom = PlaceAtoms.Danger, Value = Fixed.One,
                                               SecondHand = secondHand, TrustScale = Fixed.FromDouble(trust) });
            e.Tick(); r.Update(0);
        }

        static AgentMemoryRegistry Seeded(out EventBus e, out EntityId a)
        {
            e = new EventBus();
            var r = new AgentMemoryRegistry(e, AgentMemoryConfig.Default);
            a = new EntityId(1); r.Seed(a);
            return r;
        }

        [Fact]
        public void SecondHandDanger_IsWeaker_AndNotSurprise()
        {
            var r = Seeded(out var e, out var a);
            Observe(e, r, a, 5, secondHand: true, trust: 0.5);
            Assert.True(DangerMeta(r, a, 5, out var m));
            Assert.False(m.IsSurprise);                         // hearsay fades, not decay-resistant
            Assert.True(m.Strength > 0 && m.Strength < 200);    // ~127 (255 × 0.5), well under first-hand 255
        }

        [Fact]
        public void FirstHandDanger_IsVivid_SurpriseFlagged()
        {
            var r = Seeded(out var e, out var a);
            Observe(e, r, a, 5, secondHand: false, trust: 1.0);
            Assert.True(DangerMeta(r, a, 5, out var m));
            Assert.True(m.IsSurprise);
            Assert.Equal((byte)255, m.Strength);
        }

        [Fact]
        public void SecondHand_DoesNotDowngrade_FirstHand()
        {
            var r = Seeded(out var e, out var a);
            Observe(e, r, a, 5, secondHand: false, trust: 1.0);   // witnessed: 255 SURPRISE
            Observe(e, r, a, 5, secondHand: true, trust: 0.5);    // then merely told
            Assert.True(DangerMeta(r, a, 5, out var m));
            Assert.True(m.IsSurprise);                            // still first-hand
            Assert.Equal((byte)255, m.Strength);
        }

        [Fact]
        public void ZeroTrust_NoBelief()
        {
            var r = Seeded(out var e, out var a);
            Observe(e, r, a, 5, secondHand: true, trust: 0.0);
            Assert.False(DangerMeta(r, a, 5, out _));             // distrusted teller → nothing written
        }

        [Fact]
        public void HeardInformDanger_RoutesToSecondHandPlaceMemory()
        {
            var e = new EventBus();
            var mem = new AgentMemoryRegistry(e, AgentMemoryConfig.Default);
            var rel = new RelationsRegistry(e);
            var comm = new CommunicationSystem(e, rel);
            var speaker = new EntityId(1); var hearer = new EntityId(2);
            mem.Seed(hearer);

            var u = new Utterance
            {
                Speaker = speaker, Audience = EntityId.None, Channel = CommChannel.Shout, Act = SpeechAct.Inform,
                SubjectBuilding = 7, Content = AtomBag.Create(new[] { new Atom(PlaceAtoms.Danger, Fixed.One) }),
                Confidence = Fixed.One
            };
            e.Publish(new HeardUtterance { Hearer = hearer, Said = u });
            e.Tick(); comm.Update(0);   // route Inform → PlaceObserveIntent (second-hand)
            e.Tick(); mem.Update(0);    // apply

            Assert.True(DangerMeta(mem, hearer, 7, out var m));
            Assert.False(m.IsSurprise);                 // arrived by word of mouth
            Assert.True(m.Strength > 0 && m.Strength < 255);
        }
    }
}
