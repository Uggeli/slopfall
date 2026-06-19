using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// State fingerprint of a SimWorld — diverges if two runs differ. Used to verify
    /// the parallel engine produces identical state to a serial run.
    public static class SimWorldBridge
    {
        public static string Fingerprint(SimWorld w)
        {
            double coin = 0; foreach (var kv in w.Coin.All) coin += kv.Value;
            double posSum = 0; int agents = 0;
            foreach (var kv in w.Position.All) { posSum += kv.Value.X + kv.Value.Z; agents++; }
            double hunger = 0; foreach (var kv in w.Needs.All) hunger += kv.Value.V[NeedAxis.Hunger];
            var counts = new Dictionary<ActivityKind, int>();
            foreach (var kv in w.Behavior.All) { if (kv.Value == null) continue; counts.TryGetValue(kv.Value.Activity, out var c); counts[kv.Value.Activity] = c + 1; }
            var sb = new System.Text.StringBuilder();
            sb.Append("agents=").Append(agents).Append(" coin=").Append(coin.ToString("F2"))
              .Append(" pos=").Append(posSum.ToString("F1")).Append(" hunger=").Append(hunger.ToString("F3"));
            var keys = new List<ActivityKind>(counts.Keys); keys.Sort();
            foreach (var k in keys) sb.Append(' ').Append(k).Append(':').Append(counts[k]);
            return sb.ToString();
        }
    }
}
