using System.Collections.Generic;
using System.Linq;

namespace DaggerfallWorkshop.Sim
{
    /// The prepotency DAG, built once from DriveCatalog's gate-edges. A drive
    /// gates another (this ⊣ Target); the graph must be acyclic and carry a
    /// topological order — the same guarantee the ODD tree needs (Kahn) — so the
    /// gates resolve in dependency order when intermediate rungs (safety) slot in
    /// between the deficiency poles and the growth drives.
    ///
    /// Today the gate is flat (two deficiency sources hard-culling the growth
    /// drives), so TopoOrder isn't yet load-bearing; building + validating it now
    /// means fear/curiosity drop in as new nodes without reworking the engine.
    /// See docs/drive_engine.md.
    public static class DriveGraph
    {
        /// Drives in topological order (a gate source precedes everything it
        /// gates). Computed by Kahn; a cycle throws at static init.
        public static readonly int[] TopoOrder;

        /// Drives that hard-cull something — the deficiency poles whose loudness
        /// suppresses (and past threshold culls) the growth/discretionary drives.
        /// The flat PrepotencyGate reads the MAX over these. Derived from the
        /// table, so adding a HardCull edge to a new pole extends the gate with no
        /// engine edit.
        public static readonly int[] HardCullSources;

        /// Per the gate table, the axes that ANY deficiency hard-culls (the
        /// discretionary/growth set = {SocialDef} after the F3 correction). F3's
        /// by-axis cull removes ads whose ServesAxis is in here. Adding a HardCull
        /// edge to a new target auto-extends it with no engine edit.
        public static readonly int[] HardCullTargets;

        /// Per-target, the drives that grade it DOWN via a DesperationGraded edge
        /// (e.g. hunger ⊣ fear: starvation overrides fear, never culls it). Indexed
        /// by target axis; empty where nothing desperation-grades it. The
        /// escape-affordability mechanism reads this.
        public static readonly int[][] DesperationSourcesByTarget;

        static DriveGraph()
        {
            int n = NeedAxis.Count;
            var indegree = new int[n];
            var adj = new List<int>[n];
            for (int i = 0; i < n; i++) adj[i] = new List<int>();

            var cullSources = new List<int>();
            var cullTargets = new SortedSet<int>();
            var desperationByTarget = new List<int>[n];
            for (int i = 0; i < n; i++) desperationByTarget[i] = new List<int>();
            for (int src = 0; src < n; src++)
            {
                var gates = DriveCatalog.Defs[src].Gates;
                if (gates == null) continue;
                bool culls = false;
                for (int g = 0; g < gates.Length; g++)
                {
                    int tgt = gates[g].Target;
                    adj[src].Add(tgt);
                    indegree[tgt]++;
                    if (gates[g].Kind == GateKind.HardCull) { culls = true; cullTargets.Add(tgt); }
                    else if (gates[g].Kind == GateKind.DesperationGraded) desperationByTarget[tgt].Add(src);
                }
                if (culls) cullSources.Add(src);
            }
            HardCullSources = cullSources.ToArray();
            HardCullTargets = cullTargets.ToArray();
            DesperationSourcesByTarget = new int[n][];
            for (int i = 0; i < n; i++) DesperationSourcesByTarget[i] = desperationByTarget[i].ToArray();

            // Kahn: repeatedly emit a zero-indegree node, lowest index first so the
            // order is deterministic.
            var order = new List<int>(n);
            var ready = new SortedSet<int>();
            for (int i = 0; i < n; i++) if (indegree[i] == 0) ready.Add(i);
            while (ready.Count > 0)
            {
                int v = ready.Min;
                ready.Remove(v);
                order.Add(v);
                foreach (int w in adj[v])
                    if (--indegree[w] == 0) ready.Add(w);
            }
            if (order.Count != n)
                throw new System.InvalidOperationException(
                    "DriveCatalog gate-edges form a cycle — the prepotency graph must be a DAG.");
            TopoOrder = order.ToArray();
        }
    }
}
