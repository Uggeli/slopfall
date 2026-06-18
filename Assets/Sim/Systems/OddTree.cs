using System;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// One node of the ODD decision tree (docs/odd_spec.md Part 1): a flat,
    /// BFS-ordered buffer entry. Buffer[0] is the Object-Zero root (Action = -1).
    public struct OddNode
    {
        public int ParentIndex;
        public int Action;          // index into the caller's action set; -1 = root sentinel
        public int ChildStart;      // inclusive; ChildStart > ChildEnd ⟹ no children
        public int ChildEnd;
        public double DirectScore;
        public double PropagatedScore;
        public bool IsTerminal;
        public double Total => DirectScore + PropagatedScore;
    }

    /// The ODD decision algorithm (docs/odd_spec.md Part 1) — the planner the depth-1
    /// OddSystem converges toward (docs/odd_convergence.md, phase O2). Each action's
    /// Enables-DAG is expanded (Kahn's topo sort) into a flat BFS Node buffer; a single
    /// backward Propagate pass discounts each subtree's value up to its parent, so an
    /// action INHERITS the (geometrically decayed) value of what it ENABLES — "work" is
    /// worth the meal it eventually buys. Traverse picks the root-level action on the
    /// highest-value path. Index-based + pure → testable in isolation, before it drives
    /// the live decider.
    public static class OddTree
    {
        /// Build the BFS tree: seed C₀ (rootActions, precondition-gated) under the root,
        /// then expand each placed action's Enables (Kahn — each action placed at most
        /// once, so cycles/duplicates are silently excluded). Returns the node count;
        /// buffer[0] is the Object-Zero root.
        public static int Build(
            OddNode[] buffer, int actionCount,
            IReadOnlyList<int> rootActions,
            Func<int, IReadOnlyList<int>> enables,
            Func<int, bool> precond,
            Func<int, double> directScore)
        {
            var placed = new bool[actionCount];
            buffer[0] = new OddNode { ParentIndex = -1, Action = -1 };
            int cursor = 1;

            for (int r = 0; r < rootActions.Count && cursor < buffer.Length; r++)
            {
                int a = rootActions[r];
                if (placed[a] || !precond(a)) continue;
                buffer[cursor++] = new OddNode { ParentIndex = 0, Action = a, DirectScore = directScore(a) };
                placed[a] = true;
            }
            buffer[0].ChildStart = 1;            // the C₀ seeds ARE the root's children…
            buffer[0].ChildEnd = cursor - 1;     // …set explicitly; expansion below starts at the seeds

            for (int i = 1; i < cursor; i++)
            {
                int childStart = cursor;
                int act = buffer[i].Action;
                if (act >= 0)
                {
                    var en = enables(act);
                    if (en != null)
                        for (int j = 0; j < en.Count && cursor < buffer.Length; j++)
                        {
                            int e = en[j];
                            if (placed[e] || !precond(e)) continue;   // cycle/dupe or gated → excluded
                            buffer[cursor++] = new OddNode { ParentIndex = i, Action = e, DirectScore = directScore(e) };
                            placed[e] = true;
                        }
                }
                buffer[i].ChildStart = childStart;
                buffer[i].ChildEnd = cursor - 1;
                buffer[i].IsTerminal = childStart > cursor - 1 && i > 0;
            }
            return cursor;
        }

        /// Single backward pass (O(N)): each node hands its (Direct + Propagated) total,
        /// decayed, up to its parent. After it, a descendant d levels below a node
        /// contributes score × decay^d — the geometrically discounted subtree sum.
        public static void Propagate(OddNode[] buffer, int count, double decay)
        {
            for (int i = count - 1; i >= 1; i--)
            {
                double total = buffer[i].DirectScore + buffer[i].PropagatedScore;
                buffer[buffer[i].ParentIndex].PropagatedScore += total * decay;
            }
        }

        /// The root-level action on the highest-value path — the next thing to do (its
        /// whole subtree's value is already folded into its Total by Propagate). Returns
        /// −1 if the root has no children (Object Zero guarantees it does in practice).
        public static int Traverse(OddNode[] buffer, int count)
        {
            if (count <= 1 || buffer[0].ChildStart > buffer[0].ChildEnd) return -1;
            int best = buffer[0].ChildStart;
            for (int c = buffer[0].ChildStart + 1; c <= buffer[0].ChildEnd; c++)
                if (buffer[c].Total > buffer[best].Total) best = c;
            return buffer[best].Action;
        }
    }
}
