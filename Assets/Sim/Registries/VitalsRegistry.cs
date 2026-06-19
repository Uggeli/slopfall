using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    public sealed class VitalsData
    {
        public int CurrentHealth, MaxHealth;
        public int CurrentMagicka, MaxMagicka;
        public int CurrentFatigue, MaxFatigue;
        public int CurrentBreath, MaxBreath;
        public bool IsDead;
    }
}
