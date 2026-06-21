using System.Collections.Generic;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Engine;
using Xunit;

namespace Sim.SpatialTests
{
    /// BuildSnapshot must faithfully mirror the OddNode buffer: same parent links,
    /// per-node scores, and the same winner Traverse picks (highest-Total root child).
    public class OddSnapshotTests
    {
        // Chain: 0=Idle, 1=Labor, 2=Buy, 3=Farm. Labor→Buy→Farm; index 3 is the reward.
        // (Verb names are cosmetic labels for the test; only the indices drive scoring.)
        static readonly int[][] Chain = { new int[0], new[] { 2 }, new[] { 3 }, new int[0] };
        static readonly double[] Direct = { 0.05, 0.01, 0.01, 1.0 };
        static readonly List<ActivityKind> Verbs = new List<ActivityKind>
            { ActivityKind.Idle, ActivityKind.Labor, ActivityKind.Buy, ActivityKind.Farm };

        [Fact]
        public void BuildSnapshot_MarksChosenRoot_AndPreservesParentLinks()
        {
            var buf = new OddNode[64];
            int n = OddTree.Build(buf, 4, new[] { 0, 1 }, a => Chain[a], _ => true, a => Direct[a]);
            OddTree.Propagate(buf, n, 0.5);
            int winnerVerb = OddTree.Traverse(buf, n);   // verb index of the chosen root action

            var snap = OddSystem.BuildSnapshot(buf, n, Verbs, 8, 14);

            Assert.Equal(8, snap.Hour);
            Assert.Equal(14, snap.Minute);
            Assert.Equal(n, snap.Nodes.Length);
            Assert.Null(snap.Nodes[0].Verb);                 // root sentinel
            Assert.Equal(-1, snap.Nodes[0].Parent);
            // Chosen node is a root child whose verb matches Traverse's pick (Work).
            Assert.True(snap.Chosen > 0);
            Assert.Equal(0, snap.Nodes[snap.Chosen].Parent); // it's a root-level action
            Assert.Equal(Verbs[winnerVerb].ToString(), snap.Nodes[snap.Chosen].Verb);
            Assert.Equal("Labor", snap.Nodes[snap.Chosen].Verb);
            // Every non-root node's parent index is valid and its Total = Direct + Prop.
            for (int i = 1; i < n; i++)
            {
                Assert.InRange(snap.Nodes[i].Parent, 0, n - 1);
                Assert.Equal(snap.Nodes[i].Direct + snap.Nodes[i].Prop, snap.Nodes[i].Total, 6);
            }
        }
    }
}
