// Minimal stand-ins so pure-logic DFU sources (e.g. DaggerfallDateTime.cs)
// compile outside Unity. Only add members that those sources actually call —
// anything more belongs in a real port, not a shim.

namespace UnityEngine
{
    public static class Debug
    {
        public static void Log(object message) => System.Console.WriteLine("[log] " + message);
        public static void LogWarning(object message) => System.Console.WriteLine("[warn] " + message);
        public static void LogError(object message) => System.Console.Error.WriteLine("[error] " + message);
    }
}

namespace DaggerfallWorkshop.Game
{
    /// English-only stand-in for DFU's localization manager. List orders match
    /// the Days/Months/BirthSigns/Seasons enums in DaggerfallDateTime.cs.
    public sealed class TextManager
    {
        public static readonly TextManager Instance = new TextManager();

        static readonly string[] DayNames =
            { "Sundas", "Morndas", "Tirdas", "Middas", "Turdas", "Fredas", "Loredas" };
        static readonly string[] MonthNames =
            { "Morning Star", "Sun's Dawn", "First Seed", "Rain's Hand", "Second Seed", "Midyear",
              "Sun's Height", "Last Seed", "Hearthfire", "Frostfall", "Sun's Dusk", "Evening Star" };
        static readonly string[] BirthSignNames =
            { "The Ritual", "The Lover", "The Lord", "The Mage", "The Shadow", "The Steed",
              "The Apprentice", "The Warrior", "The Lady", "The Tower", "The Atronach", "The Thief" };
        static readonly string[] SeasonNames =
            { "Fall", "Spring", "Summer", "Winter" };

        public string GetLocalizedText(string key)
        {
            // Values copied from Assets/StreamingAssets/Text/Master Localization
            // CSV Files/Internal_Strings.csv so headless output matches DFU.
            switch (key)
            {
                case "longDateTimeFormatString": return "{0:00}:{1:00}:{2:00} on {3}, {4}{5} of {6:00}, 3E{7}";
                case "dateTimeFormatString":     return "{0:00}:{1:00}:{2:00} on {3}{4} of {5:00}, 3E{6}";
                case "dateFormatString":         return "{0} the {1}{2} of {3:00}";
                case "midDateTimeFormatString":  return "{0:00}:{1:00}:{2:00} {3:00} {4:00} 3E{5}";
                default:                         return key;
            }
        }

        public string[] GetLocalizedTextList(string key)
        {
            switch (key)
            {
                case "dayNames":       return DayNames;
                case "monthNames":     return MonthNames;
                case "birthSignNames": return BirthSignNames;
                case "seasonNames":    return SeasonNames;
                default:               return new string[0];
            }
        }
    }
}
