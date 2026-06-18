using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Sole writer of ItemRegistry — the discrete things that exist in the world
    /// (docs/items_and_inventory.md). Registered right after EconomySystem so the
    /// material consequences of what agents are Doing apply together, before the
    /// pre-decision Needs update.
    ///
    /// Individuation: a fungible unit becomes a discrete item the moment a behaviour
    /// pulls it out of a stack. Theft is the first puller — EconomySystem draws the
    /// provisions off the shelf (it owns Stock) and emits ProvisionsTakenEvent; here
    /// we turn whole units into discrete, persistent, CARRIED loaves owned by the
    /// keeper, and charge the taker's conscience when it's theft. Take/Use/Drop as
    /// chooseable blocks (with affordances + scoring) land next; this is the effect
    /// side they'll drive.
    public sealed class ItemSystem : ISystem
    {
        SimulationContext _ctx;
        readonly List<ProvisionsTakenEvent> _takes = new List<ProvisionsTakenEvent>();
        readonly Dictionary<EntityId, double> _takeProgress = new Dictionary<EntityId, double>();
        // Agents that have applied their current item-block's one-shot effect (eat
        // consumes, store relocates) — pruned when they leave the block so a fresh
        // act re-applies.
        readonly HashSet<EntityId> _applied = new HashSet<EntityId>();

        const double TheftGuilt = 0.15;     // committing theft deepens the Steal qualm (charged on commission)

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
            ctx.Events.Subscribe<ProvisionsTakenEvent>(e => _takes.Add(e));
        }

        public void ProcessEvents()
        {
            for (int i = 0; i < _takes.Count; i++)
            {
                var e = _takes[i];
                _takeProgress.TryGetValue(e.Taker, out var prog);
                prog += e.Units;
                while (prog >= 1.0)         // each whole unit individuates into one loaf
                {
                    prog -= 1.0;
                    Individuate(e.Taker, e.Owner);
                }
                _takeProgress[e.Taker] = prog;
            }
            _takes.Clear();
        }

        /// A unit of provisions becomes a discrete loaf the moment it's individuated:
        /// born CARRIED by the taker, still OWNED by the keeper. held ≠ owned is the
        /// stolen state — recoverable by returning location to the owner, and the
        /// taker's conscience is charged when it's theft.
        void Individuate(EntityId taker, EntityId owner)
        {
            var loaf = GoodsCatalog.NewItem(Good.Provisions);   // the lifted good, as a discrete item
            loaf.Owner = owner;
            loaf.OriginOwner = owner;
            loaf.OriginTick = _ctx.Time.Tick;
            bool theft = ItemOps.Take(loaf, taker);      // into the taker's hands (CarriedBy)
            _ctx.Items.Set(_ctx.Items.Allocate(), loaf);
            if (theft) ItemOps.ChargeCriminalGuilt(_ctx, taker, ActivityKind.Steal, TheftGuilt);
        }

        /// Apply each item-block's physical effect ONCE per act: eating consumes the
        /// loaf (the hunger relief is NeedsSystem's, via the spec Δ); storing drops it
        /// into the home it arrived at. Phase==Doing means the agent is at the spot
        /// (ExecutionSystem flips on arrival), so no position check is needed. Guarded
        /// by _applied, pruned for agents who've left the block.
        public void Update(long tick)
        {
            _applied.RemoveWhere(id => !IsItemBlockDoing(id));
            foreach (var kv in _ctx.Behavior.All)
            {
                var b = kv.Value;
                if (b == null || b.Phase != ActivityPhase.Doing) continue;
                if (b.Activity != ActivityKind.UseItem && b.Activity != ActivityKind.StoreItem) continue;
                if (_applied.Contains(kv.Key)) continue;
                if (b.TargetItem.IsNone || !_ctx.Items.TryGet(b.TargetItem, out var item) || item == null) continue;

                if (b.Activity == ActivityKind.UseItem)
                    _ctx.Items.Remove(b.TargetItem);                              // eaten — consumed
                else
                    ItemOps.Drop(item, b.TargetBuilding, b.TargetX, b.TargetZ);   // stored in the home

                _applied.Add(kv.Key);
            }
        }

        bool IsItemBlockDoing(EntityId id)
            => _ctx.Behavior.TryGet(id, out var b) && b != null && b.Phase == ActivityPhase.Doing
               && (b.Activity == ActivityKind.UseItem || b.Activity == ActivityKind.StoreItem);
    }
}
