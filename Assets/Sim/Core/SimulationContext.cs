namespace DaggerfallWorkshop.Sim
{
    /// Container for all sim-thread-owned services + registries.
    /// Phase 0: just clock, RNG, event bus, and the cross-thread input queue.
    /// Phase 1+ will add registry references here as they land.
    public sealed class SimulationContext
    {
        public EventBus Events { get; }
        public SimulationTime Time { get; }
        public SimRandom Random { get; }
        public InputBus Inputs { get; }

        // Phase 1 registries — mirrored from DFU state, eventually authoritative.
        public IdentityRegistry Identity { get; }
        public PositionRegistry Position { get; }
        public VitalsRegistry Vitals { get; }
        public WorldClockRegistry WorldClock { get; }
        public WeatherRegistry Weather { get; }
        public LightingRegistry Lighting { get; }
        public EffectsRegistry Effects { get; }
        public EffectAggregateRegistry EffectAggregate { get; }
        public StatusFlagsRegistry StatusFlags { get; }
        public StatsRegistry Stats { get; }
        public ProgressionRegistry Progression { get; }
        public BuildingRegistry Buildings { get; }
        public ResidencyRegistry Residency { get; }
        public NeedsRegistry Needs { get; }
        public BehaviorRegistry Behavior { get; }
        public RelationsRegistry Relations { get; }
        public MemoryRegistry Memory { get; }
        public OccupancyRegistry Occupancy { get; }
        public TownGridRegistry TownGrid { get; }
        public CoinRegistry Coin { get; }
        public PersonalityRegistry Personality { get; }

        public SimulationContext(EventBus events, SimulationTime time, SimRandom random, InputBus inputs)
        {
            Events = events;
            Time = time;
            Random = random;
            Inputs = inputs;

            Identity = new IdentityRegistry();
            Position = new PositionRegistry();
            Vitals = new VitalsRegistry();
            WorldClock = new WorldClockRegistry();
            Weather = new WeatherRegistry();
            Lighting = new LightingRegistry();
            Effects = new EffectsRegistry();
            EffectAggregate = new EffectAggregateRegistry();
            StatusFlags = new StatusFlagsRegistry();
            Stats = new StatsRegistry();
            Progression = new ProgressionRegistry();
            Buildings = new BuildingRegistry();
            Residency = new ResidencyRegistry();
            Needs = new NeedsRegistry();
            Behavior = new BehaviorRegistry();
            Relations = new RelationsRegistry();
            Memory = new MemoryRegistry();
            Occupancy = new OccupancyRegistry();
            TownGrid = new TownGridRegistry();
            Coin = new CoinRegistry();
            Personality = new PersonalityRegistry();
        }
    }
}
