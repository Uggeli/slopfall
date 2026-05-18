namespace DaggerfallWorkshop.Sim
{
    /// Applies DamageEvent / HealEvent to VitalsRegistry and emits
    /// DeathSimEvent on a fatal hit.
    ///
    /// Phase 3e scope: infrastructure. The Phase 1 SimMirror still mirrors
    /// DaggerfallEntity.CurrentHealth into VitalsRegistry every frame, so
    /// any health change a sim emitter makes here will be overwritten by the
    /// next mirror tick unless DFU's own combat path was also updated to
    /// match. No one in the engine emits these events yet — sim-originated
    /// damage gets wired in Phase 4 combat. The flip to single-writer-of-
    /// vitals lands once damage flows exclusively through this system.
    public sealed class HealthSystem : ISystem
    {
        SimulationContext _ctx;

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
            ctx.Events.Subscribe<DamageEvent>(OnDamage);
            ctx.Events.Subscribe<HealEvent>(OnHeal);
        }

        public void ProcessEvents() { }
        public void Update(long tick) { }

        void OnDamage(DamageEvent e)
        {
            if (e.Amount <= 0) return;
            if (!_ctx.Vitals.TryGet(e.Target, out var v) || v == null) return;
            if (v.IsDead) return;

            int newHealth = v.CurrentHealth - e.Amount;
            bool fatal = newHealth <= 0;
            if (fatal) newHealth = 0;

            _ctx.Vitals.Set(e.Target, new VitalsData
            {
                CurrentHealth   = newHealth,
                MaxHealth       = v.MaxHealth,
                CurrentMagicka  = v.CurrentMagicka,
                MaxMagicka      = v.MaxMagicka,
                CurrentFatigue  = v.CurrentFatigue,
                MaxFatigue      = v.MaxFatigue,
                CurrentBreath   = v.CurrentBreath,
                MaxBreath       = v.MaxBreath,
                IsDead          = fatal || v.IsDead,
            });

            if (fatal && !v.IsDead)
                _ctx.Events.Emit(new DeathSimEvent { Entity = e.Target, Killer = e.Source, FatalDamageType = e.Type });
        }

        void OnHeal(HealEvent e)
        {
            if (e.Amount <= 0) return;
            if (!_ctx.Vitals.TryGet(e.Target, out var v) || v == null) return;
            if (v.IsDead) return;

            int newHealth = v.CurrentHealth + e.Amount;
            if (newHealth > v.MaxHealth) newHealth = v.MaxHealth;

            _ctx.Vitals.Set(e.Target, new VitalsData
            {
                CurrentHealth   = newHealth,
                MaxHealth       = v.MaxHealth,
                CurrentMagicka  = v.CurrentMagicka,
                MaxMagicka      = v.MaxMagicka,
                CurrentFatigue  = v.CurrentFatigue,
                MaxFatigue      = v.MaxFatigue,
                CurrentBreath   = v.CurrentBreath,
                MaxBreath       = v.MaxBreath,
                IsDead          = false,
            });
        }
    }
}
