using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // Homes the cross-tick greet/dislike cooldowns that the old SubjectiveSystem kept as
    // bare _nextGreetAt / _nextDislikeAt dictionaries (tick-state with no owner). In CQRS
    // those have a registry: per-entity "next allowed at" timestamps, expressed in ABSOLUTE
    // game-minutes (DaggerfallDateTime.ToSeconds()/60 — the same monotonic clock the old
    // system's self-accumulated _gameMinutes approximated, now read off WorldClock instead
    // of carried). SubjectiveSystem reads this read-only to gate greets/dislikes and emits a
    // SocialCooldownSetIntent to push the next window; this registry is the sole applier.
    //
    // The struct value carries BOTH next-times so one intent updates a whole row (a greet
    // touches the greeter and greetee; a dislike touches only the disliker). NaN/absent ==
    // "no cooldown set yet" (treated as 0 by the reader — never gated).

    /// One agent's social cooldown clocks, in absolute game-minutes.
    public struct SocialCooldownData
    {
        public double NextGreetAt;
        public double NextDislikeAt;
    }

    /// Intent: "set this agent's social cooldown row." Whole-value set per id.
    public struct SocialCooldownSetIntent : IEvent { public EntityId Id; public SocialCooldownData Data; }

    public sealed class SocialCooldownRegistry : Registry
    {
        readonly Dictionary<EntityId, SocialCooldownData> _d = new Dictionary<EntityId, SocialCooldownData>();

        public SocialCooldownRegistry(EventBus events) : base(events) { }

        public override void Update(long tick)
        {
            var sets = Events.GetEvents<SocialCooldownSetIntent>();
            for (int i = 0; i < sets.Length; i++)
                _d[sets[i].Id] = sets[i].Data;          // last write per id wins

            var gone = Events.GetEvents<DespawnedEvent>();
            for (int i = 0; i < gone.Length; i++)
                _d.Remove(gone[i].Entity);
        }

        // --- read API (read phase only) ---
        public bool TryGet(EntityId id, out SocialCooldownData data) => _d.TryGetValue(id, out data);

        /// Absolute game-minute the agent may next greet (0 if never set — not gated).
        public double NextGreetAt(EntityId id) => _d.TryGetValue(id, out var c) ? c.NextGreetAt : 0;

        /// Absolute game-minute the agent may next register a dislike (0 if never set).
        public double NextDislikeAt(EntityId id) => _d.TryGetValue(id, out var c) ? c.NextDislikeAt : 0;

        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, SocialCooldownData>> All => _d;
    }
}
