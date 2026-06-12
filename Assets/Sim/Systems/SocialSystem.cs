using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Turns co-location into social fabric. Each tick: group civilians who
    /// are Doing a social activity at the same building, then for every
    /// occupant grow directed relations toward up to MaxPartners co-occupants
    /// (you can only talk to so many people — Atoms' SUM projection, capped).
    ///
    /// Regard is signed by a deterministic per-pair affinity hash: some pairs
    /// click, some grate. Personalities will replace the hash; the shape
    /// (familiarity + signed regard per directed pair) stays.
    ///
    /// Sole writer of OccupancyRegistry, RelationsRegistry, MemoryRegistry.
    public sealed class SocialSystem : ISystem
    {
        const int MaxPartners = 6;
        const double MetFamiliarity = 0.05;
        const double FriendFamiliarity = 0.3;
        const double FriendRegard = 0.25;
        /// Shared game-minutes for familiarity to reach 1.0.
        const double MinutesToFullFamiliarity = 600;

        SimulationContext _ctx;
        readonly Dictionary<int, List<EntityId>> _groups = new Dictionary<int, List<EntityId>>();
        readonly List<RelationImpulseEvent> _impulses = new List<RelationImpulseEvent>();

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
            ctx.Events.Subscribe<RelationImpulseEvent>(e => _impulses.Add(e));
        }

        /// Apply regard/familiarity impulses from systems that don't own the
        /// relations registry (gratitude and grudges from RequestSystem).
        public void ProcessEvents()
        {
            for (int i = 0; i < _impulses.Count; i++)
            {
                var e = _impulses[i];
                var next = CloneRelations(e.Who);
                if (!next.Of.TryGetValue(e.Other, out var rel))
                {
                    rel = new RelationData();
                    next.Of[e.Other] = rel;
                }
                rel.Regard = Clamp(rel.Regard + e.RegardDelta, -1, 1);
                rel.Familiarity = Clamp01(rel.Familiarity + e.FamiliarityDelta);
                _ctx.Relations.Set(e.Who, next);

                if (e.RecordMemory)
                    _ctx.Memory.Set(e.Who, Remember(e.Who, null, new MemoryEntry
                    {
                        Tick = _ctx.Time.Tick, Kind = e.Memory, Other = e.Other, Building = -1,
                    }));
            }
            _impulses.Clear();
        }

        public void Update(long tick)
        {
            var clock = _ctx.WorldClock.Current;
            if (clock.Year == 0) return;
            double gameMinutes = _ctx.Time.TickIntervalSeconds * clock.TimeScale / 60.0;

            // --- Build occupancy groups. ---
            foreach (var list in _groups.Values) list.Clear();
            foreach (var kv in _ctx.Behavior.All)
            {
                var b = kv.Value;
                if (b.Phase != ActivityPhase.Doing) continue;
                if (!IsSocialActivity(b.Activity)) continue;
                if (b.TargetBuilding < 0) continue;

                if (!_groups.TryGetValue(b.TargetBuilding, out var list))
                {
                    list = new List<EntityId>();
                    _groups[b.TargetBuilding] = list;
                }
                list.Add(kv.Key);
            }

            var company = new Dictionary<EntityId, int>();
            var place = new Dictionary<int, int>();
            foreach (var kv in _groups)
            {
                var group = kv.Value;
                if (group.Count == 0) continue;
                // Deterministic partner order regardless of registry iteration.
                group.Sort((a, b) => a.Value.CompareTo(b.Value));
                place[kv.Key] = group.Count;
                for (int i = 0; i < group.Count; i++)
                    company[group[i]] = group.Count - 1;

                if (group.Count > 1)
                    GrowRelations(kv.Key, group, gameMinutes, tick);
            }
            _ctx.Occupancy.Swap(company, place);
        }

        void GrowRelations(int building, List<EntityId> group, double gameMinutes, long tick)
        {
            double familiarityGain = gameMinutes / MinutesToFullFamiliarity;

            for (int i = 0; i < group.Count; i++)
            {
                var self = group[i];
                var next = CloneRelations(self);
                MemoryData memory = null;

                // Gossip — listener-pull: every few ticks of shared time, hear
                // about the teller's juiciest contact and lean toward their
                // view of them, weighted by how well you know the teller.
                // Opinions propagate through tavern networks; nobody needs to
                // have met the person being discussed.
                if (Hash(self.Value, tick) % 3 == 0)
                {
                    var teller = group[i == 0 ? (group.Count > 1 ? 1 : 0) : 0];
                    if (teller != self)
                        HearGossip(self, teller, next);
                }

                int partners = 0;
                for (int j = 0; j < group.Count && partners < MaxPartners; j++)
                {
                    if (i == j) continue;
                    var other = group[j];
                    partners++;

                    if (!next.Of.TryGetValue(other, out var rel))
                    {
                        rel = new RelationData();
                        next.Of[other] = rel;
                    }

                    bool wasMet = rel.Familiarity >= MetFamiliarity;
                    rel.Familiarity = Clamp01(rel.Familiarity + familiarityGain);
                    rel.Regard = Clamp(rel.Regard + Affinity(self, other) * familiarityGain, -1, 1);

                    if (!wasMet && rel.Familiarity >= MetFamiliarity)
                    {
                        memory = Remember(self, memory, new MemoryEntry
                        {
                            Tick = tick, Kind = MemoryKind.Met, Other = other, Building = building,
                        });
                        _ctx.Events.Emit(new MetSimEvent { Who = self, Other = other, Building = building });
                    }

                    if (!rel.FriendAnnounced && rel.Familiarity >= FriendFamiliarity && rel.Regard >= FriendRegard)
                    {
                        rel.FriendAnnounced = true;
                        memory = Remember(self, memory, new MemoryEntry
                        {
                            Tick = tick, Kind = MemoryKind.BecameFriend, Other = other, Building = building,
                        });
                        _ctx.Events.Emit(new FriendshipFormedEvent { Who = self, Other = other });
                    }
                }

                _ctx.Relations.Set(self, next);
                if (memory != null)
                    _ctx.Memory.Set(self, memory);
            }
        }

        /// The teller's strongest opinion (biggest |regard| about someone the
        /// listener isn't, known well enough to gossip about) rubs off on the
        /// listener, scaled by gossip strength and trust in the teller.
        void HearGossip(EntityId listener, EntityId teller, RelationsData listenerNext)
        {
            const double GossipFactor = 0.15;
            const double MinTellerFamiliarity = 0.2;

            if (!_ctx.Relations.TryGet(teller, out var tellerRelations)) return;

            EntityId about = EntityId.None;
            double aboutRegard = 0;
            foreach (var kv in tellerRelations.Of)
            {
                if (kv.Key == listener) continue;
                if (kv.Value.Familiarity < MinTellerFamiliarity) continue;
                if (System.Math.Abs(kv.Value.Regard) > System.Math.Abs(aboutRegard)
                    || (System.Math.Abs(kv.Value.Regard) == System.Math.Abs(aboutRegard)
                        && !about.IsNone && kv.Key.Value < about.Value))
                {
                    about = kv.Key;
                    aboutRegard = kv.Value.Regard;
                }
            }
            if (about.IsNone || System.Math.Abs(aboutRegard) < 0.1) return;

            double trust = listenerNext.Of.TryGetValue(teller, out var rel) ? rel.Familiarity : 0;
            if (trust <= 0) return;

            if (!listenerNext.Of.TryGetValue(about, out var heard))
            {
                heard = new RelationData();
                listenerNext.Of[about] = heard;
            }
            heard.Regard = Clamp(heard.Regard + aboutRegard * GossipFactor * trust, -1, 1);
            heard.Familiarity = Clamp01(heard.Familiarity + 0.02);  // knows OF them now
        }

        static uint Hash(int idValue, long tick)
        {
            uint x = (uint)(idValue * 2654435761u) ^ (uint)(tick * 40503u);
            x ^= x >> 13; x *= 0x5bd1e995; x ^= x >> 15;
            return x;
        }

        RelationsData CloneRelations(EntityId id)
        {
            var next = new RelationsData();
            if (_ctx.Relations.TryGet(id, out var current) && current != null)
            {
                foreach (var kv in current.Of)
                    next.Of[kv.Key] = new RelationData
                    {
                        Familiarity = kv.Value.Familiarity,
                        Regard = kv.Value.Regard,
                        FriendAnnounced = kv.Value.FriendAnnounced,
                    };
            }
            return next;
        }

        MemoryData Remember(EntityId id, MemoryData pending, MemoryEntry entry)
        {
            var next = pending;
            if (next == null)
            {
                next = new MemoryData();
                if (_ctx.Memory.TryGet(id, out var current) && current != null)
                    next.Entries.AddRange(current.Entries);
            }
            next.Entries.Insert(0, entry);
            if (next.Entries.Count > MemoryRegistry.MaxEntries)
                next.Entries.RemoveRange(MemoryRegistry.MaxEntries, next.Entries.Count - MemoryRegistry.MaxEntries);
            return next;
        }

        static bool IsSocialActivity(ActivityKind kind) =>
            kind == ActivityKind.Socialize || kind == ActivityKind.EatTavern || kind == ActivityKind.Visit;

        /// Symmetric, deterministic pair chemistry in [-1, 1]. Most pairs are
        /// mildly positive (shared drinks beat solitude); a tail grates.
        public static double Affinity(EntityId a, EntityId b)
        {
            int lo = a.Value < b.Value ? a.Value : b.Value;
            int hi = a.Value < b.Value ? b.Value : a.Value;
            uint x = (uint)(lo * 2654435761u) ^ (uint)(hi * 97u + 13);
            x ^= x >> 13; x *= 0x5bd1e995; x ^= x >> 15;
            double h = x / (double)uint.MaxValue;     // 0..1
            return h * 1.6 - 0.5;                      // -0.5 .. 1.1 -> mostly positive
        }

        static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
        static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
