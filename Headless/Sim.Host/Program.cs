using System;
using DaggerfallWorkshop.Sim;

namespace DaggerfallWorkshop.Sim.Host
{
    /// Headless sim runner. Stands in for SimDriver outside Unity: builds the
    /// context, registers the same system stack, seeds a classic game start,
    /// spawns a few demo entities, and fast-forwards N ticks, printing the
    /// event log. This is the seed of the eventual dedicated server binary.
    ///
    /// Usage: dotnet run [--ticks N] [--timescale X] [--realtime]
    ///        dotnet run --probe [regionName [locationName]]
    ///        dotnet run --town <regionName> <locationName> [--ticks N] [--timescale X]
    ///        dotnet run --soak <regionName> <locationName> [--days N]
    ///        dotnet run --view <regionName> <locationName> [--timescale X] [--frames N]
    ///        dotnet run --serve <regionName> <locationName> [--port N] [--timescale X]
    ///        dotnet run --connect <host:port> [--frames N]
    public static class Program
    {
        public static int Main(string[] args)
        {
            int ticks = 600;            // 1 game-hour at default 10 Hz / scale 600
            float timeScale = 600f;     // 1 game-minute per tick
            bool realtime = false;
            int frames = 0;
            int port = 7777;
            string townRegion = null, townLocation = null;
            string soakRegion = null, soakLocation = null;
            string loadRegion = null;
            string soakRegionWhole = null;
            string viewRegionWhole = null;
            int days = 7;
            string viewRegion = null, viewLocation = null;
            string serveRegion = null, serveLocation = null;
            string connect = null;

            if (args.Length > 0 && args[0] == "--probe")
            {
                string region = args.Length > 1 ? args[1] : null;
                string location = args.Length > 2 ? args[2] : null;
                return DataProbe.Run(region, location);
            }

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--ticks": ticks = int.Parse(args[++i]); break;
                    case "--timescale": timeScale = float.Parse(args[++i]); break;
                    case "--realtime": realtime = true; break;
                    case "--town": townRegion = args[++i]; townLocation = args[++i]; break;
                    case "--soak": soakRegion = args[++i]; soakLocation = args[++i]; break;
                    case "--loadregion": loadRegion = args[++i]; break;
                    case "--soakregion": soakRegionWhole = args[++i]; break;
                    case "--viewregion": viewRegionWhole = args[++i]; break;
                    case "--days": days = int.Parse(args[++i]); break;
                    case "--view": viewRegion = args[++i]; viewLocation = args[++i]; break;
                    case "--serve": serveRegion = args[++i]; serveLocation = args[++i]; break;
                    case "--connect": connect = args[++i]; break;
                    case "--port": port = int.Parse(args[++i]); break;
                    case "--frames": frames = int.Parse(args[++i]); break;
                    default:
                        Console.Error.WriteLine("unknown arg: " + args[i]);
                        return 2;
                }
            }

            if (serveRegion != null)
                return SimServer.Run(serveRegion, serveLocation, timeScale, port);
            if (connect != null)
            {
                var parts = connect.Split(':');
                int connectPort = parts.Length > 1 ? int.Parse(parts[1]) : port;
                return TownViewer.RunRemote(parts[0], connectPort, frames);
            }
            if (viewRegion != null)
                return TownViewer.RunLocal(viewRegion, viewLocation, timeScale, frames);
            if (loadRegion != null)
                return RegionProbe.Run(loadRegion);
            if (soakRegionWhole != null)
                return Soak.RunRegion(soakRegionWhole, days);
            if (viewRegionWhole != null)
                return TownViewer.RunRegion(viewRegionWhole, timeScale, frames);
            if (soakRegion != null)
                return Soak.Run(soakRegion, soakLocation, days);
            if (townRegion != null)
                return TownDemo.Run(townRegion, townLocation, ticks, timeScale);

            var events = new EventBus();
            var time = new SimulationTime(0.1);     // 10 Hz
            var random = new SimRandom(12345);
            var inputs = new InputBus();
            var ctx = new SimulationContext(events, time, random, inputs);

            var loop = new TickLoop(ctx);
            loop.Register(new TimeSystem());
            loop.Register(new WeatherSystem());
            loop.Register(new SunlightSystem());
            loop.Register(new HealthSystem());
            loop.Register(new EffectLifecycleSystem());
            loop.Register(new EffectTickSystem());
            loop.Register(new EffectAggregateSystem());
            loop.Register(new StatusFlagDeriveSystem());
            loop.Register(new SkillAdvancementSystem());
            loop.Register(new ProgressionSystem());
            var log = new EventLog();
            loop.Register(log);

            // Demo glue: route poison DoT ticks into damage. In the real engine
            // this wiring belongs to a Phase 4 effect-handler system, not here.
            events.Subscribe<EffectTickedEvent>(e =>
            {
                if (e.Key.StartsWith("Poison"))
                    events.Emit(new DamageEvent { Target = e.Target, Source = e.Source, Amount = e.Magnitude, Type = DamageType.Poison });
            });

            // Classic game start: 13:30, 4th of Morning Star, 3E405.
            inputs.Enqueue(new SeedClockInput
            {
                Year = 405, Month = 0, Day = 3, Hour = 13, Minute = 30, Second = 0f,
                TimeScale = timeScale,
            });
            ctx.Weather.Set(new WeatherData { Kind = WeatherKind.Sunny });

            // Demo population.
            var hero = Spawn(ctx, "Hero", EntityKind.Player, 60);
            ctx.Identity.SetPlayer(hero);
            var rat = Spawn(ctx, "Giant Rat", EntityKind.EnemyMonster, 12);
            var bear = Spawn(ctx, "Grizzly Bear", EntityKind.EnemyMonster, 40);

            // Demo scenario: the rat poisons the hero, the hero kills the rat,
            // then trains stealth hard enough to level.
            inputs.Enqueue(new ApplyEffectEvent { Target = hero, Source = rat, Key = "Poison-Drug", Magnitude = 1, DurationTicks = 10, AppliesPerTick = true });
            inputs.Enqueue(new DamageEvent { Target = rat, Source = hero, Amount = 15, Type = DamageType.Physical });
            inputs.Enqueue(new SkillUsedEvent { Entity = hero, Skill = "Stealth", Magnitude = 4200 });

            Console.WriteLine("sim: " + ticks + " ticks @ 10 Hz, timescale " + timeScale + (realtime ? ", realtime" : ", fast-forward"));
            Console.WriteLine();

            if (realtime)
            {
                var publisher = new SnapshotPublisher();
                var thread = new SimThread(loop, ctx, publisher);
                thread.Start();
                while (ctx.Time.Tick < ticks && thread.IsRunning)
                    System.Threading.Thread.Sleep(100);
                thread.Stop();
                if (thread.LastException != null)
                {
                    Console.Error.WriteLine("sim thread crashed: " + thread.LastException);
                    return 1;
                }
            }
            else
            {
                for (int i = 0; i < ticks; i++)
                    loop.Step();
            }

            var clock = ctx.WorldClock.Current;
            var light = ctx.Lighting.Current;
            Console.WriteLine("clock: " + clock.Hour.ToString("00") + ":" + clock.Minute.ToString("00")
                + " day " + clock.Day + " month " + clock.Month + " year 3E" + clock.Year);
            Console.WriteLine("light: night=" + light.IsNight + " sun=" + light.SunIntensity.ToString("F2"));
            Console.WriteLine();

            Console.WriteLine("entities:");
            foreach (var kv in ctx.Identity.All)
            {
                ctx.Vitals.TryGet(kv.Key, out var v);
                ctx.Progression.TryGet(kv.Key, out var prog);
                Console.WriteLine("  #" + kv.Key.Value + " " + kv.Value.Name
                    + "  hp=" + (v == null ? "?" : v.CurrentHealth + "/" + v.MaxHealth)
                    + (v != null && v.IsDead ? " DEAD" : "")
                    + (prog != null ? "  L" + prog.Level : ""));
            }
            Console.WriteLine();

            Console.WriteLine("event log (newest first):");
            foreach (var entry in log.Snapshot())
                Console.WriteLine("  [t" + entry.Tick.ToString("0000") + "] " + entry.Summary);

            return 0;
        }

        static EntityId Spawn(SimulationContext ctx, string name, EntityKind kind, int hp)
        {
            var id = ctx.Identity.Allocate();
            ctx.Identity.Set(id, new IdentityData { Name = name, Kind = kind, Level = 1 });
            ctx.Vitals.Set(id, new VitalsData
            {
                CurrentHealth = hp, MaxHealth = hp,
                CurrentMagicka = 10, MaxMagicka = 10,
                CurrentFatigue = 100, MaxFatigue = 100,
                CurrentBreath = 10, MaxBreath = 10,
            });
            ctx.Stats.Set(id, new StatsData());
            ctx.Position.Set(id, 0f, 0f, 0f, 0f);
            return id;
        }
    }
}
