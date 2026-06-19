using System;
using DaggerfallWorkshop.Sim.Engine;

namespace DaggerfallWorkshop.Sim.Host.Render
{
    /// Reads the immutable read-phase state of a SimWorld into JSON-ready DTOs.
    /// MUST be called on the sim thread right after Step()/StepSerial() — that's the
    /// window where every registry holds coherent, writer-free tick-N data.
    public static class SnapshotBuilder
    {
        /// One-time map geometry. Build once after the world is seeded.
        public static WorldStaticDto BuildStatic(SimWorld w)
        {
            var grid = w.TownGrid.Current;
            var dto = new WorldStaticDto
            {
                RegionName = FirstSettlementRegion(w),
                GridWidth = grid.Width,
                GridHeight = grid.Height,
                CellSize = TownGridData.CellSize,
                BlocksWide = grid.BlocksWide,
                BlocksHigh = grid.BlocksHigh,
                CostBase64 = grid.Cost != null ? Convert.ToBase64String(grid.Cost) : "",
            };

            // Map each building index to its owning settlement (for client grouping/tint).
            var owner = new System.Collections.Generic.Dictionary<int, int>();
            var sts = w.Settlements.All;
            for (int s = 0; s < sts.Count; s++)
            {
                var st = sts[s];
                if (st.Buildings != null)
                    foreach (var bi in st.Buildings) owner[bi] = st.Id;
                dto.Settlements.Add(new SettlementDto
                {
                    Id = st.Id,
                    Name = st.Name,
                    Kind = st.Kind,
                    OriginX = st.OriginX,
                    OriginZ = st.OriginZ,
                    BlocksWide = st.BlocksWide,
                    BlocksHigh = st.BlocksHigh,
                    Residents = st.Residents != null ? st.Residents.Count : 0,
                });
            }

            foreach (var kv in w.Buildings.All)
            {
                var b = kv.Value;
                dto.Buildings.Add(new BuildingDto
                {
                    Id = kv.Key,
                    Kind = b.Kind,
                    X = b.X,
                    Z = b.Z,
                    Yaw = b.YRotation,
                    Settlement = owner.TryGetValue(kv.Key, out var sid) ? sid : -1,
                });
            }

            return dto;
        }

        /// Per-tick agents + global state. Cheap; build every published tick.
        public static SnapshotDto BuildDynamic(SimWorld w, long tick, bool paused)
        {
            var clock = w.WorldClock.Current;
            var weather = w.Weather.Current;
            var light = w.Lighting.Current;

            var dto = new SnapshotDto
            {
                Tick = tick,
                Year = clock.Year, Month = clock.Month, Day = clock.Day,
                Hour = clock.Hour, Minute = clock.Minute,
                IsNight = light.IsNight,
                SunIntensity = light.SunIntensity,
                Weather = weather.Kind,
                TimeScale = clock.TimeScale,
                Paused = paused,
            };

            foreach (var kv in w.Position.All)
            {
                var id = kv.Key;
                var p = kv.Value;
                if (p == null) continue;

                var a = new AgentDto { Id = id.Value, X = p.X, Z = p.Z, Yaw = p.Yaw };
                if (w.Identity.TryGet(id, out var ident)) a.Kind = ident.Kind;
                if (w.Behavior.TryGet(id, out var beh) && beh != null)
                {
                    a.Activity = beh.Activity;
                    a.Phase = beh.Phase;
                }
                dto.Agents.Add(a);
            }
            dto.Population = dto.Agents.Count;
            return dto;
        }

        static string FirstSettlementRegion(SimWorld w)
        {
            var sts = w.Settlements.All;
            return sts.Count > 0 ? sts[0].RegionName : "(region)";
        }
    }
}
