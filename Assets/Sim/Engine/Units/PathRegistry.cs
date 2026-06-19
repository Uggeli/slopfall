using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // Homes the per-entity active path that the old MovementSystem held in its private
    // `_plans` dictionary — cross-tick state with no registry of its own. Under CQRS a
    // system holds no tick state, so the journey cache becomes a Registry: MovementSystem
    // reads the active Plan, advances along it, and Publishes PathSetIntent (cache the
    // plan for next tick, with its advanced Next cursor) or PathClearIntent (journey done
    // / arrived). PathRegistry is the sole applier; a despawned entity drops its plan.
    //
    // Plan is the same shape the old inner MovementSystem.Plan struct had (TargetX/Z, the
    // waypoint Points list, and the Next cursor) — it is carried whole in the intent.

    /// A cached town path: the target it was planned toward, the waypoint chain, and how
    /// far along it the entity has walked.
    public sealed class Plan
    {
        public float TargetX, TargetZ;
        public List<PathPoint> Points = new List<PathPoint>();
        public int Next;
    }

    /// Intent: "cache this entity's active path." Whole-value set (the Plan carries the
    /// advanced Next cursor). MovementSystem emits it after walking the plan this tick.
    public struct PathSetIntent : IEvent { public EntityId Id; public Plan Plan; }

    /// Intent: "this entity's journey is over — drop its cached path." Emitted on arrival
    /// (so the next journey replans from scratch), mirroring the old `_plans.Remove(id)`.
    public struct PathClearIntent : IEvent { public EntityId Id; }

    /// Per-entity active path cache. Sole writer; clears on PathClearIntent and on
    /// DespawnedEvent.
    public sealed class PathRegistry : Registry
    {
        readonly Dictionary<EntityId, Plan> _d = new Dictionary<EntityId, Plan>();

        public PathRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            // Sets first, then clears/despawns win for the same id this tick (a clear
            // emitted alongside a set means the journey ended — drop it).
            foreach (var s in Events.GetEvents<PathSetIntent>())
                _d[s.Id] = s.Plan;
            foreach (var c in Events.GetEvents<PathClearIntent>())
                _d.Remove(c.Id);
            foreach (var g in Events.GetEvents<DespawnedEvent>())
                _d.Remove(g.Entity);
        }

        public bool TryGet(EntityId id, out Plan plan) => _d.TryGetValue(id, out plan);

        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, Plan>> All => _d;
    }
}
