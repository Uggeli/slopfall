using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    // P1: the hearing raw-sense. An Utterance is heard by every agent within its channel's audible
    // radius (addressee + eavesdroppers), nobody outside, never the speaker.
    public class HearingTests
    {
        sealed class Rig
        {
            public readonly EventBus E = new EventBus();
            public readonly BehaviorRegistry Behavior;
            public readonly PositionRegistry Position;
            public readonly HearingSystem System;

            public Rig()
            {
                Behavior = new BehaviorRegistry(E);
                Position = new PositionRegistry(E);
                System = new HearingSystem(E, Behavior, Position);
            }

            // Place an agent (direct position seed + behavior so it's an enumerable hearer).
            public void Place(EntityId id, float x, float z)
            {
                Position.Seed(id, x, 0f, z, 0f);
                E.Publish(new BehaviorSetIntent { Id = id, Data = new BehaviorData { Activity = ActivityKind.Idle } });
            }
            public void ApplyPlacements() { E.Tick(); Behavior.Update(0); }

            // Say something, then run the hearing sense, then read who heard it.
            public EntityId[] Heard(Utterance u)
            {
                E.Publish(u);
                E.Tick(); System.Update(0);          // hearing reads the (flipped) utterance, emits HeardUtterance
                E.Tick(); return E.GetEvents<HeardUtterance>().ToArray().Select(h => h.Hearer).ToArray();
            }
        }

        static readonly EntityId Speaker = new EntityId(1);
        static readonly EntityId Near = new EntityId(2);   // 5 units away
        static readonly EntityId Mid = new EntityId(3);    // 20 units away
        static readonly EntityId Far = new EntityId(4);    // 40 units away

        static Rig Town()
        {
            var r = new Rig();
            r.Place(Speaker, 0f, 0f);
            r.Place(Near, 5f, 0f);
            r.Place(Mid, 20f, 0f);
            r.Place(Far, 40f, 0f);
            r.ApplyPlacements();
            return r;
        }

        static Utterance Say(CommChannel ch) => new Utterance
        { Speaker = Speaker, Audience = EntityId.None, Channel = ch, Act = SpeechAct.Inform, Content = AtomBag.Empty, Confidence = Fixed.One };

        [Fact]
        public void Talk_ReachesNear_NotMidOrFar_NorSpeaker()
        {
            var heard = Town().Heard(Say(CommChannel.Talk));
            Assert.Contains(Near, heard);
            Assert.DoesNotContain(Mid, heard);
            Assert.DoesNotContain(Far, heard);
            Assert.DoesNotContain(Speaker, heard);   // never hears itself
        }

        [Fact]
        public void Shout_CarriesFartherThanTalk()
        {
            var heard = Town().Heard(Say(CommChannel.Shout));
            Assert.Contains(Near, heard);
            Assert.Contains(Mid, heard);            // 20u — within shout, was outside talk
            Assert.DoesNotContain(Far, heard);      // 40u — still too far
        }

        [Fact]
        public void Whisper_DoesNotEvenReachNear()
        {
            var heard = Town().Heard(Say(CommChannel.Whisper));
            Assert.DoesNotContain(Near, heard);     // 5u — outside the whisper radius
            Assert.Empty(heard);
        }
    }
}
