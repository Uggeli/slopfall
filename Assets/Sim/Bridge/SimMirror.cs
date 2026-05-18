using UnityEngine;
using DaggerfallWorkshop.Game.Entity;

namespace DaggerfallWorkshop.Sim
{
    /// Auto-attached by SimRegistrar to every DaggerfallEntityBehaviour. Mirrors
    /// that entity's identity / position / vitals into the sim registries.
    ///
    /// Phase 1 is one-way: Unity → sim. Nothing in the sim reads these yet; this
    /// is the "make sure registries match live state" validation pass.
    ///
    /// Runs at a very high DefaultExecutionOrder so we mirror values *after*
    /// DFU's own per-frame updates land them.
    [DefaultExecutionOrder(10000)]
    public sealed class SimMirror : MonoBehaviour
    {
        DaggerfallEntityBehaviour _entityBehaviour;
        EntityId _id;
        bool _registered;
        EntityKind _kind;

        public EntityId Id => _id;
        public bool IsRegistered => _registered;

        void Awake()
        {
            _entityBehaviour = GetComponent<DaggerfallEntityBehaviour>();
        }

        void Start()
        {
            TryRegister();
        }

        void TryRegister()
        {
            if (_registered) return;
            var driver = SimDriver.Instance;
            if (driver == null) return;
            if (_entityBehaviour == null || _entityBehaviour.Entity == null) return;

            _id = driver.Context.Identity.Allocate();
            _kind = ResolveKind(_entityBehaviour.EntityType);

            var identity = BuildIdentity(_entityBehaviour, _kind);
            driver.Context.Identity.Set(_id, identity);
            if (_kind == EntityKind.Player)
                driver.Context.Identity.SetPlayer(_id);
            _registered = true;
        }

        void Update()
        {
            if (!_registered) { TryRegister(); return; }
            var driver = SimDriver.Instance;
            if (driver == null) return;
            var entity = _entityBehaviour?.Entity;
            if (entity == null) return;

            var pos = transform.position;
            var yaw = transform.eulerAngles.y;
            driver.Context.Position.Set(_id, pos.x, pos.y, pos.z, yaw);

            driver.Context.Vitals.Set(_id, new VitalsData
            {
                CurrentHealth = entity.CurrentHealth,
                MaxHealth = entity.MaxHealth,
                CurrentMagicka = entity.CurrentMagicka,
                MaxMagicka = entity.MaxMagicka,
                CurrentFatigue = entity.CurrentFatigue,
                MaxFatigue = entity.MaxFatigue,
                CurrentBreath = entity.CurrentBreath,
                MaxBreath = entity.MaxBreath,
                IsDead = entity.CurrentHealth <= 0,
            });
        }

        void OnDestroy()
        {
            if (!_registered) return;
            var driver = SimDriver.Instance;
            if (driver == null) return;
            if (_kind == EntityKind.Player)
                driver.Context.Identity.ClearPlayer(_id);
            driver.Context.Identity.Remove(_id);
            driver.Context.Position.Remove(_id);
            driver.Context.Vitals.Remove(_id);
        }

        static IdentityData BuildIdentity(DaggerfallEntityBehaviour beh, EntityKind kind)
        {
            var entity = beh.Entity;
            int race = -1;
            int career = -1;

            if (entity is PlayerEntity player) race = (int)player.Race;
            if (entity is EnemyEntity enemy) career = enemy.CareerIndex;

            return new IdentityData
            {
                Name = entity.Name ?? string.Empty,
                Kind = kind,
                Race = race,
                Gender = (int)entity.Gender,
                CareerIndex = career,
                Level = entity.Level,
                FactionId = 0,
                Team = (int)entity.Team,
            };
        }

        static EntityKind ResolveKind(EntityTypes t)
        {
            switch (t)
            {
                case EntityTypes.Player: return EntityKind.Player;
                case EntityTypes.EnemyClass: return EntityKind.EnemyClass;
                case EntityTypes.EnemyMonster: return EntityKind.EnemyMonster;
                case EntityTypes.CivilianNPC: return EntityKind.CivilianNPC;
                case EntityTypes.StaticNPC: return EntityKind.StaticNPC;
                default: return EntityKind.Unknown;
            }
        }
    }
}
