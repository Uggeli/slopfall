using System.Collections.Generic;
using DaggerfallWorkshop.Sim;

namespace Sim.Tests
{
    /// Builds the same system stack SimDriver registers, but stepped manually
    /// (no SimThread) so tests are deterministic and instant.
    public sealed class SimHarness
    {
        public SimulationContext Ctx { get; }
        public TickLoop Loop { get; }
        public EventLog Log { get; }

        public SimHarness(int seed = 12345, double tickIntervalSeconds = 0.1)
        {
            var events = new EventBus();
            var time = new SimulationTime(tickIntervalSeconds);
            var random = new SimRandom(seed);
            var inputs = new InputBus();
            Ctx = new SimulationContext(events, time, random, inputs);

            Loop = new TickLoop(Ctx);
            Loop.Register(new TimeSystem());
            Loop.Register(new AgingSystem());
            Loop.Register(new WeatherSystem());
            Loop.Register(new SunlightSystem());
            Loop.Register(new HealthSystem());
            Loop.Register(new EffectLifecycleSystem());
            Loop.Register(new EffectTickSystem());
            Loop.Register(new EffectAggregateSystem());
            Loop.Register(new StatusFlagDeriveSystem());
            Loop.Register(new SkillAdvancementSystem());
            Loop.Register(new ProgressionSystem());
            // No WeatherDriverSystem here: tests own the weather they set.
            Loop.Register(new HolidaySystem());
            Loop.Register(new EconomySystem());
            Loop.Register(new NeedsSystem());
            Loop.Register(new OddSystem());
            Loop.Register(new ExecutionSystem());
            Loop.Register(new MovementSystem());
            Loop.Register(new SenseSystem());
            Loop.Register(new PerceptionSystem());
            Loop.Register(new SocialSystem());
            Loop.Register(new RequestSystem());
            Loop.Register(new LifecycleSystem());
            Loop.Register(new RepopulationSystem());
            Log = new EventLog();
            Loop.Register(Log);
        }

        public void Step(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
                Loop.Step();
        }

        /// Collects every event of T fired from now on. Subscribe before stepping.
        public List<T> Collect<T>() where T : ISimEvent
        {
            var sink = new List<T>();
            Ctx.Events.Subscribe<T>(sink.Add);
            return sink;
        }

        public EntityId SpawnEntity(string name = "TestEntity", int health = 50, int maxHealth = 50)
        {
            var id = Ctx.Identity.Allocate();
            Ctx.Identity.Set(id, new IdentityData { Name = name, Kind = EntityKind.EnemyMonster, Level = 1 });
            Ctx.Vitals.Set(id, new VitalsData
            {
                CurrentHealth = health, MaxHealth = maxHealth,
                CurrentMagicka = 10, MaxMagicka = 10,
                CurrentFatigue = 100, MaxFatigue = 100,
                CurrentBreath = 10, MaxBreath = 10,
                IsDead = false,
            });
            Ctx.Stats.Set(id, new StatsData());
            return id;
        }

        public void SeedClock(int year = 405, int month = 0, int day = 3, int hour = 13, int minute = 30,
                              float timeScale = 12f)
        {
            Ctx.Inputs.Enqueue(new SeedClockInput
            {
                Year = year, Month = month, Day = day,
                Hour = hour, Minute = minute, Second = 0f,
                TimeScale = timeScale,
            });
        }
    }
}
