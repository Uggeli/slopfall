using System.Collections.Generic;
using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>Timescale knobs for agent memory (bunny doc: rates are config). Cadences in ticks.</summary>
    public readonly struct AgentMemoryConfig
    {
        public readonly int PerceiveCadenceTicks;     // how often an agent forms perceptions
        public readonly int AttentionK;               // top-K sensed entities perceived per batch
        public readonly int ConsolidateCadenceTicks;  // how often a sleeping agent consolidates
        public readonly EncodeConfig Encode;
        public readonly ConsolidationConfig Consolidation;

        public AgentMemoryConfig(int perceiveCadenceTicks, int attentionK, int consolidateCadenceTicks,
                                 EncodeConfig encode, ConsolidationConfig consolidation)
        {
            PerceiveCadenceTicks = perceiveCadenceTicks;
            AttentionK = attentionK;
            ConsolidateCadenceTicks = consolidateCadenceTicks;
            Encode = encode;
            Consolidation = consolidation;
        }

        // 600 ticks ~ 1 game-minute; 36000 ~ 1 game-hour (864000 ticks/day).
        public static readonly AgentMemoryConfig Default =
            new AgentMemoryConfig(600, 4, 36000, EncodeConfig.Default, ConsolidationConfig.Default);
    }

    /// <summary>One agent's private memory: learned categories + the bounded record stores.</summary>
    public sealed class AgentMemory
    {
        public readonly MeaningsStore Meanings = new MeaningsStore(AgentMemoryStores.MeaningsCap, MeaningsConfig.Default);
        public readonly AgentMemoryStores Stores = new AgentMemoryStores();
    }

    /// <summary>Perceive an entity: recognize+fold+surprise, write a record if gated.</summary>
    public struct MemoryPerceiveIntent : IEvent
    {
        public EntityId Perceiver, Perceived;
        public AtomBag Signature, Percept;
        public Fixed Arousal;
    }

    /// <summary>Run a sleep consolidation pass over an agent's memory.</summary>
    public struct MemoryConsolidateIntent : IEvent { public EntityId Agent; }

    /// <summary>Reinforce the perceiver's recognized category (of the signature) toward an outcome.</summary>
    public struct MemoryReinforceIntent : IEvent { public EntityId Perceiver; public AtomBag Signature; public Fixed Outcome; public Fixed Scale; }

    /// <summary>Stamp/refresh one atom on the agent's memory of a place (building). First-hand by
    /// default; <see cref="SecondHand"/> (relayed by word of mouth) caps strength to
    /// salience × <see cref="TrustScale"/>, drops SURPRISE/INNATE, and never downgrades a witnessed trace.</summary>
    public struct PlaceObserveIntent : IEvent
    { public EntityId Agent; public int Building; public AtomTypeId Atom; public Fixed Value; public bool SecondHand; public Fixed TrustScale; }

    /// <summary>
    /// Sole writer of per-agent memory. Systems read percepts and emit intents; this registry
    /// applies them by calling the memory core (Perceive folds + builds a record routed to THINGS;
    /// Consolidate runs RE-DIFF/MINT/DECAY). The mutable stores are mutated only here.
    /// </summary>
    public sealed class AgentMemoryRegistry : Registry
    {
        readonly Dictionary<EntityId, AgentMemory> _d = new Dictionary<EntityId, AgentMemory>();
        readonly AgentMemoryConfig _cfg;

        public AgentMemoryRegistry(EventBus events, AgentMemoryConfig cfg) : base(events) { _cfg = cfg; }

        /// <summary>Load-time: give an agent an empty memory.</summary>
        public void Seed(EntityId id) { if (!_d.ContainsKey(id)) _d[id] = new AgentMemory(); }

        /// <summary>Load-time direct place stamp (no intent) — the first SeedX of agent-memory seeding.</summary>
        public void SeedPlace(EntityId agent, int building, AtomTypeId atom, Fixed value)
        { if (_d.TryGetValue(agent, out var mem)) MergePlaceAtom(mem, building, atom, value, 0, false, Fixed.One); }

        /// <summary>Seed an innate category belief into the agent's MEANINGS store (load-time, direct —
        /// the SeedPlace pattern). SeedPriors uses this.</summary>
        public void SeedInnatePrior(EntityId agent, AtomBag signature, Fixed valence, Fixed confidence)
        { if (_d.TryGetValue(agent, out var mem)) mem.Meanings.SeedInnate(signature, valence, confidence); }

        /// <summary>Upsert one observed fact into the agent's memory of a building. FIRST-HAND uses the
        /// atom's intrinsic salience (Kind=INNATE, Danger=SURPRISE, Provisions=ordinary) and refreshes
        /// to vivid. SECOND-HAND (heard) caps strength to salience × trustScale, flags None (fades),
        /// and may only RAISE an existing atom's strength — never downgrade a witnessed (SURPRISE) trace.</summary>
        static void MergePlaceAtom(AgentMemory mem, int building, AtomTypeId atom, Fixed value, long tick,
                                   bool secondHand, Fixed trustScale)
        {
            var key = new MemoryKey(building);
            AtomMeta incoming = MemorySalience.For(atom);
            if (secondHand)
            {
                int ts = trustScale.Raw; if (ts < 0) ts = 0; else if (ts > Fixed.Scale) ts = Fixed.Scale;
                int s = incoming.Strength * ts / Fixed.Scale;        // integer, deterministic (Scale=256)
                if (s <= 0) return;                                  // no belief → leave no trace
                incoming = new AtomMeta((byte)s, MemoryFlags.None);
            }

            if (!mem.Stores.Places.TryGet(key, out var rec))
            {
                var bag0 = AtomBag.Create(new[] { new Atom(atom, value) });
                mem.Stores.Places.Encode(new MemoryRecord(key, CategoryId.None, bag0, new[] { incoming }, tick, tick));
                return;
            }

            // Rebuild the record's bag + per-atom meta, upserting the observed atom in sorted order.
            var atoms = rec.DeltaBag.Atoms;
            var outA = new List<Atom>(atoms.Count + 1);
            var outM = new List<AtomMeta>(atoms.Count + 1);
            bool placed = false;
            for (int i = 0; i < atoms.Count; i++)
            {
                if (!placed && atoms[i].Type.CompareTo(atom) > 0)
                { outA.Add(new Atom(atom, value)); outM.Add(incoming); placed = true; }
                if (atoms[i].Type == atom)
                {
                    // First-hand refreshes to vivid; second-hand may only RAISE strength and keeps the
                    // existing flags — so hearsay cannot downgrade a witnessed (SURPRISE) trace.
                    AtomMeta merged = secondHand
                        ? new AtomMeta(rec.Meta[i].Strength >= incoming.Strength ? rec.Meta[i].Strength : incoming.Strength, rec.Meta[i].Flags)
                        : incoming;
                    outA.Add(new Atom(atom, value)); outM.Add(merged); placed = true; continue;
                }
                outA.Add(atoms[i]); outM.Add(rec.Meta[i]);
            }
            if (!placed) { outA.Add(new Atom(atom, value)); outM.Add(incoming); }
            mem.Stores.Places.Encode(new MemoryRecord(key, CategoryId.None, AtomBag.Create(outA), outM.ToArray(), rec.WrittenAt, tick));
        }

        public override void Update(long tick)
        {
            var perceives = Events.GetEvents<MemoryPerceiveIntent>();
            for (int i = 0; i < perceives.Length; i++)
            {
                if (!_d.TryGetValue(perceives[i].Perceiver, out var mem)) continue;
                EncodeResult res = MemoryEncoder.Perceive(
                    mem.Meanings, perceives[i].Signature, perceives[i].Percept, perceives[i].Arousal,
                    new MemoryKey(perceives[i].Perceived.Value), tick, _cfg.Encode);
                if (res.Written) mem.Stores.Things.Encode(res.Record);
            }

            var reinforce = Events.GetEvents<MemoryReinforceIntent>();
            for (int i = 0; i < reinforce.Length; i++)
            {
                if (!_d.TryGetValue(reinforce[i].Perceiver, out var rm)) continue;
                CategoryId cat = rm.Meanings.Recognize(reinforce[i].Signature);
                // Scale.Raw == 0 means "unset" (Fixed zero-init) — treat as Fixed.One (full trust).
                Fixed scale = reinforce[i].Scale.Raw == 0 ? Fixed.One : reinforce[i].Scale;
                if (!cat.IsNone) rm.Meanings.Reinforce(cat, reinforce[i].Signature, reinforce[i].Outcome, scale);
            }

            var places = Events.GetEvents<PlaceObserveIntent>();
            for (int i = 0; i < places.Length; i++)
                if (_d.TryGetValue(places[i].Agent, out var pm))
                    MergePlaceAtom(pm, places[i].Building, places[i].Atom, places[i].Value, tick,
                                   places[i].SecondHand, places[i].TrustScale);

            var cons = Events.GetEvents<MemoryConsolidateIntent>();
            for (int i = 0; i < cons.Length; i++)
                if (_d.TryGetValue(cons[i].Agent, out var mem))
                {
                    Consolidation.Pass(mem.Stores.Things, mem.Meanings, _cfg.Consolidation);
                    // PLACES fade on the same sleep cadence — danger a place no longer earns
                    // decays away (re-observation refreshes a place's strength back to vivid).
                    mem.Stores.Places.Decay(_cfg.Consolidation.DecayNormalRate, _cfg.Consolidation.DecaySurpriseRate);
                }

            var gone = Events.GetEvents<DespawnedEvent>();
            for (int i = 0; i < gone.Length; i++) _d.Remove(gone[i].Entity);
        }

        public bool TryGet(EntityId id, out AgentMemory mem) => _d.TryGetValue(id, out mem);
        public int Count => _d.Count;
        public IEnumerable<KeyValuePair<EntityId, AgentMemory>> All => _d;
    }
}
