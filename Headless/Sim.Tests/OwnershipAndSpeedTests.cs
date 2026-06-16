using System;
using System.Collections.Generic;
using System.IO;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    public class TimeScaleControlTests
    {
        [Fact]
        public void SetTimeScale_ChangesClockRate()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            h.SeedClock(hour: 12, minute: 0, timeScale: 60f);   // 1 game-min per tick
            h.Step(5);
            Assert.Equal(5, h.Ctx.WorldClock.Current.Minute);

            h.Ctx.Inputs.Enqueue(new SetTimeScaleInput { TimeScale = 600f });  // 10 min per tick
            h.Step(3);
            Assert.Equal(600f, h.Ctx.WorldClock.Current.TimeScale);
            // First step after the change consumes the input and already runs
            // at the new scale: 5 + 3 * 10 = 35.
            Assert.Equal(35, h.Ctx.WorldClock.Current.Minute);
        }

        [Fact]
        public void TimeScaleZero_PausesTheWorld()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var id = h.SpawnEntity();
            h.Ctx.Needs.Set(id, new NeedsData());
            h.SeedClock(hour: 12, minute: 0, timeScale: 60f);
            h.Step(2);

            h.Ctx.Inputs.Enqueue(new SetTimeScaleInput { TimeScale = 0f });
            h.Step();
            int minuteAtPause = h.Ctx.WorldClock.Current.Minute;
            h.Ctx.Needs.TryGet(id, out var needsAtPause);
            double hungerAtPause = needsAtPause.V[NeedAxis.Hunger];

            h.Step(50);
            Assert.Equal(minuteAtPause, h.Ctx.WorldClock.Current.Minute);
            Assert.True(h.Ctx.Needs.TryGet(id, out var needsAfter));
            Assert.Equal(hungerAtPause, needsAfter.V[NeedAxis.Hunger], 10);
        }
    }

    public class OwnershipTests
    {
        static string Arena2 =>
            Environment.GetEnvironmentVariable("DAGGERFALL_ARENA2")
            ?? "/home/sakkivi/omat/daggerfall-gamedata/arena2";

        static bool Available => Directory.Exists(Arena2);

        static SimHarness LoadTown()
        {
            var h = new SimHarness(tickIntervalSeconds: 1.0);
            var maps = new MapsFile(Path.Combine(Arena2, "MAPS.BSA"), FileUsage.UseMemory, true);
            var blocks = new BlocksFile(Path.Combine(Arena2, "BLOCKS.BSA"), FileUsage.UseMemory, true);
            TownLoader.Load(h.Ctx, maps.GetLocation("Daggerfall", "Gothway Garden"), blocks);
            h.SeedClock(hour: 5, minute: 30, timeScale: 60f);
            h.Ctx.Weather.Set(new WeatherData { Kind = WeatherKind.Sunny });
            return h;
        }

        [Fact]
        public void InspectBuilding_ListsItsPeople()
        {
            if (!Available) return;
            var h = LoadTown();
            h.Step(10);

            // Find a house with residents via Residency, then inspect it. (Stage 5
            // promotes one laborer per settlement to farm keeper, so a house may have
            // 1 rather than 2 residents — assert against the actual occupancy.)
            int building = -1;
            foreach (var kv in h.Ctx.Residency.All)
            {
                if (kv.Value.Role == ResidentRole.Resident) { building = kv.Value.BuildingIndex; break; }
            }
            Assert.True(building >= 0);

            int expected = 0;
            foreach (var kv in h.Ctx.Residency.All)
                if (kv.Value.BuildingIndex == building) expected++;

            var detail = Inspector.InspectBuilding(h.Ctx, building);
            Assert.NotNull(detail);
            Assert.Equal(expected, detail.People.Count);    // lists exactly the building's people
            Assert.All(detail.People, p => Assert.Equal("Resident", p.Role));
            Assert.All(detail.People, p => Assert.False(string.IsNullOrEmpty(p.Name)));

            Assert.Null(Inspector.InspectBuilding(h.Ctx, 99999));
        }

        [Fact]
        public void Wanderer_AimedIntoAHouse_StopsOnTheStreet()
        {
            // A wander target with no TargetBuilding must not walk inside a
            // building even when the picked point lands in one — the path
            // ends at the nearest walkable cell (approachTarget = false).
            if (!Available) return;
            var h = LoadTown();
            var g = h.Ctx.TownGrid.Current;

            // A blocked (building) cell away from the map edge, and a walkable
            // start nearby.
            int bx = -1, by = -1;
            for (int y = 32; y < g.Height - 32 && bx < 0; y++)
                for (int x = 32; x < g.Width - 32 && bx < 0; x++)
                    if (!g.Walkable(x, y)) { bx = x; by = y; }
            Assert.True(bx >= 0);
            Assert.True(TownPathfinder.NearestWalkable(g, bx, by, out int sx, out int sy));

            // Plant a roster-less entity (no Residency → OddSystem won't
            // re-decide it) wandering INTO the building cell.
            var id = h.SpawnEntity("Stroller");
            h.Ctx.Position.Set(id, g.WorldX(sx) + 20f, 0f, g.WorldZ(sy) + 20f, 0f);
            h.Ctx.Behavior.Set(id, new BehaviorData
            {
                Activity = ActivityKind.Wander,
                Phase = ActivityPhase.Moving,
                TargetBuilding = -1,
                TargetX = g.WorldX(bx),
                TargetZ = g.WorldZ(by),
                RemainingGameMinutes = 30,
            });
            h.SeedClock(hour: 10, minute: 0, timeScale: 60f);

            for (int i = 0; i < 60; i++)
            {
                h.Step();
                if (h.Ctx.Behavior.TryGet(id, out var b) && b.Phase == ActivityPhase.Doing) break;
            }

            Assert.True(h.Ctx.Behavior.TryGet(id, out var done));
            Assert.Equal(ActivityPhase.Doing, done.Phase);
            Assert.True(h.Ctx.Position.TryGet(id, out var pos));
            Assert.True(g.Walkable(g.CellX(pos.X), g.CellY(pos.Z)),
                "stroller ended inside a building at " + pos.X.ToString("F1") + "," + pos.Z.ToString("F1"));
        }
    }
}
