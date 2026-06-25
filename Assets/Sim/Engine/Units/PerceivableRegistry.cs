using System.Collections.Generic;
using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>Add/replace one perceivable atom on an entity.</summary>
    public struct StampAtomIntent : IEvent { public EntityId Entity; public AtomTypeId Type; public Fixed Value; }

    /// <summary>Remove one perceivable atom from an entity (no-op if absent).</summary>
    public struct ClearAtomIntent : IEvent { public EntityId Entity; public AtomTypeId Type; }

    /// <summary>
    /// Sole writer of each entity's perceivable surface — its broadcast bag of atoms (identity +
    /// observable state). Owning systems stamp their own atoms via intents; perception reads Bag()
    /// directly. Clears apply before stamps within a tick so a re-stamp is deterministic.
    /// </summary>
    public sealed class PerceivableRegistry : Registry
    {
        readonly Dictionary<EntityId, Dictionary<int, Fixed>> _work = new Dictionary<EntityId, Dictionary<int, Fixed>>();
        readonly Dictionary<EntityId, AtomBag> _bags = new Dictionary<EntityId, AtomBag>();

        public PerceivableRegistry(EventBus events) : base(events) { }

        /// <summary>Load-time direct stamp (no intent), mirroring IdentityRegistry.Seed.</summary>
        public void Seed(EntityId id, AtomTypeId type, Fixed value)
        {
            Set(id, type, value);
            Rebuild(id);
        }

        public override void Update(long tick)
        {
            var dirty = new HashSet<EntityId>();

            var clears = Events.GetEvents<ClearAtomIntent>();   // clears first
            for (int i = 0; i < clears.Length; i++)
                if (_work.TryGetValue(clears[i].Entity, out var m) && m.Remove(clears[i].Type.Value))
                    dirty.Add(clears[i].Entity);

            var stamps = Events.GetEvents<StampAtomIntent>();
            for (int i = 0; i < stamps.Length; i++)
            {
                Set(stamps[i].Entity, stamps[i].Type, stamps[i].Value);
                dirty.Add(stamps[i].Entity);
            }

            var gone = Events.GetEvents<DespawnedEvent>();
            for (int i = 0; i < gone.Length; i++)
            {
                _work.Remove(gone[i].Entity);
                _bags.Remove(gone[i].Entity);
                dirty.Remove(gone[i].Entity);
            }

            foreach (var id in dirty) Rebuild(id);
        }

        void Set(EntityId id, AtomTypeId type, Fixed value)
        {
            if (!_work.TryGetValue(id, out var m)) { m = new Dictionary<int, Fixed>(); _work[id] = m; }
            m[type.Value] = value;
        }

        void Rebuild(EntityId id)
        {
            if (!_work.TryGetValue(id, out var m) || m.Count == 0) { _bags[id] = AtomBag.Empty; return; }
            var atoms = new List<Atom>(m.Count);
            foreach (var kv in m) atoms.Add(new Atom(new AtomTypeId(kv.Key), kv.Value));
            _bags[id] = AtomBag.Create(atoms);   // sorts + dedups by type
        }

        // --- read API ---
        public AtomBag Bag(EntityId id) => _bags.TryGetValue(id, out var b) ? b : AtomBag.Empty;
        public int Count => _bags.Count;

        /// <summary>The entity's identity atoms only (Kind/Role/Race) — its recognition signature.</summary>
        public AtomBag Signature(EntityId id)
        {
            AtomBag full = Bag(id);
            List<Atom> ids = null;
            for (int i = 0; i < full.Count; i++)
            {
                if (!AtomCatalog.For(full[i].Type).IsIdentity) continue;
                if (ids == null) ids = new List<Atom>(full.Count);
                ids.Add(full[i]);
            }
            return ids == null ? AtomBag.Empty : AtomBag.Create(ids);
        }
    }
}
