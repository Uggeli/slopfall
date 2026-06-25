using System.Linq;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    // P4: the sim-side utterance buffer that the snapshot pump drains into each frame.
    // The pump publishes at ~5 Hz but the sim ticks far faster and the EventBus flips every
    // tick, so the buffer must ACCUMULATE every tick's utterances (not sample one tick); a
    // Drain() returns + clears the lot so each frame carries exactly the utterances spoken
    // since the previous drain. It is a read-only diagnostic accumulator — it never publishes.
    public class UtteranceWireTests
    {
        static readonly EntityId Speaker = new EntityId(7);
        static readonly EntityId Audience = new EntityId(9);

        static Utterance Say(int building, params Atom[] content) => new Utterance
        {
            Speaker = Speaker, Audience = Audience, Channel = CommChannel.Shout,
            Act = SpeechAct.Inform, SubjectBuilding = building,
            Content = AtomBag.Create(content), Confidence = Fixed.One,
        };

        // Publish one utterance this tick, flip, then run the registry — mirrors the engine's
        // one-tick latency (an event emitted on tick N is readable on tick N+1).
        static void TickWith(EventBus e, UtteranceLogRegistry log, long tick, params Utterance[] said)
        {
            foreach (var u in said) e.Publish(u);
            e.Tick();
            log.Update(tick);
        }

        [Fact]
        public void Buffer_AccumulatesEveryTick_ThenDrainsEmpty()
        {
            var e = new EventBus();
            var log = new UtteranceLogRegistry(e);

            // Speak across MANY ticks WITHOUT draining between them — proves the buffer keeps
            // every tick's utterances, not just the latest flipped batch.
            const int Ticks = 50;
            for (int t = 0; t < Ticks; t++)
                TickWith(e, log, t, Say(t, new Atom(PlaceAtoms.Danger, Fixed.One)));

            var drained = log.Drain();
            Assert.Equal(Ticks, drained.Count);
            // In speak order, and each carries its own building (none lost between publishes).
            Assert.Equal(Enumerable.Range(0, Ticks).ToArray(),
                drained.Select(u => u.SubjectBuilding).ToArray());

            // Drain emptied it: a second drain with no new speech returns nothing.
            Assert.Empty(log.Drain());
        }

        [Fact]
        public void Drain_ReturnsOnlyUtterancesSinceLastDrain()
        {
            var e = new EventBus();
            var log = new UtteranceLogRegistry(e);

            TickWith(e, log, 0, Say(1), Say(2));
            Assert.Equal(2, log.Drain().Count);

            TickWith(e, log, 1, Say(3));
            var second = log.Drain();
            Assert.Single(second);
            Assert.Equal(3, second[0].SubjectBuilding);
        }

        [Fact]
        public void Buffer_OverflowDropsOldest_KeepsNewest()
        {
            var e = new EventBus();
            var log = new UtteranceLogRegistry(e, capacity: 4);

            // Eight utterances into a 4-slot buffer: the oldest four fall off, newest four remain.
            for (int t = 0; t < 8; t++)
                TickWith(e, log, t, Say(t));

            var drained = log.Drain();
            Assert.Equal(4, drained.Count);
            Assert.Equal(new[] { 4, 5, 6, 7 }, drained.Select(u => u.SubjectBuilding).ToArray());
        }

        [Fact]
        public void ToFlatContent_ShapesAtomBagAsTypeValuePairs()
        {
            var bag = AtomBag.Create(new[]
            {
                new Atom(PlaceAtoms.Danger, Fixed.One),                 // PlaceDanger -> 1.0
                new Atom(PlaceAtoms.Kind(BuildingKind.Tavern), Fixed.FromDouble(0.5)),
            });

            var flat = UtteranceLogRegistry.ToFlatContent(bag);

            // Sorted ascending by atom type (AtomBag invariant), value round-trips via ToDouble.
            Assert.Equal(2, flat.Count);
            Assert.Equal(PlaceAtoms.Kind(BuildingKind.Tavern).Value, flat[0].atom);
            Assert.Equal(0.5, flat[0].value, 3);
            Assert.Equal(PlaceAtoms.Danger.Value, flat[1].atom);
            Assert.Equal(1.0, flat[1].value, 3);
        }

        [Fact]
        public void ToFlatContent_NullBag_IsEmpty()
        {
            Assert.Empty(UtteranceLogRegistry.ToFlatContent(null));
        }
    }
}
