using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Primary attributes — fixed order matches DFU's DFCareer.Stats enum:
    /// 0 Strength, 1 Intelligence, 2 Willpower, 3 Agility,
    /// 4 Endurance, 5 Personality, 6 Speed, 7 Luck
    public static class StatIndex
    {
        public const int Strength     = 0;
        public const int Intelligence = 1;
        public const int Willpower    = 2;
        public const int Agility      = 3;
        public const int Endurance    = 4;
        public const int Personality  = 5;
        public const int Speed        = 6;
        public const int Luck         = 7;
        public const int Count        = 8;
    }

    public sealed class StatsData
    {
        /// Base primary attribute values (length 8, see StatIndex).
        public int[] Stats = new int[StatIndex.Count];
        /// Current skill values keyed by skill name (DFU's DFCareer.Skills.ToString()).
        public Dictionary<string, int> Skills = new Dictionary<string, int>();
        /// Skill experience progress keyed by skill name. SkillAdvancementSystem
        /// promotes to the next skill point when this reaches the threshold.
        public Dictionary<string, int> SkillExp = new Dictionary<string, int>();
    }
}
