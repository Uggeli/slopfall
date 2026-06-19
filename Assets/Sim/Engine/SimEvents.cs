namespace DaggerfallWorkshop.Sim.Engine
{
    // Signal & input events as value types (IEvent). These are cross-system
    // notifications and external inputs — distinct from per-registry INTENT events,
    // which are declared next to the registry that applies them. Enum types
    // (ActivityKind, DamageType, MemoryKind, WeatherKind, EntityId) resolve from the
    // enclosing DaggerfallWorkshop.Sim namespace.

    // --- time ---
    public struct TimeTickedEvent : IEvent { public long Tick; public double SimSeconds; }
    public struct NewHourEvent : IEvent { public int Hour, Day, Month, Year; }
    public struct NewDayEvent : IEvent { public int Day, Month, Year; }
    public struct NewMonthEvent : IEvent { public int Month, Year; }
    public struct NewYearEvent : IEvent { public int Year; }
    public struct DawnEvent : IEvent { }
    public struct DuskEvent : IEvent { }
    public struct MiddayEvent : IEvent { }
    public struct MidnightEvent : IEvent { }
    public struct CityLightsOnEvent : IEvent { }
    public struct CityLightsOffEvent : IEvent { }

    // --- external inputs (injected at tick start) ---
    public struct SeedClockInput : IEvent { public int Year, Month, Day, Hour, Minute; public float Second, TimeScale; }
    public struct SetTimeScaleInput : IEvent { public float TimeScale; }

    // --- weather ---
    public struct WeatherChangedEvent : IEvent { public WeatherKind From, To; }

    // --- health / lifecycle ---
    public enum DamageType { Physical, Magic, Fire, Cold, Shock, Poison, Disease, Age }

    public struct DamageEvent : IEvent { public EntityId Target, Source; public int Amount; public DamageType Type; }
    public struct HealEvent : IEvent { public EntityId Target, Source; public int Amount; }
    public struct DeathEvent : IEvent { public EntityId Entity, Killer; public DamageType FatalDamageType; }
    public struct DespawnedEvent : IEvent { public EntityId Entity; public int Settlement, Building; }
    public struct EscheatEvent : IEvent { public EntityId Dead; }

    // --- behavior ---
    public struct ActivityStartedEvent : IEvent { public EntityId Entity; public ActivityKind Activity; public int TargetBuilding; }
    public struct ArrivedAtTargetEvent : IEvent { public EntityId Entity; }

    // --- social / interaction ---
    public struct GreetingEvent : IEvent { public EntityId A, B; }
    public struct DislikeNearbyEvent : IEvent { public EntityId Who, Whom; }
    public struct AskJourneyEvent : IEvent { public EntityId Asker, Target; public float TargetX, TargetZ; }
    public struct MetEvent : IEvent { public EntityId Who, Other; public int Building; }
    public struct FriendshipFormedEvent : IEvent { public EntityId Who, Other; }
    public struct FriendshipLapsedEvent : IEvent { public EntityId Who, Other; }
    public struct RelationImpulseEvent : IEvent { public EntityId Who, Other; public double RegardDelta, FamiliarityDelta; public MemoryKind Memory; public bool RecordMemory; }
    public struct HelpGrantedEvent : IEvent { public EntityId Asker, Giver; public double Amount; }
    public struct HelpRefusedEvent : IEvent { public EntityId Asker, Refuser; }

    // --- economy / items ---
    public struct CoinTransferEvent : IEvent { public EntityId From, To; public double Amount; }
    public struct ProvisionsTakenEvent : IEvent { public EntityId Taker, Owner; public int Building; public double Units; }

    // --- progression / skill ---
    public struct LevelUpEvent : IEvent { public EntityId Entity; public int NewLevel; }
    public struct SkillUsedEvent : IEvent { public EntityId Entity; public string Skill; public int Magnitude; }
    public struct SkillAdvancedEvent : IEvent { public EntityId Entity; public string Skill; public int NewValue; }

    // --- holiday ---
    public struct HolidayBeganEvent : IEvent { public int HolidayId; }

    // --- effects (signal side; the apply-intents live with EffectsRegistry) ---
    public struct EffectAppliedEvent : IEvent { public EntityId Target, Source; public string Key; public int Magnitude; }
    public struct EffectTickedEvent : IEvent { public EntityId Target, Source; public string Key; public int Magnitude; }
    public struct EffectExpiredEvent : IEvent { public EntityId Target; public string Key; }
}
