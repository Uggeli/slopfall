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

            // Agent-memory learning arc: categories minted + records held, plus the "blend lean"
            // (mean |valence| + mean confidence over all category nodes — how much learned feeling
            // the agents have accrued, i.e. how far the Interpret blend has shifted off the old store).
            long cats = 0, recs = 0, placeRecs = 0, dangerRecs = 0; int memAgents = 0, learned = 0, nodes = 0, reinforced = 0;
            double absVal = 0, conf = 0, maxAbsVal = 0, maxConf = 0, dangerSum = 0;
            foreach (var kv in w.AgentMemory.All)
            {
                var meanings = kv.Value.Meanings;
                int c2 = meanings.Count;
                cats += c2; recs += kv.Value.Stores.Things.Count; memAgents++;
                if (c2 > 0) learned++;

                // PLACES learning: how many place facts agents hold + how much remembered danger.
                var places = kv.Value.Stores.Places;
                placeRecs += places.Count;
                for (int pi = 0; pi < places.Count; pi++)
                    if (places[pi].DeltaBag.TryGet(Memory.PlaceAtoms.Danger, out var dv))
                    { dangerSum += dv.ToDouble(); dangerRecs++; }
                for (int ni = 0; ni < c2; ni++)
                {
                    var node = meanings[ni];
                    double av = System.Math.Abs(node.Valence.ToDouble()), cv = node.Confidence.ToDouble();
                    absVal += av; conf += cv; nodes++;
                    if (cv > 0) reinforced++;
                    if (av > maxAbsVal) maxAbsVal = av;
                    if (cv > maxConf) maxConf = cv;
                }
            }
            if (memAgents > 0)
            {
                Console.WriteLine($"           mem[cat/agent={(double)cats / memAgents:F2} rec/agent={(double)recs / memAgents:F1} learned%={100.0 * learned / memAgents:F0}]");
                if (nodes > 0)
                    Console.WriteLine($"           val[meanAbs={absVal / nodes:F3} conf={conf / nodes:F3} maxAbs={maxAbsVal:F2} maxConf={maxConf:F2} reinforcedNodes={reinforced}/{nodes}]");
                Console.WriteLine($"           places[rec/agent={(double)placeRecs / memAgents:F1} dangerRecs={dangerRecs} meanDanger={(dangerRecs > 0 ? dangerSum / dangerRecs : 0):F3}]");
            }
        }
    }
}
