using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Raw senses — the first stage of Atoms' rung 2. Every few ticks each agent
    /// out on the street perceives the other agents within sight range that a
    /// wall doesn't hide. No interpretation, no relationships, no decisions: just
    /// "who is here, now." PerceptionSystem refines this into percepts worth
    /// acting on.
    ///
    /// Occlusion is grid line-of-sight (walk the town tiles, blocked by walls),
    /// not a physics raycast — so it runs headless and deterministic. Place
    /// knowledge is NOT sensed: town agents already know their whole town
    /// (PlaceMemory); sensing is only about what's present right now.
    ///
    /// Kind-agnostic: it senses agents, not "civilians" — when creatures enter
    /// the sim they sense and are sensed through the same path. Sole writer of
    /// SensedRegistry.
    public sealed class SenseSystem : ISystem
    {
        const int SenseEveryTicks = 5;
        const float SightRadius = 12f;
        const float CellSize = 16f;     // spatial-hash bucket; coarser than the LOS grid

        SimulationContext _ctx;
        readonly Dictionary<long, List<EntityId>> _buckets = new Dictionary<long, List<EntityId>>();

        public void Init(SimulationContext ctx) { _ctx = ctx; }
        public void ProcessEvents() { }

        public void Update(long tick)
        {
            var clock = _ctx.WorldClock.Current;
            if (clock.Year == 0) return;
            if (tick % SenseEveryTicks != 0) return;

            // Spatial hash over agents who are out and about. People busy indoors
            // (Doing at a building) don't street-watch.
            foreach (var list in _buckets.Values) list.Clear();
            foreach (var kv in _ctx.Behavior.All)
            {
                if (!IsOutAndAbout(kv.Value)) continue;
                if (!_ctx.Position.TryGet(kv.Key, out var pos)) continue;
                long cell = CellKey(pos.X, pos.Z);
                if (!_buckets.TryGetValue(cell, out var list))
                {
                    list = new List<EntityId>();
                    _buckets[cell] = list;
                }
                list.Add(kv.Key);
            }

            var grid = _ctx.TownGrid.Current;
            foreach (var kv in _buckets)
            {
                var list = kv.Value;
                for (int i = 0; i < list.Count; i++)
                    Sense(list[i], grid);
            }
        }

        void Sense(EntityId self, TownGridData grid)
        {
            if (!_ctx.Position.TryGet(self, out var pos)) return;
            var sensed = new List<EntityId>();

            int cx = (int)(pos.X / CellSize), cy = (int)(pos.Z / CellSize);
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!_buckets.TryGetValue(Key(cx + dx, cy + dy), out var list)) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var other = list[i];
                        if (other == self) continue;
                        if (!_ctx.Position.TryGet(other, out var op)) continue;

                        float ddx = op.X - pos.X, ddz = op.Z - pos.Z;
                        if (ddx * ddx + ddz * ddz > SightRadius * SightRadius) continue;
                        // No raycasts headless: a wall between us hides them.
                        if (grid != null && !grid.LineClear(pos.X, pos.Z, op.X, op.Z)) continue;

                        sensed.Add(other);
                    }
                }
            }

            _ctx.Sensed.Set(self, sensed);
        }

        static bool IsOutAndAbout(BehaviorData b)
        {
            if (b.Phase == ActivityPhase.Moving) return true;
            return b.Activity == ActivityKind.Wander
                || b.Activity == ActivityKind.Visit
                || b.Activity == ActivityKind.Idle
                || b.Activity == ActivityKind.Beg;        // a beggar sits in public, watching for passers-by (L4)
        }

        static long CellKey(float x, float z) => Key((int)(x / CellSize), (int)(z / CellSize));
        static long Key(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;
    }
}
