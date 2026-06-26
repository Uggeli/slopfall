using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class GossipReinforceTests
    {
        static AtomBag MonsterKind()
            => AtomBag.Create(new[] { new Atom(AtomName.Beast.ToId(), Fixed.One) });

        static Utterance Alarm(EntityId speaker)
            => new Utterance
            {
                Speaker = speaker, Audience = EntityId.None, Channel = CommChannel.Shout, Act = SpeechAct.Inform,
                SubjectBuilding = 7, Content = AtomBag.Create(new[] { new Atom(PlaceAtoms.Danger, Fixed.One) }),
                SubjectKind = MonsterKind(), Confidence = Fixed.One
            };

        [Fact]
        public void HeardKindAlarm_EmitsSecondHandReinforce_ForTheKind()
        {
            var e = new EventBus();
            var rel = new RelationsRegistry(e);
            var comm = new CommunicationSystem(e, rel);
            var speaker = new EntityId(1); var hearer = new EntityId(2);

            e.Publish(new HeardUtterance { Hearer = hearer, Said = Alarm(speaker) });
            e.Tick(); comm.Update(0);
            e.Tick();

            var ri = e.GetEvents<MemoryReinforceIntent>().ToArray();
            Assert.Single(ri);
            Assert.Equal(hearer, ri[0].Perceiver);
            Assert.True(ri[0].Outcome.ToDouble() < 0.0);                                 // "this kind is bad"
            Assert.True(ri[0].Scale.ToDouble() > 0.0 && ri[0].Scale.ToDouble() <= 1.0);  // trust-scaled
            Assert.Contains(ri[0].Signature.Atoms,
                a => a.Type.Value == AtomName.Beast.ToId().Value);
        }

        [Fact]
        public void PlaceOnlyUtterance_EmitsNoKindReinforce()
        {
            var e = new EventBus();
            var rel = new RelationsRegistry(e);
            var comm = new CommunicationSystem(e, rel);
            var u = Alarm(new EntityId(1)); u.SubjectKind = null;   // a plain place-danger shout

            e.Publish(new HeardUtterance { Hearer = new EntityId(2), Said = u });
            e.Tick(); comm.Update(0);
            e.Tick();

            Assert.Empty(e.GetEvents<MemoryReinforceIntent>().ToArray());   // no kind belief → no reinforce
            Assert.NotEmpty(e.GetEvents<PlaceObserveIntent>().ToArray());   // place relay still fires
        }
    }
}
