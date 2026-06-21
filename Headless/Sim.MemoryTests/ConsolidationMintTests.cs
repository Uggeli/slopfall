using System.Linq;
using DaggerfallWorkshop.Sim.Memory;
using Xunit;

namespace Sim.MemoryTests
{
    public class ConsolidationMintTests
    {
        static AtomBag Bag(params (int type, double v)[] atoms)
            => AtomBag.Create(atoms.Select(a => new Atom(new AtomTypeId(a.type), Fixed.FromDouble(a.v))));

        static MemoryRecord Novel(long key, AtomBag delta)
            => new MemoryRecord(new MemoryKey(key), CategoryId.None, delta, 150, 100, 100, MemoryFlags.Surprise);

        static MemoryStore ThreeSimilarNovels()
        {
            var store = new MemoryStore(16);
            // Three novel records sharing {1,2} at 1.0, each with one tiny unique atom.
            store.Encode(Novel(10, Bag((1, 1.0), (2, 1.0), (10, 0.05))));
            store.Encode(Novel(20, Bag((1, 1.0), (2, 1.0), (11, 0.05))));
            store.Encode(Novel(30, Bag((1, 1.0), (2, 1.0), (12, 0.05))));
            return store;
        }

        [Fact]
        public void Mint_ClusterOfSimilarNovels_MintsCategory_AndRekeys()
        {
            var meanings = new MeaningsStore(16, MeaningsConfig.Default);
            var store = ThreeSimilarNovels();

            int meaningsBefore = meanings.Count;
            Consolidation.Mint(store, meanings, ConsolidationConfig.Default);

            Assert.Equal(meaningsBefore + 1, meanings.Count);          // one fact minted
            // Each record is now recognized and re-diffed down to just its unique atom.
            for (long k = 10; k <= 30; k += 10)
            {
                store.TryGet(new MemoryKey(k), out var r);
                Assert.False(r.CategoryRef.IsNone);                   // no longer novel
                Assert.Equal(1, r.DeltaBag.Count);                    // only the unique atom remains
            }
        }

        [Fact]
        public void Mint_Rekeyed_DeltaIsOnlyTheUniqueAtom()
        {
            var meanings = new MeaningsStore(16, MeaningsConfig.Default);
            var store = ThreeSimilarNovels();

            Consolidation.Mint(store, meanings, ConsolidationConfig.Default);

            store.TryGet(new MemoryKey(10), out var r);
            Assert.Equal(new[] { 10 }, r.DeltaBag.Atoms.Select(a => a.Type.Value).ToArray());   // shared {1,2} migrated into the fact
        }

        [Fact]
        public void Mint_AbsorbsOnlyExactMatches_NearMissAtomsRideInTheRecord()
        {
            // Honest compression boundary: the minted prototype value is an integer-truncated mean,
            // and Diff is exact-equality. Type 1 is identical across members (exact -> absorbed);
            // type 2 differs slightly (mean truncates to a value no member holds -> NOT absorbed,
            // so it rides in the re-keyed record). MINT compresses less than "the shared core
            // migrates into the fact" implies once values are noisy.
            var meanings = new MeaningsStore(16, MeaningsConfig.Default);
            var store = new MemoryStore(16);
            store.Encode(Novel(10, Bag((1, 1.0), (2, 1.0))));
            store.Encode(Novel(20, Bag((1, 1.0), (2, 0.99))));
            store.Encode(Novel(30, Bag((1, 1.0), (2, 0.98))));

            Consolidation.Mint(store, meanings, ConsolidationConfig.Default);

            Assert.Equal(1, meanings.Count);                     // still minted (both types low-variance)
            store.TryGet(new MemoryKey(10), out var r);
            Assert.False(r.CategoryRef.IsNone);                  // recognized
            Assert.Equal(new[] { 2 }, r.DeltaBag.Atoms.Select(a => a.Type.Value).ToArray());
            // type 1 (exact) absorbed into the fact; type 2 (near-miss) still rides in the record.
        }

        [Fact]
        public void Mint_TooFewMembers_DoesNotMint()
        {
            var meanings = new MeaningsStore(16, MeaningsConfig.Default);
            var store = new MemoryStore(16);
            store.Encode(Novel(10, Bag((1, 1.0), (2, 1.0))));     // a single novel record
            Consolidation.Mint(store, meanings, ConsolidationConfig.Default);

            Assert.Equal(0, meanings.Count);                     // below MinClusterSupport -> nothing minted
            store.TryGet(new MemoryKey(10), out var r);
            Assert.True(r.CategoryRef.IsNone);                   // still novel
        }
    }
}
