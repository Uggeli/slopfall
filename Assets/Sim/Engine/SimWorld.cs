namespace DaggerfallWorkshop.Sim.Engine
{
    /// The CQRS sim world: the event bus, every registry (data, sole-writer), every
    /// system (pure read→emit), and the engine that ticks them. Replaces the old
    /// SimulationContext + TickLoop. Registry and system ORDER is irrelevant —
    /// registries apply only their own intents, systems all read the same settled
    /// tick-N snapshot — which is exactly what lets the engine run them in parallel.
    public sealed class SimWorld
    {
        public readonly EventBus Events = new EventBus();
        public SimEngine Engine { get; }
        public long Tick => Engine.Tick;

        // --- registries (data) ---
        public readonly WorldClockRegistry WorldClock;
        public readonly WeatherRegistry Weather;
        public readonly LightingRegistry Lighting;
        public readonly HolidayRegistry Holiday;
        public readonly OccupancyRegistry Occupancy;
        public readonly TownGridRegistry TownGrid;
        public readonly LedgerRegistry Ledger;
        public readonly WorldMarketRegistry WorldMarket;
        public readonly SettlementRegistry Settlements;
        public readonly BuildingRegistry Buildings;
        public readonly PositionRegistry Position;
        public readonly BehaviorRegistry Behavior;
        public readonly IntentRegistry Intent;
        public readonly IdentityRegistry Identity;
        public readonly NeedsRegistry Needs;
        public readonly VitalsRegistry Vitals;
        public readonly LifeRegistry Life;
        public readonly PersonalityRegistry Personality;
        public readonly StatsRegistry Stats;
        public readonly EffectsRegistry Effects;
        public readonly EffectAggregateRegistry EffectAggregate;
        public readonly StatusFlagsRegistry StatusFlags;
        public readonly ProgressionRegistry Progression;
        public readonly EmploymentRegistry Employment;
        public readonly ResidencyRegistry Residency;
        public readonly AffectsRegistry Affects;
        public readonly MeaningsRegistry Meanings;
        public readonly RelationsRegistry Relations;
        public readonly MemoryRegistry Memory;
        public readonly ConscienceRegistry Conscience;
        public readonly LineageRegistry Lineage;
        public readonly SubjectiveViewRegistry Subjective;
        public readonly SensedRegistry Sensed;
        public readonly CoinRegistry Coin;
        public readonly StockRegistry Stock;
        public readonly LarderRegistry Larder;
        public readonly TreasuryRegistry Treasury;
        public readonly ItemRegistry Items;
        public readonly ItemTakeRegistry ItemTake;
        public readonly PlaceMemoryRegistry PlaceMemory;
        public readonly CreatureRegistry Creatures;
        public readonly PathRegistry Path;
        public readonly SocialCooldownRegistry SocialCooldown;
        public readonly RequestCooldownRegistry RequestCooldown;
        public readonly EarningsRegistry Earnings;

        public SimWorld(int seed, double tickIntervalSeconds = 0.1)
        {
            var e = Events;
            WorldClock = new WorldClockRegistry(e); Weather = new WeatherRegistry(e);
            Lighting = new LightingRegistry(e); Holiday = new HolidayRegistry(e);
            Occupancy = new OccupancyRegistry(e); TownGrid = new TownGridRegistry(e);
            Ledger = new LedgerRegistry(e); WorldMarket = new WorldMarketRegistry(e);
            Settlements = new SettlementRegistry(e); Buildings = new BuildingRegistry(e);
            Position = new PositionRegistry(e); Behavior = new BehaviorRegistry(e);
            Intent = new IntentRegistry(e); Identity = new IdentityRegistry(e);
            Needs = new NeedsRegistry(e); Vitals = new VitalsRegistry(e); Life = new LifeRegistry(e);
            Personality = new PersonalityRegistry(e); Stats = new StatsRegistry(e);
            Effects = new EffectsRegistry(e); EffectAggregate = new EffectAggregateRegistry(e);
            StatusFlags = new StatusFlagsRegistry(e); Progression = new ProgressionRegistry(e);
            Employment = new EmploymentRegistry(e); Residency = new ResidencyRegistry(e);
            Affects = new AffectsRegistry(e); Meanings = new MeaningsRegistry(e);
            Relations = new RelationsRegistry(e); Memory = new MemoryRegistry(e);
            Conscience = new ConscienceRegistry(e); Lineage = new LineageRegistry(e);
            Subjective = new SubjectiveViewRegistry(e);
            Sensed = new SensedRegistry(e); Coin = new CoinRegistry(e); Stock = new StockRegistry(e);
            Larder = new LarderRegistry(e); Treasury = new TreasuryRegistry(e);
            Items = new ItemRegistry(e); ItemTake = new ItemTakeRegistry(e);
            PlaceMemory = new PlaceMemoryRegistry(e); Creatures = new CreatureRegistry(e);
            Path = new PathRegistry(e); SocialCooldown = new SocialCooldownRegistry(e);
            RequestCooldown = new RequestCooldownRegistry(e); Earnings = new EarningsRegistry(e);

            var registries = new Registry[]
            {
                WorldClock, Weather, Lighting, Holiday, Occupancy, TownGrid, Ledger, WorldMarket,
                Settlements, Buildings, Position, Behavior, Intent, Identity, Needs, Vitals, Life,
                Personality, Stats, Effects, EffectAggregate, StatusFlags, Progression, Employment,
                Residency, Affects, Meanings, Relations, Memory, Conscience, Lineage, Subjective, Sensed,
                Coin, Stock, Larder, Treasury, Items, ItemTake, PlaceMemory, Creatures, Path,
                SocialCooldown, RequestCooldown, Earnings,
            };

            var systems = new SimSystem[]
            {
                new TimeSystem(e, WorldClock, tickIntervalSeconds),
                new WeatherSystem(e, Weather, seed),
                new SunlightSystem(e, WorldClock, Weather),
                new HolidaySystem(e, Holiday, TownGrid),
                new HealthSystem(e, Vitals),
                new AgingSystem(e, Life, Vitals, seed),
                new EffectsSystem(e, Effects),
                new EffectAggregateSystem(e, Effects),
                new StatusFlagDeriveSystem(e, Effects),
                new SkillAdvancementSystem(e, Stats),
                new ProgressionSystem(e, Progression, Identity),
                new EconomySystem(e, Coin, Stock, Larder, Treasury, Ledger, WorldMarket, Earnings,
                    Behavior, Buildings, Residency, Employment, Settlements, PlaceMemory, WorldClock),
                new ItemSystem(e, Items, Behavior, Conscience, ItemTake, WorldClock),
                new NeedsSystem(e, Needs, Behavior, Personality, Coin, Larder, Residency, Occupancy,
                    Buildings, Stock, Subjective, Position, WorldClock),
                new OddSystem(e, Residency, Behavior, Needs, Buildings, Position, Personality, Weather,
                    Holiday, Employment, Coin, Larder, Occupancy, Conscience, Subjective, Creatures,
                    Affects, Relations, Meanings, PlaceMemory, Stock, Items, WorldClock, seed),
                new ExecutionSystem(e, Intent, Behavior, Position, WorldClock),
                new MovementSystem(e, WorldClock, Behavior, Position, TownGrid, Path, seed),
                new CreatureSystem(e, WorldClock, Position, Creatures, Identity, Vitals, seed),
                new CombatSystem(e, WorldClock, Behavior, Position, Creatures, Vitals, seed),
                new SenseSystem(e, WorldClock, Behavior, Position, Creatures, TownGrid, seed),
                new SubjectiveSystem(e, WorldClock, Sensed, Subjective, Relations, Affects, Meanings,
                    Behavior, Personality, Creatures, Residency, SocialCooldown),
                new AffectsSystem(e, WorldClock, Affects),
                new MeaningsSystem(e, WorldClock, Meanings, Residency),
                new SocialSystem(e, Behavior, Relations, Personality, Memory, WorldClock, seed),
                new RequestSystem(e, Behavior, Residency, Sensed, Coin, Relations, Personality, WorldClock, RequestCooldown),
                new LifecycleSystem(e, Identity, Residency, Creatures, Settlements),
                new RepopulationSystem(e, Identity, Residency, Buildings, Settlements),
            };

            Engine = new SimEngine(e, registries, systems);
        }

        /// One PARALLEL tick — the production path (phases ∥, registries sole-write
        /// their own data, systems read-only + locked Publish).
        public void Step() => Engine.Step();

        /// One serial tick — same order, single-threaded. The determinism reference:
        /// a parallel run must produce identical state to a serial one.
        public void StepSerial() => Engine.StepSerial();

        /// Apply queued seed intents into the registries (call once after seeding,
        /// before the first Step).
        public void ApplySeed() => Engine.SeedApply();
    }
}
