using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// Identifies a serviced affordance: a building + the verb you queue for.
    public struct QueueAnchor : System.IEquatable<QueueAnchor>
    {
        public int Building;
        public ActivityKind Verb;
        public QueueAnchor(int building, ActivityKind verb) { Building = building; Verb = verb; }
        public bool Equals(QueueAnchor o) => Building == o.Building && Verb == o.Verb;
        public override bool Equals(object o) => o is QueueAnchor q && Equals(q);
        public override int GetHashCode() => (Building * 397) ^ (int)Verb;
    }

    /// Intent: "this agent has arrived and wants a turn at this affordance."
    public struct QueueJoinIntent : IEvent
    {
        public EntityId Agent;
        public QueueAnchor Anchor;
        public int Capacity;            // how many the affordance serves at once
        public float AnchorX, AnchorZ;  // the affordance's standing point (for slot geometry)
    }

    /// Intent: "this agent is leaving the line / counter (served, balked, or re-decided away)."
    public struct QueueLeaveIntent : IEvent { public EntityId Agent; }

    /// A live serviced-affordance instance. The ServiceQueue protocol: ordered
    /// Waiters, up to Capacity in Served at once.
    /// External assemblies (e.g. Sim.Web) see only the read-only views below;
    /// the registry mutates the internal lists in Update.
    public sealed class SharedActivityInstance
    {
        public QueueAnchor Anchor;
        public int Capacity;
        public float AnchorX, AnchorZ;
        // Internal so only the registry (same assembly) can mutate them.
        internal readonly List<EntityId> Waiters = new List<EntityId>();
        internal readonly List<EntityId> Served = new List<EntityId>();
        // Public read-only views for consumers outside this assembly.
        public IReadOnlyList<EntityId> WaitingAgents => Waiters;
        public IReadOnlyList<EntityId> ServedAgents => Served;
    }

    /// Holds live shared-activity instances keyed by anchor. Apply order each tick:
    /// leaves (free slots), then joins (append waiters), then despawn-removals (a dead
    /// agent vacates its slot exactly like a leave), then promote (fill free Served slots
    /// from the head of the line), then reap empties. CQRS: systems publish Join/Leave
    /// intents; reads are settled state.
    public sealed class SharedActivityRegistry : Registry
    {
        readonly Dictionary<QueueAnchor, SharedActivityInstance> _byAnchor =
            new Dictionary<QueueAnchor, SharedActivityInstance>();
        readonly Dictionary<EntityId, QueueAnchor> _ofAgent =
            new Dictionary<EntityId, QueueAnchor>();
        public SharedActivityRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            foreach (ref readonly var lv in Events.GetEvents<QueueLeaveIntent>())
                RemoveAgent(lv.Agent);

            foreach (ref readonly var jn in Events.GetEvents<QueueJoinIntent>())
                JoinAgent(jn);

            // Despawns (death): a SERVED agent never emits a QueueLeaveIntent, so without
            // this its counter slot would stay occupied forever and every waiter behind it
            // would deadlock Queued. Treat a despawn exactly like a leave, positioned AFTER
            // leaves+joins and BEFORE promote so the freed slot is filled this same tick.
            foreach (ref readonly var d in Events.GetEvents<DespawnedEvent>())
                RemoveAgent(d.Entity);

            // Promote: deterministic — fill free Served slots from the head.
            foreach (var inst in _byAnchor.Values)
                while (inst.Served.Count < inst.Capacity && inst.Waiters.Count > 0)
                {
                    var head = inst.Waiters[0];
                    inst.Waiters.RemoveAt(0);
                    inst.Served.Add(head);
                }

            // Reap empties.
            if (_pendingReap.Count > 0) _pendingReap.Clear();
            foreach (var kv in _byAnchor)
                if (kv.Value.Served.Count == 0 && kv.Value.Waiters.Count == 0)
                    _pendingReap.Add(kv.Key);
            for (int i = 0; i < _pendingReap.Count; i++) _byAnchor.Remove(_pendingReap[i]);
        }
        readonly List<QueueAnchor> _pendingReap = new List<QueueAnchor>();

        void JoinAgent(in QueueJoinIntent jn)
        {
            if (_ofAgent.ContainsKey(jn.Agent)) return;     // idempotent
            if (!_byAnchor.TryGetValue(jn.Anchor, out var inst))
            {
                inst = new SharedActivityInstance
                {
                    Anchor = jn.Anchor, Capacity = jn.Capacity < 1 ? 1 : jn.Capacity,
                    AnchorX = jn.AnchorX, AnchorZ = jn.AnchorZ,
                };
                _byAnchor[jn.Anchor] = inst;
            }
            inst.Waiters.Add(jn.Agent);
            _ofAgent[jn.Agent] = jn.Anchor;
        }

        void RemoveAgent(EntityId agent)
        {
            if (!_ofAgent.TryGetValue(agent, out var anchor)) return;
            _ofAgent.Remove(agent);
            if (!_byAnchor.TryGetValue(anchor, out var inst)) return;
            inst.Served.Remove(agent);
            inst.Waiters.Remove(agent);
        }

        public bool TryGet(QueueAnchor anchor, out SharedActivityInstance inst)
            => _byAnchor.TryGetValue(anchor, out inst);

        public bool AnchorOf(EntityId agent, out QueueAnchor anchor)
            => _ofAgent.TryGetValue(agent, out anchor);

        public bool IsServed(EntityId agent)
            => _ofAgent.TryGetValue(agent, out var a)
               && _byAnchor.TryGetValue(a, out var inst) && inst.Served.Contains(agent);

        public int PositionOf(EntityId agent)
            => _ofAgent.TryGetValue(agent, out var a) && _byAnchor.TryGetValue(a, out var inst)
               ? inst.Waiters.IndexOf(agent) : -1;

        public IReadOnlyList<EntityId> ServedSnapshot()
        {
            var all = new List<EntityId>();
            foreach (var inst in _byAnchor.Values) all.AddRange(inst.Served);
            return all.Count == 0 ? System.Array.Empty<EntityId>() : all;
        }
    }
}
