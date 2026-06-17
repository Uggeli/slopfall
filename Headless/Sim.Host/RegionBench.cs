using System;
using System.Diagnostics;
using DaggerfallWorkshop.Sim;

namespace DaggerfallWorkshop.Sim.Host
{
    /// Throughput benchmark for the WHOLE-region sim. Boots every settlement of a
    /// region into one context — no LOD, no interest management, the full system stack
    /// over every agent — and measures how many fixed 10 Hz ticks per wall-second it
    /// sustains. The question: can the sim run a real mainland region (hundreds of
    /// settlements, tens of thousands of agents) flat at real time (>=10 tick/s), or
    /// does scale force a coarse layer the way rendering does?
    ///
    /// 10 tick/s == real time (1 tick = 0.1 s sim). Above that, the sim outruns the
    /// wall clock; below, it can't keep up without thinning the agent set.
    public static class RegionBench
    {
        public static int Run(string regionName, int ticks, int startHour = -1)
        {
            var swLoad = Stopwatch.StartNew();
            SimBootResult boot;
            try { boot = SimBoot.CreateRegion(DataProbe.Arena2Path, regionName, 1f); }
            catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 1; }
            swLoad.Stop();

            var ctx = boot.Ctx;
            var r = boot.Region;
            int agents = 0;
            foreach (var _ in ctx.Identity.All) agents++;

            Console.WriteLine(regionName + " — loaded in " + (swLoad.ElapsedMilliseconds / 1000.0).ToString("F1") + " s");
            Console.WriteLine("  settlements " + r.Settlements + ", civilians " + r.Civilians
                + ", live entities " + agents + ", combined grid " + r.BlocksWide + "x" + r.BlocksHigh + " blocks");

            // Optionally jump the clock to a busy hour so the timed window measures the
            // daytime peak (everyone awake, moving, sensing crowds) rather than dawn idle.
            if (startHour >= 0)
            {
                ctx.Inputs.Enqueue(new SeedClockInput
                {
                    Year = 405, Month = 0, Day = 3, Hour = startHour, Minute = 0, Second = 0f, TimeScale = 1f,
                });
                Console.WriteLine("  clock seeded to " + startHour.ToString("00") + ":00");
            }
            Console.WriteLine();

            // Warm up: JIT the system stack, apply the clock seed, and let agents pick
            // their daytime intents so the timed window measures steady state.
            for (int i = 0; i < 20; i++) boot.Loop.Step();

            long memBefore = GC.GetTotalMemory(true);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < ticks; i++) boot.Loop.Step();
            sw.Stop();
            long memAfter = GC.GetTotalMemory(false);

            double secs = sw.Elapsed.TotalSeconds;
            double tps = ticks / secs;
            double msPerTick = 1000.0 * secs / ticks;
            Console.WriteLine("  ran " + ticks + " ticks in " + secs.ToString("F2") + " s");
            Console.WriteLine("  throughput : " + tps.ToString("F1") + " ticks/s   ("
                + (tps / 10.0).ToString("F2") + "x real time)");
            Console.WriteLine("  per tick   : " + msPerTick.ToString("F2") + " ms"
                + (agents > 0 ? "   (" + (msPerTick / agents * 1000.0).ToString("F2") + " us/agent)" : ""));
            Console.WriteLine("  heap after : " + (memAfter / 1048576) + " MB (steady "
                + (memBefore / 1048576) + " MB)");
            Console.WriteLine();
            Console.WriteLine(tps >= 10.0
                ? "  VERDICT: clears 10 tick/s — the flat region keeps real time."
                : "  VERDICT: below 10 tick/s — flat region can't hold real time at this scale.");
            return 0;
        }
    }
}
