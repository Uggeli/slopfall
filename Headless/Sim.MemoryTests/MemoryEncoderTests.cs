using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class MemoryEncoderTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        // A store with one fox category whose prediction is {1..5 at 1.0}.
        static MeaningsStore FoxStore(out CategoryId fox)
        {
            var s = new MeaningsStore(16, MeaningsConfig.Default);
            fox = s.AddNode(Bag((1, 1.0)), Fixed.FromDouble(-1.0), Fixed.FromDouble(0.9), true);
            var features = Bag((1, 1.0), (2, 1.0), (3, 1.0), (4, 1.0), (5, 1.0));
            for (int i = 0; i < 4; i++) s.Fold(fox, features);   // build the prediction
            return s;
        }

        static readonly AtomBag FoxSignature = Bag((1, 1.0));

        [Fact]
        public void CalmFamiliarFox_LeavesNoTrace()
        {
            var s = FoxStore(out _);
            var percept = Bag((1, 1.0), (2, 1.0), (3, 1.0), (4, 1.0), (5, 1.0));
            var r = MemoryEncoder.Perceive(s, FoxSignature, percept, Fixed.Zero, new MemoryKey(7), 100, EncodeConfig.Default);

            Assert.False(r.Written);                     // matches prediction, no arousal -> nothing stored
            Assert.Equal(Fixed.Zero, r.Surprise.Encode);
        }

        [Fact]
        public void NotchedEar_SpikesAttention_DoesNotFloodTheStore()
        {
            var s = FoxStore(out _);
            var percept = Bag((1, 1.0), (2, 1.0), (3, 1.0), (4, 1.0), (5, 1.0), (99, 1.0));
            var r = MemoryEncoder.Perceive(s, FoxSignature, percept, Fixed.Zero, new MemoryKey(7), 100, EncodeConfig.Default);

            Assert.Equal(Fixed.One, r.Surprise.Attention);   // attention spikes on the new feature
            Assert.False(r.Written);                         // but MEAN-encode is below threshold -> no flood
        }

        [Fact]
        public void NotchedEar_WhenWritten_StoresOnlyTheDelta_AtItsOwnStrength()
        {
            var s = FoxStore(out _);
            var percept = Bag((1, 1.0), (2, 1.0), (3, 1.0), (4, 1.0), (5, 1.0), (99, 1.0));
            // Threshold low enough to write: proves delta-only storage. Under per-atom strength the
            // notched-ear atom is etched at ITS OWN divergence (strong), not MEAN-diluted across the
            // five matched features — the MEAN only gates whether we write (anti-flood), not how deep.
            var cfg = new EncodeConfig(0, 154);
            var r = MemoryEncoder.Perceive(s, FoxSignature, percept, Fixed.Zero, new MemoryKey(7), 100, cfg);

            Assert.True(r.Written);
            Assert.Equal(new[] { 99 }, r.Record.DeltaBag.Atoms.Select(a => a.Type.Value).ToArray());   // ONLY the new atom
            Assert.True(r.Record.Meta[0].Strength > 200);    // the surprising atom etched deep
            Assert.True(r.Record.Meta[0].IsSurprise);        // surprise-driven
        }

        [Fact]
        public void DeltaAtoms_EtchedByTheirOwnError_NotOneRecordStrength()
        {
            var s = FoxStore(out _);
            // Atom 2 wildly off (err ~1.0), atom 3 slightly off (err ~0.1); the rest match.
            var percept = Bag((1, 1.0), (2, 0.0), (3, 0.9), (4, 1.0), (5, 1.0));
            var cfg = new EncodeConfig(0, 999);   // gate on surprise only; force the write
            var r = MemoryEncoder.Perceive(s, FoxSignature, percept, Fixed.Zero, new MemoryKey(7), 100, cfg);

            Assert.True(r.Written);
            Assert.Equal(new[] { 2, 3 }, r.Record.DeltaBag.Atoms.Select(a => a.Type.Value).ToArray());
            byte s2 = r.Record.Meta[0].Strength;   // atom 2 (big divergence)
            byte s3 = r.Record.Meta[1].Strength;   // atom 3 (small divergence)
            Assert.True(s2 > 200);                  // deep etch
            Assert.True(s3 < 64);                   // shallow etch — same record, different importance
            Assert.True(s2 > s3);
        }

        [Fact]
        public void TotallyWrongFox_WritesStrongly()
        {
            var s = FoxStore(out _);
            var percept = Bag((1, 0.0), (2, 0.0), (3, 0.0), (4, 0.0), (5, 0.0));
            var r = MemoryEncoder.Perceive(s, FoxSignature, percept, Fixed.Zero, new MemoryKey(7), 100, EncodeConfig.Default);

            Assert.True(r.Written);
            Assert.True(r.Record.Strength > 200);            // big divergence -> deep etch
            Assert.True(r.Record.IsSurprise);
            Assert.Equal(5, r.Record.DeltaBag.Count);        // all five contradicted features stored
        }

        [Fact]
        public void HighArousal_WritesEvenWhenUnsurprising()
        {
            var s = FoxStore(out _);
            var percept = Bag((1, 1.0), (2, 1.0), (3, 1.0), (4, 1.0), (5, 1.0));   // zero surprise
            var r = MemoryEncoder.Perceive(s, FoxSignature, percept, Fixed.FromDouble(0.9), new MemoryKey(7), 100, EncodeConfig.Default);

            Assert.True(r.Written);                          // arousal arm fired
            Assert.False(r.Record.IsSurprise);               // not surprise-driven -> no SURPRISE flag
            Assert.True(r.Record.Strength > 200);            // strength from arousal
        }

        [Fact]
        public void NovelPercept_StoresVerbatim_AtMaximalSurprise()
        {
            var s = FoxStore(out _);
            var novelSig = Bag((50, 1.0));                    // matches no prototype
            var percept = Bag((50, 1.0), (51, 0.7));
            var r = MemoryEncoder.Perceive(s, novelSig, percept, Fixed.Zero, new MemoryKey(7), 100, EncodeConfig.Default);

            Assert.True(r.Category.IsNone);                  // novelty
            Assert.Equal(Surprise.Maximal.Encode, r.Surprise.Encode);
            Assert.True(r.Written);
            Assert.True(r.Record.IsNovel);                   // categoryRef None
            Assert.True(r.Record.IsSurprise);                // novelty is maximal surprise -> decay-resistant until MINT
            Assert.Equal(new[] { 50, 51 }, r.Record.DeltaBag.Atoms.Select(a => a.Type.Value).ToArray());   // full percept verbatim
            Assert.Equal((byte)255, r.Record.Strength);
        }

        [Fact]
        public void Perceive_FoldsOnRecognition()
        {
            var s = FoxStore(out var fox);
            s.TryGetNode(fox, out var before);
            int typesBefore = before.Predicted.TypeCount;
            MemoryEncoder.Perceive(s, FoxSignature,
                Bag((1, 1.0), (2, 1.0), (3, 1.0), (4, 1.0), (5, 1.0), (7, 1.0)),
                Fixed.Zero, new MemoryKey(7), 100, EncodeConfig.Default);
            s.TryGetNode(fox, out var after);
            Assert.True(after.Predicted.TypeCount > typesBefore);   // the new type 7 was folded in
        }
    }
}
