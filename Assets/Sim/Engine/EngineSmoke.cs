using System;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// Smoke test: build the full SimWorld (every registry + every system), seed the
    /// clock, and tick the engine. Validates the whole system stack runs end-to-end
    /// without throwing and that time advances through the bus → registry → system
    /// pipeline. Run via `Sim.Host --enginesmoke [ticks]`.
    public static class EngineSmoke
    {
        public static int Run(int ticks)
        {
            var world = new SimWorld(seed: 12345);
            world.Events.Publish(new SeedClockInput
            {
                Year = 405, Month = 0, Day = 3, Hour = 5, Minute = 30, Second = 0f, TimeScale = 600f,
            });

            for (int i = 0; i < ticks; i++)
            {
                world.Step();   // serial step (the correctness path)
                if (i == 1 || i == ticks - 1 || (i % System.Math.Max(1, ticks / 5)) == 0)
                {
                    var c = world.WorldClock.Current;
                    Console.WriteLine($"tick {world.Tick,5}: {c.Year:0000}-{c.Month + 1:00}-{c.Day + 1:00} " +
                                      $"{c.Hour:00}:{c.Minute:00}  weather={world.Weather.Current.Kind}  night={world.Lighting.Current.IsNight}");
                }
            }
            Console.WriteLine($"\nengine smoke ok — {ticks} ticks, no exceptions");
            return 0;
        }
    }
}
