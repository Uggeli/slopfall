using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of DaggerfallWorkshop.Sim.SocialSystem. Turns co-location into
    // social fabric: each tick group civilians Doing a social activity at the same
    // building, decay every directed relation toward neutral on a game-hour cadence,
    // grow relations + gossip within groups, and rebuild the occupancy snapshot. Holds
    // NO tick state — the legacy scratch buffers (_groups/_decayKeys/_dropList/
    // _lapseList) and the _gameMinutesSinceDecay accumulator are now locals / event-
    // driven. Reads Behavior + Relations + Personality read-only; writes via whole-value
    // set intents (RelationsRegistry / MemoryRegistry / OccupancyRegistry are the sole
    // appliers) and emits the social signal events.
    //
    // Cadence: the legacy ~1 game-hour decay accumulator becomes event-driven — decay
    // runs on a NewHourEvent this tick (one game-hour boundary), passing gameHours = 1.0.
    //
    // RelationImpulseEvent consumption: the legacy ProcessEvents() folded gratitude /
    // grudge impulses (from RequestSystem, via AffectsSystem) into relations + memory.
    // Those are read from this tick's GetEvents<RelationImpulseEvent>() batch and folded
    // into the same recomputed rows.
    //
    // Pure helpers (Decay, TraitIndex, MemoryKind, ActivityKind/Phase, RelationData,
    // RelationsData, MemoryData/MemoryEntry) resolve unqualified from the enclosing
    // DaggerfallWorkshop.Sim namespace.
    public sealed class SocialSystem : SimSystem
    {
        const int MaxPartners = 6;
        const double MetFamiliarity = 0.05;
        const double FriendFamiliarity = 0.3;
        const double FriendRegard = 0.25;
        const double MinutesToFullFamiliarity = 600;

        const double RelationBaseline = 0.0;
        const double FamiliarityDecayPerHour = 0.01;
        const double RegardDecayPerHour = 0.01;
        const double ForgetThreshold = 0.01;

        readonly BehaviorRegistry _behavior;
        readonly RelationsRegistry _relations;
        readonly PersonalityRegistry _personality;
        readonly MemoryRegistry _memory;
        readonly WorldClockRegistry _worldClock;
        readonly int _seed;

        public SocialSystem(
            EventBus events,
            BehaviorRegistry behavior,
            RelationsRegistry relations,
            PersonalityRegistry personality,
            MemoryRegistry memory,
            WorldClockRegistry worldClock,
            int seed) : base(events)
        {
            _behavior = behavior;
            _relations = relations;
            _personality = personality;
            _memory = memory;
            _worldClock = worldClock;
            _seed = seed;
        }

        public override void Update(long tick)
        {
            var clock = _worldClock.Current;
            if (clock.Year == 0) return;
            double gameMinutes = clock.DeltaGameSeconds / 60.0;

            // --- Impulse fold (legacy ProcessEvents): gratitude/grudges from
            // RequestSystem applied to relations + memory. Accumulate per-entity so a
            // single recomputed RelationsData/MemoryData per id carries all of them
            // (whole-value set — one applied row per id wins). ---
            var impulses = Events.GetEvents<RelationImpulseEvent>();

            // Snapshot the rows each impulsed entity will mutate, so several impulses to
            // the same id compose on one copy-on-write row (matching the legacy
            // CloneRelations-per-impulse-then-Set chain, last-write-wins per id).
            Dictionary<EntityId, RelationsData> impulseRows = null;
            Dictionary<EntityId, MemoryData> impulseMem = null;
            for (int i = 0; i < impulses.Length; i++)
            {
                var e = impulses[i];
                impulseRows ??= new Dictionary<EntityId, RelationsData>();
                if (!impulseRows.TryGetValue(e.Who, out var next))
                {
                    next = CloneRelations(e.Who);
                    impulseRows[e.Who] = next;
                }

                if (!next.Of.TryGetValue(e.Other, out var rel))
                {
                    rel = new RelationData();
                    next.Of[e.Other] = rel;
                }
                rel.Regard = Clamp(rel.Regard + e.RegardDelta, -1, 1);
                rel.Familiarity = Clamp01(rel.Familiarity + e.FamiliarityDelta);

                if (rel.FriendAnnounced && (rel.Familiarity < FriendFamiliarity || rel.Regard < FriendRegard))
                {
                    rel.FriendAnnounced = false;
                    Events.Publish(new FriendshipLapsedEvent { Who = e.Who, Other = e.Other });
                }

                if (e.RecordMemory)
                {
                    impulseMem ??= new Dictionary<EntityId, MemoryData>();
                    impulseMem.TryGetValue(e.Who, out var pending);
                    var mem = Remember(e.Who, pending, new MemoryEntry
                    {
                        Tick = tick, Kind = e.Memory, Other = e.Other, Building = -1,
                    });
                    impulseMem[e.Who] = mem;
                }
            }
            if (impulseRows != null)
                foreach (var kv in impulseRows)
                    Events.Publish(new RelationsSetIntent { Id = kv.Key, Data = kv.Value });
            if (impulseMem != null)
                foreach (var kv in impulseMem)
                    Events.Publish(new MemorySetIntent { Id = kv.Key, Data = kv.Value });

            // --- Erode relations toward neutral on a ~1 game-hour cadence. The legacy
            // accumulator fired at >=60 accumulated game-minutes; event-driven now, it
            // fires once per NewHourEvent (one game-hour boundary). Decaying before the
            // refresh below keeps net change positive only where there is contact. The
            // impulse-mutated rows above are the same tick's intents, applied next tick,
            // so DecayRelations reads the pre-impulse settled store — same ordering the
            // legacy ProcessEvents()->Update() pair produced (impulses then decay both
            // wrote, last-write-wins per id). ---
            bool decayHour = Events.GetEvents<NewHourEvent>().Length > 0;
            if (decayHour)
                DecayRelations(1.0, tick, impulseRows);

            // --- Build occupancy groups (local scratch — was the _groups field). ---
            var groups = new Dictionary<int, List<EntityId>>();
            foreach (var kv in _behavior.All)
            {
                var b = kv.Value;
                if (b.Phase != ActivityPhase.Doing) continue;
                if (!IsSocialActivity(b.Activity)) continue;
                if (b.TargetBuilding < 0) continue;

                if (!groups.TryGetValue(b.TargetBuilding, out var list))
                {
                    list = new List<EntityId>();
                    groups[b.TargetBuilding] = list;
                }
                list.Add(kv.Key);
            }

            // Per-entity relations growth must compose with the decay/impulse rows this
            // tick (one whole-value set per id wins). Carry forward the rows already
            // staged this tick so growth builds on top of them.
            var grownRows = new Dictionary<EntityId, RelationsData>();
            var grownMem = new Dictionary<EntityId, MemoryData>();

            var company = new Dictionary<EntityId, int>();
            var place = new Dictionary<int, int>();
            var occupants = new Dictionary<int, List<EntityId>>();

            // Iterate groups in building-key order for deterministic emission, matching
            // the legacy Dictionary enumeration only insofar as growth is order-free per
            // id; sort the building keys to be replay-exact.
            var buildingKeys = new List<int>(groups.Keys);
            buildingKeys.Sort();
            foreach (var bkey in buildingKeys)
            {
                var group = groups[bkey];
                if (group.Count == 0) continue;
                group.Sort((a, b) => a.Value.CompareTo(b.Value));
                place[bkey] = group.Count;
                occupants[bkey] = new List<EntityId>(group);
                for (int i = 0; i < group.Count; i++)
                    company[group[i]] = group.Count - 1;

                if (group.Count > 1)
                    GrowRelations(bkey, group, gameMinutes, tick, grownRows, grownMem, impulseRows);
            }

            foreach (var kv in grownRows)
                Events.Publish(new RelationsSetIntent { Id = kv.Key, Data = kv.Value });
            foreach (var kv in grownMem)
                Events.Publish(new MemorySetIntent { Id = kv.Key, Data = kv.Value });

            Events.Publish(new OccupancySetIntent { Company = company, Place = place, Occupants = occupants });
        }

        /// Erode every directed relation toward neutral, over ALL relation rows. Key-
        /// ordered iteration + sorted event emission keep it replay-exact. Cooled-out
        /// edges are dropped (doubling as reference-GC). Rows already mutated by impulses
        /// this tick (staged, not yet applied) are decayed on top of those staged copies
        /// so the per-id whole-value set composes.
        void DecayRelations(double gameHours, long tick, Dictionary<EntityId, RelationsData> staged)
        {
            var decayKeys = new List<EntityId>();
            foreach (var kv in _relations.All) decayKeys.Add(kv.Key);
            if (staged != null)
                foreach (var k in staged.Keys)
                    if (!_relations.TryGet(k, out _)) decayKeys.Add(k);
            decayKeys.Sort((a, b) => a.Value.CompareTo(b.Value));

            var dropList = new List<EntityId>();
            var lapseList = new List<EntityId>();

            for (int k = 0; k < decayKeys.Count; k++)
            {
                var id = decayKeys[k];
                var next = (staged != null && staged.TryGetValue(id, out var pre)) ? pre : CloneRelations(id);
                if (next.Of.Count == 0) continue;

                bool changed = false;
                dropList.Clear();
                lapseList.Clear();
                foreach (var pair in next.Of)
                {
                    var rel = pair.Value;
                    double fam = Decay.TowardBaseline(rel.Familiarity, RelationBaseline, FamiliarityDecayPerHour, gameHours);
                    double reg = Decay.TowardBaseline(rel.Regard, RelationBaseline, RegardDecayPerHour, gameHours);
                    if (fam != rel.Familiarity || reg != rel.Regard) changed = true;
                    rel.Familiarity = fam;
                    rel.Regard = reg;

                    if (rel.FriendAnnounced && (fam < FriendFamiliarity || reg < FriendRegard))
                    {
                        rel.FriendAnnounced = false;
                        changed = true;
                        lapseList.Add(pair.Key);
                    }

                    if (fam < ForgetThreshold && reg < ForgetThreshold && reg > -ForgetThreshold)
                        dropList.Add(pair.Key);
                }

                lapseList.Sort((a, b) => a.Value.CompareTo(b.Value));
                for (int l = 0; l < lapseList.Count; l++)
                    Events.Publish(new FriendshipLapsedEvent { Who = id, Other = lapseList[l] });

                for (int d = 0; d < dropList.Count; d++)
                {
                    next.Of.Remove(dropList[d]);
                    changed = true;
                }

                // Stage so a later growth pass composes; emit (a staged row that growth
                // never touches is published here, else re-published with growth folded).
                if (changed || (staged != null && staged.ContainsKey(id)))
                {
                    if (staged != null) staged[id] = next;
                    Events.Publish(new RelationsSetIntent { Id = id, Data = next });
                }
            }
        }

        void GrowRelations(int building, List<EntityId> group, double gameMinutes, long tick,
            Dictionary<EntityId, RelationsData> grownRows, Dictionary<EntityId, MemoryData> grownMem,
            Dictionary<EntityId, RelationsData> staged)
        {
            double familiarityGain = gameMinutes / MinutesToFullFamiliarity;

            for (int i = 0; i < group.Count; i++)
            {
                var self = group[i];
                // Compose on the freshest staged/decayed row for this id.
                RelationsData next;
                if (grownRows.TryGetValue(self, out var g)) next = g;
                else if (staged != null && staged.TryGetValue(self, out var s)) next = s;
                else next = CloneRelations(self);
                MemoryData memory = grownMem.TryGetValue(self, out var pm) ? pm : null;

                if (Hash(self.Value, tick) % 3 == 0)
                {
                    var teller = group[i == 0 ? (group.Count > 1 ? 1 : 0) : 0];
                    if (teller != self)
                        HearGossip(self, teller, next, staged, grownRows);
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
                        Events.Publish(new MetEvent { Who = self, Other = other, Building = building });
                    }

                    if (!rel.FriendAnnounced && rel.Familiarity >= FriendFamiliarity && rel.Regard >= FriendRegard)
                    {
                        rel.FriendAnnounced = true;
                        memory = Remember(self, memory, new MemoryEntry
                        {
                            Tick = tick, Kind = MemoryKind.BecameFriend, Other = other, Building = building,
                        });
                        Events.Publish(new FriendshipFormedEvent { Who = self, Other = other });
                    }
                }

                grownRows[self] = next;
                if (memory != null)
                    grownMem[self] = memory;
            }
        }

        /// The teller's strongest opinion rubs off on the listener (reads the teller's
        /// freshest staged row this tick if present, else the settled store — the legacy
        /// read the live registry, which by its tick ordering was the pre-growth row).
        void HearGossip(EntityId listener, EntityId teller, RelationsData listenerNext,
            Dictionary<EntityId, RelationsData> staged, Dictionary<EntityId, RelationsData> grownRows)
        {
            const double GossipFactor = 0.15;
            const double MinTellerFamiliarity = 0.2;

            RelationsData tellerRelations;
            if (grownRows != null && grownRows.TryGetValue(teller, out var gt)) tellerRelations = gt;
            else if (staged != null && staged.TryGetValue(teller, out var st)) tellerRelations = st;
            else if (!_relations.TryGet(teller, out tellerRelations) || tellerRelations == null) return;

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
            heard.Familiarity = Clamp01(heard.Familiarity + 0.02);
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
            if (_relations.TryGet(id, out var current) && current != null)
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

        /// Copy-on-write append to the entity's memory. A fresh pending row is seeded
        /// from the settled MemoryRegistry (preserving history under the whole-value set
        /// discipline); subsequent appends this tick build on the same pending row.
        MemoryData Remember(EntityId id, MemoryData pending, MemoryEntry entry)
        {
            var next = pending;
            if (next == null)
            {
                next = new MemoryData();
                if (_memory.TryGet(id, out var current) && current != null)
                    next.Entries.AddRange(current.Entries);
            }
            next.Entries.Insert(0, entry);
            if (next.Entries.Count > MemoryRegistry.MaxEntries)
                next.Entries.RemoveRange(MemoryRegistry.MaxEntries, next.Entries.Count - MemoryRegistry.MaxEntries);
            return next;
        }

        static bool IsSocialActivity(ActivityKind kind) =>
            kind == ActivityKind.Socialize || kind == ActivityKind.EatTavern || kind == ActivityKind.Visit;

        double Affinity(EntityId a, EntityId b)
        {
            if (!_personality.TryGet(a, out var pa) || pa == null
                || !_personality.TryGet(b, out var pb) || pb == null)
                return HashAffinity(a, b);

            double warmth = (pa.Trait(TraitIndex.Warmth) + pb.Trait(TraitIndex.Warmth) - 1.0) * 0.7;

            double dist = 0;
            for (int t = 0; t < TraitIndex.Count; t++)
                dist += System.Math.Abs(pa.Traits[t] - pb.Traits[t]);
            dist /= TraitIndex.Count;
            double similarity = (0.5 - dist) * 1.2;

            return warmth + similarity + 0.15;
        }

        static double HashAffinity(EntityId a, EntityId b)
        {
            int lo = a.Value < b.Value ? a.Value : b.Value;
            int hi = a.Value < b.Value ? b.Value : a.Value;
            uint x = (uint)(lo * 2654435761u) ^ (uint)(hi * 97u + 13);
            x ^= x >> 13; x *= 0x5bd1e995; x ^= x >> 15;
            double h = x / (double)uint.MaxValue;
            return h * 1.6 - 0.5;
        }

        static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
        static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
