using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of the old DaggerfallWorkshop.Sim.SenseSystem. Raw senses — the
    // first stage of Atoms' rung 2. Every few ticks each agent out on the street perceives
    // the other agents within sight range that a wall doesn't hide. No interpretation, no
    // relationships, no decisions: just "who is here, now."
    //
    // Occlusion is grid line-of-sight (walk the town tiles, blocked by walls), not a
    // physics raycast — so it runs headless and deterministic. Place knowledge is NOT
    // sensed: town agents already know their whole town (PlaceMemory). Kind-agnostic: it
    // senses agents, not "civilians" — creatures sense and are sensed through the same path.
    //
    // Where state went: the old private `_buckets` spatial-hash dictionary was per-tick
    // scratch (cleared and rebuilt every sense-tick) — it has no business crossing ticks,
    // so it becomes a LOCAL inside Update. The old `SenseEveryTicks` cadence (an internal
    // counter against the tick) is now `tick % 5 != 0`. Reads Position+Behavior+Creatures+
    // TownGrid read-only; Publishes SensedSetIntent (a whole rebuilt list per senser).
    public sealed class SenseSystem : SimSystem
    {
        const int SenseEveryTicks = 5;
        const float SightRadius = 12f;
        const float CellSize = 16f;     // spatial-hash bucket; coarser than the LOS grid

        readonly WorldClockRegistry _clock;
        readonly BehaviorRegistry _behavior;
        readonly PositionRegistry _position;
        readonly CreatureRegistry _creatures;
        readonly TownGridRegistry _townGrid;

        public SenseSystem(EventBus events, WorldClockRegistry clock, BehaviorRegistry behavior,
            PositionRegistry position, CreatureRegistry creatures, TownGridRegistry townGrid, int seed) : base(events)
        {
            _clock = clock;
            _behavior = behavior;
            _position = position;
            _creatures = creatures;
            _townGrid = townGrid;
        }

        public override void Update(long tick)
        {
            var clock = _clock.Current;
            if (clock.Year == 0) return;
            if (tick % SenseEveryTicks != 0) return;

            // Spatial hash over agents who are out and about (local scratch — was _buckets).
            // People busy indoors (Doing at a building) don't street-watch.
            var buckets = new Dictionary<long, List<EntityId>>();
            foreach (var kv in _behavior.All)
            {
                if (!IsOutAndAbout(kv.Value)) continue;
                if (!_position.TryGet(kv.Key, out var pos)) continue;
                long cell = CellKey(pos.X, pos.Z);
                if (!buckets.TryGetValue(cell, out var list))
                {
                    list = new List<EntityId>();
                    buckets[cell] = list;
                }
                list.Add(kv.Key);
            }

            // Creatures (V2) are perceptible — drop them into the same hash so the
            // out-and-about agents sense them — but they don't street-watch, so we
            // don't build a view FOR them (CreatureSystem does their proximity).
            foreach (var kv in _creatures.All)
            {
                if (!_position.TryGet(kv.Key, out var pos)) continue;
                long cell = CellKey(pos.X, pos.Z);
                if (!buckets.TryGetValue(cell, out var list))
                {
                    list = new List<EntityId>();
                    buckets[cell] = list;
                }
                list.Add(kv.Key);
            }

            var grid = _townGrid.Current;
            foreach (var kv in buckets)
            {
                var list = kv.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    if (_creatures.Contains(list[i])) continue;   // sensed, but not a senser
                    Sense(list[i], grid, buckets);
                }
            }
        }

        void Sense(EntityId self, TownGridData grid, Dictionary<long, List<EntityId>> buckets)
        {
            if (!_position.TryGet(self, out var pos)) return;
            var sensed = new List<EntityId>();

            int cx = (int)(pos.X / CellSize), cy = (int)(pos.Z / CellSize);
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!buckets.TryGetValue(Key(cx + dx, cy + dy), out var list)) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var other = list[i];
                        if (other == self) continue;
                        if (!_position.TryGet(other, out var op)) continue;

                        float ddx = op.X - pos.X, ddz = op.Z - pos.Z;
                        if (ddx * ddx + ddz * ddz > SightRadius * SightRadius) continue;
                        // No raycasts headless: a wall between us hides them.
                        if (grid != null && !grid.LineClear(pos.X, pos.Z, op.X, op.Z)) continue;

                        sensed.Add(other);
                    }
                }
            }

            Events.Publish(new SensedSetIntent { Id = self, Sensed = sensed });
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
