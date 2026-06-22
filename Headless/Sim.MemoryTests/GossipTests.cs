using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    // P3: action NoiseLevel drives audible reach (channel↔noise unification, radii ~3/8/25), and a
    // Gossip-activity agent voluntarily emits Inform turns drawn from its OWN place memory so danger
    // AND provisions knowledge spread by word of mouth.
    public class GossipTests
    {
        // --- 1. NoiseLevel ↔ hearing radius -------------------------------------------------------

        [Fact]
        public void LouderChannel_ReachesFarther()
        {
            Assert.True(HearingSystem.NoiseToRadius(Communication.NoiseOf(CommChannel.Talk))
                      > HearingSystem.NoiseToRadius(Communication.NoiseOf(CommChannel.Whisper)));
            Assert.True(HearingSystem.NoiseToRadius(Communication.NoiseOf(CommChannel.Shout))
                      > HearingSystem.NoiseToRadius(Communication.NoiseOf(CommChannel.Talk)));
        }

        [Fact]
        public void SpeechRadii_StayCalibrated_3_8_25()
        {
            // The P1 effective radii must be preserved so P2's danger soak isn't perturbed.
            Assert.InRange(HearingSystem.NoiseToRadius(Communication.NoiseOf(CommChannel.Whisper)), 2.5f, 3.5f);
            Assert.InRange(HearingSystem.NoiseToRadius(Communication.NoiseOf(CommChannel.Talk)), 7f, 9f);
            Assert.InRange(HearingSystem.NoiseToRadius(Communication.NoiseOf(CommChannel.Shout)), 23f, 27f);
        }

        // --- 2. The Gossip activity exists and is a social, noisy, social-relieving spec ----------

        [Fact]
        public void GossipSpec_IsSocial_AndModeledOnSocialize()
        {
            var s = ActivityCatalog.SpecFor(ActivityKind.Gossip);
            Assert.NotNull(s);
            Assert.Equal(ActivityKind.Gossip, s.Kind);
            Assert.True(s.Social);
            Assert.True(s.NoiseLevel > 0.2 && s.NoiseLevel < 0.7);   // talk-ish
            Assert.True(s.Delta[NeedAxis.SocialDef] < 0);            // relieves social deficit, like Socialize
            Assert.True(s.DurationMinutes > 0);
        }

        // --- 3. GossipSpeakSystem emits Inform turns from the speaker's OWN memory ----------------

        sealed class GossipRig
        {
            public readonly EventBus E = new EventBus();
            public readonly BehaviorRegistry Behavior;
            public readonly AgentMemoryRegistry Memory;
            public readonly GossipSpeakSystem System;

            public GossipRig()
            {
                Behavior = new BehaviorRegistry(E);
                Memory = new AgentMemoryRegistry(E, AgentMemoryConfig.Default);
                System = new GossipSpeakSystem(E, Behavior, Memory);
            }

            public void Gossiping(EntityId id)
            {
                Memory.Seed(id);
                E.Publish(new BehaviorSetIntent
                { Id = id, Data = new BehaviorData { Activity = ActivityKind.Gossip, Phase = ActivityPhase.Doing } });
            }

            public void KnowsPlace(EntityId id, int building, AtomTypeId atom, Fixed value)
                => Memory.SeedPlace(id, building, atom, value);

            public void ApplyState() { E.Tick(); Behavior.Update(0); Memory.Update(0); }

            // Run the gossip system across [0, ticks) and collect every Utterance it published.
            public Utterance[] Speak(long ticks)
            {
                var said = new System.Collections.Generic.List<Utterance>();
                for (long t = 0; t < ticks; t++)
                {
                    System.Update(t);
                    E.Tick();
                    said.AddRange(E.GetEvents<Utterance>().ToArray());
                }
                return said.ToArray();
            }
        }

        static readonly EntityId Talker = new EntityId(1);

        [Fact]
        public void Gossiper_EmitsInformOfKnownPlaceFact_OverItsDuration()
        {
            var r = new GossipRig();
            r.Gossiping(Talker);
            r.KnowsPlace(Talker, 7, PlaceAtoms.Danger, Fixed.One);
            r.ApplyState();

            var said = r.Speak(2000);   // a few cadence windows
            Assert.NotEmpty(said);
            var u = said[0];
            Assert.Equal(Talker, u.Speaker);
            Assert.Equal(SpeechAct.Inform, u.Act);
            Assert.Equal(CommChannel.Talk, u.Channel);
            Assert.Equal(7, u.SubjectBuilding);
            Assert.NotNull(u.Content);
            Assert.True(u.Content.Contains(PlaceAtoms.Danger));
        }

        [Fact]
        public void Gossiper_AlsoSpreadsProvisions_NotJustDanger()
        {
            var r = new GossipRig();
            r.Gossiping(Talker);
            r.KnowsPlace(Talker, 4, PlaceAtoms.Provisions, Fixed.One);
            r.ApplyState();

            var said = r.Speak(2000);
            Assert.NotEmpty(said);
            Assert.All(said, u => Assert.True(u.Content.Contains(PlaceAtoms.Provisions)));
            Assert.All(said, u => Assert.Equal(4, u.SubjectBuilding));
        }

        [Fact]
        public void NonGossiper_SaysNothing()
        {
            var r = new GossipRig();
            r.Memory.Seed(Talker);
            r.E.Publish(new BehaviorSetIntent
            { Id = Talker, Data = new BehaviorData { Activity = ActivityKind.Socialize, Phase = ActivityPhase.Doing } });
            r.KnowsPlace(Talker, 7, PlaceAtoms.Danger, Fixed.One);
            r.ApplyState();

            Assert.Empty(r.Speak(2000));
        }

        [Fact]
        public void GossiperWithNoShareableFact_SaysNothing()
        {
            var r = new GossipRig();
            r.Gossiping(Talker);   // knows no place facts
            r.ApplyState();

            Assert.Empty(r.Speak(2000));
        }

        // --- 4. End-to-end: a bystander overhears a gossiped fact and gains it second-hand --------

        [Fact]
        public void OverheardGossip_BecomesSecondHandMemory_ForABystander()
        {
            var e = new EventBus();
            var behavior = new BehaviorRegistry(e);
            var position = new PositionRegistry(e);
            var memory = new AgentMemoryRegistry(e, AgentMemoryConfig.Default);
            var relations = new RelationsRegistry(e);

            var gossip = new GossipSpeakSystem(e, behavior, memory);
            var hearing = new HearingSystem(e, behavior, position);
            var comm = new CommunicationSystem(e, relations);

            var speaker = new EntityId(1);
            var bystander = new EntityId(2);

            memory.Seed(speaker);
            memory.Seed(bystander);
            memory.SeedPlace(speaker, 9, PlaceAtoms.Danger, Fixed.One);
            position.Seed(speaker, 0f, 0f, 0f, 0f);
            position.Seed(bystander, 4f, 0f, 0f, 0f);   // within Talk radius (~8)
            e.Publish(new BehaviorSetIntent
            { Id = speaker, Data = new BehaviorData { Activity = ActivityKind.Gossip, Phase = ActivityPhase.Doing } });
            e.Publish(new BehaviorSetIntent
            { Id = bystander, Data = new BehaviorData { Activity = ActivityKind.Idle } });
            e.Tick();
            behavior.Update(0); position.Update(0); memory.Update(0);

            // Drive the pipeline for a while: gossip → hear → receive → apply.
            bool learned = false;
            for (long t = 0; t < 3000 && !learned; t++)
            {
                gossip.Update(t);
                e.Tick();
                hearing.Update(t);
                e.Tick();
                comm.Update(t);
                e.Tick();
                memory.Update(t);
                if (memory.TryGet(bystander, out var mem)
                    && mem.Stores.Places.TryGet(new MemoryKey(9), out var rec)
                    && rec.DeltaBag.Contains(PlaceAtoms.Danger))
                    learned = true;
            }
            Assert.True(learned, "bystander never learned the gossiped danger second-hand");

            memory.TryGet(bystander, out var bm);
            bm.Stores.Places.TryGet(new MemoryKey(9), out var brec);
            var atoms = brec.DeltaBag.Atoms;
            for (int i = 0; i < atoms.Count; i++)
                if (atoms[i].Type == PlaceAtoms.Danger)
                {
                    Assert.False(brec.Meta[i].IsSurprise);                 // word of mouth, not witnessed
                    Assert.True(brec.Meta[i].Strength > 0 && brec.Meta[i].Strength < 255);
                }
        }
    }
}
