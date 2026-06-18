using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// The ODD tree (odd_spec.md Part 1): Propagate is lookahead — an action is worth
    /// what it ENABLES, so a near-worthless step (work) wins when it leads to a payoff
    /// (eat) that's otherwise out of reach. The exact thing depth-1 argmax can't do.
    public class OddTreeTests
    {
        // 0=Idle, 1=Work, 2=Buy, 3=Eat. Work→Buy→Eat is the chain; Eat is the reward.
        static readonly int[][] Chain = { new int[0], new[] { 2 }, new[] { 3 }, new int[0] };
        static readonly double[] Direct = { 0.05, 0.01, 0.01, 1.0 };   // idle floor; work/buy ≈0; eat big

        static int Decide(IReadOnlyList<int> roots, Func<int, IReadOnlyList<int>> enables)
        {
            var buf = new OddNode[64];
            int n = OddTree.Build(buf, 4, roots, enables, _ => true, a => Direct[a]);
            OddTree.Propagate(buf, n, 0.5);
            return OddTree.Traverse(buf, n);
        }

        [Fact]
        public void Lookahead_ChoosesWork_BecauseItEnablesTheMeal()
        {
            // C₀ offers Idle and Work; Buy/Eat are reachable only by working first.
            int next = Decide(new[] { 0, 1 }, a => Chain[a]);
            Assert.Equal(1, next);   // Work — its subtree (→Buy→Eat) outscores idling
        }

        [Fact]
        public void WithoutTheChain_IdleWins()
        {
            // Same ads, but nothing Enables anything → pure depth-1 argmax.
            int next = Decide(new[] { 0, 1 }, _ => new int[0]);
            Assert.Equal(0, next);   // Idle (0.05) beats bare Work (0.01) with no payoff ahead
        }

        [Fact]
        public void Propagate_DiscountsGeometricallyByDepth()
        {
            var buf = new OddNode[64];
            int n = OddTree.Build(buf, 4, new[] { 1 }, a => Chain[a], _ => true, a => Direct[a]);
            OddTree.Propagate(buf, n, 0.5);
            // Work's Total = 0.01 + decay·(0.01 + decay·1.0) = 0.01 + 0.5·0.51 = 0.265.
            Assert.Equal(0.265, buf[1].Total, 6);
        }

        [Fact]
        public void Build_ExcludesCycles_AndPlacesEachActionOnce()
        {
            // 0 enables 1, 1 enables 0 (a cycle). Each placed once; no infinite tree.
            var en = new int[][] { new[] { 1 }, new[] { 0 } };
            var buf = new OddNode[64];
            int n = OddTree.Build(buf, 2, new[] { 0 }, a => en[a], _ => true, _ => 1.0);
            Assert.Equal(3, n);   // root + action0 + action1 (the back-edge to 0 is skipped)
        }
    }
}
