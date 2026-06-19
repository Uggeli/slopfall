using System;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// Runs a loaded SimWorld on the parallel engine for N game-days and reports what
    /// the town is doing — the engine-era replacement for the old Soak. Fixed step:
    /// 864000 ticks/game-day (0.1s tick).
    public static class EngineSoak
    {
        public const long TicksPerDay = 864000;

        public static int Run(SimWorld w, int days)
        {
            long total = days * TicksPerDay;
            int startPop = w.Identity.Count;
            Console.WriteLine($"soaking {days} game-day(s) = {total} ticks, start pop {startPop}");
            Report(w, 0);

            long every = TicksPerDay / 4;   // ~4 samples/day
            for (long i = 1; i <= total; i++)
            {
                w.Step();
                if (i % every == 0) Report(w, i);
            }

            int endPop = w.Identity.Count;
            Console.WriteLine($"\nSOAK DONE — pop {startPop} -> {endPop}, {total} ticks on the parallel engine");
            return 0;
        }

        static void Report(SimWorld w, long tick)
        {
            var c = w.WorldClock.Current;
            int agents = w.Identity.Count;
            double hunger = 0; int n = 0;
            foreach (var kv in w.Needs.All) { hunger += kv.Value.V[NeedAxis.Hunger]; n++; }
            double coin = 0; foreach (var kv in w.Coin.All) coin += kv.Value;

            var counts = new Dictionary<ActivityKind, int>();
            foreach (var kv in w.Behavior.All)
            {
                if (kv.Value == null) continue;
                counts.TryGetValue(kv.Value.Activity, out var k); counts[kv.Value.Activity] = k + 1;
            }
            var keys = new List<ActivityKind>(counts.Keys); keys.Sort((a, b) => counts[b].CompareTo(counts[a]));
            var sb = new System.Text.StringBuilder();
            foreach (var k in keys) sb.Append(' ').Append(k).Append('=').Append(counts[k]);

            Console.WriteLine($"  t={tick,8} {c.Hour:00}:{c.Minute:00} pop={agents} meanHunger={(n > 0 ? hunger / n : 0):F2} coin={coin:F0} |{sb}");

            int posting = 0, attacking = 0, hungry = 0;
            foreach (var kv in w.Behavior.All)
            {
                if (kv.Value == null) continue;
                var a = kv.Value.Activity;
                if (a == ActivityKind.Patrol || a == ActivityKind.StandWatch) posting++;
                else if (a == ActivityKind.Attack) attacking++;
            }
            foreach (var kv in w.Creatures.All)
                if (kv.Value.HungerLevel >= 0.5f) hungry++;

            var m = w.Metrics;
            string kills = m == null ? "" :
                $" kills[gate={m.KillsAtGate} inside={m.KillsInside} day={m.KillsByDay} night={m.KillsByNight}]";
            Console.WriteLine($"           guards[posting={posting} attacking={attacking}] hungryMonsters={hungry}{kills}");
        }
    }
}
