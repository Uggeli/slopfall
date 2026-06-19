using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // Holds the cross-tick cooldown state the old DaggerfallWorkshop.Sim.RequestSystem
    // carried in fields: the running game-minute clock (_gameMinutes), the per-beggar
    // ask cadence (_nextAskAt), and the per-(beggar, mark) re-ask cooldown
    // (_pairCooldown). RequestSystem is stateless in the CQRS model, so this small
    // registry owns that state and is its sole writer.
    //
    // Two intents, applied here:
    //   - RequestCooldownTickIntent advances the game-minute clock by the tick's delta
    //     (RequestSystem emits it each active tick from the WorldClock delta).
    //   - RequestCooldownSetIntent records "beggar tried at GameMinutes; don't re-ask
    //     this pair until Until" — written when a beggar picks a mark.
    // The map is pruned here too, against the same advancing clock, replacing the old
    // PruneCooldowns().

    /// Intent: advance the cooldown clock by Delta game-minutes (one per active tick).
    public struct RequestCooldownTickIntent : IEvent { public double Delta; }

    /// Intent: "beggar asked at NextAskAtBase, set this pair's re-ask floor to Until."
    public struct RequestCooldownSetIntent : IEvent
    {
        public EntityId Beggar;
        public EntityId Mark;
        public double NextAskAt;   // absolute game-minute the beggar may next ask
        public double Until;       // absolute game-minute the (beggar,mark) pair reopens
    }

    public sealed class RequestCooldownRegistry : Registry
    {
        const int CooldownPruneCap = 4096;              // bound the per-pair cooldown map

        double _gameMinutes;
        readonly Dictionary<EntityId, double> _nextAskAt = new Dictionary<EntityId, double>();  // per-beggar cadence
        readonly Dictionary<long, double> _pairCooldown = new Dictionary<long, double>();        // per (beggar, mark)
        readonly List<long> _stale = new List<long>();

        public RequestCooldownRegistry(EventBus events) : base(events) { }

        // --- read view (read phase) ---
        public double GameMinutes => _gameMinutes;

        /// True if the beggar is still inside its inter-ask cadence window.
        public bool BeggarOnCooldown(EntityId beggar) =>
            _nextAskAt.TryGetValue(beggar, out var at) && _gameMinutes < at;

        /// True if this (beggar, mark) pair is still inside its re-ask cooldown.
        public bool PairOnCooldown(EntityId beggar, EntityId mark) =>
            _pairCooldown.TryGetValue(PairKey(beggar, mark), out var until) && _gameMinutes < until;

        public override void Update(long tick)
        {
            foreach (ref readonly var t in Events.GetEvents<RequestCooldownTickIntent>())
                _gameMinutes += t.Delta;

            foreach (ref readonly var s in Events.GetEvents<RequestCooldownSetIntent>())
            {
                _nextAskAt[s.Beggar] = s.NextAskAt;
                _pairCooldown[PairKey(s.Beggar, s.Mark)] = s.Until;
            }

            if (_pairCooldown.Count > CooldownPruneCap) Prune();

            // Drop cooldowns for the dead so the maps don't leak across turnover.
            foreach (ref readonly var d in Events.GetEvents<DespawnedEvent>())
                _nextAskAt.Remove(d.Entity);
        }

        void Prune()
        {
            _stale.Clear();
            foreach (var kv in _pairCooldown)
                if (_gameMinutes >= kv.Value) _stale.Add(kv.Key);
            for (int i = 0; i < _stale.Count; i++) _pairCooldown.Remove(_stale[i]);
        }

        public static long PairKey(EntityId a, EntityId b) => ((long)a.Value << 32) | (uint)b.Value;
    }
}
