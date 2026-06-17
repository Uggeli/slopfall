// Stand-ins for DFU engine types the DaggerfallConnect readers reference.
// Each shim is the neutral "feature off / no replacement found" behavior so
// the readers parse vanilla ARENA2 data exactly as classic Daggerfall shipped.

using DaggerfallConnect;
using DaggerfallConnect.Arena2;

namespace UnityEngine
{
    public static class Debug
    {
        public static void Log(object message) => System.Console.WriteLine("[log] " + message);
        public static void LogWarning(object message) => System.Console.WriteLine("[warn] " + message);
        public static void LogError(object message) => System.Console.Error.WriteLine("[error] " + message);
        public static void LogFormat(string format, params object[] args) => System.Console.WriteLine("[log] " + string.Format(format, args));
        public static void LogErrorFormat(string format, params object[] args) => System.Console.Error.WriteLine("[error] " + string.Format(format, args));
    }

    /// FileProxy probes Unity's Resources for embedded files before falling
    /// back to disk; headless always falls back to disk.
    public sealed class TextAsset
    {
        public byte[] bytes => null;
    }

    public static class Resources
    {
        public static T Load<T>(string path) where T : class => null;
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }

        public static implicit operator Color(Color32 c) =>
            new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);

        public static Color32 Lerp(Color32 a, Color32 b, float t)
        {
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return new Color32(
                (byte)(a.r + (b.r - a.r) * t),
                (byte)(a.g + (b.g - a.g) * t),
                (byte)(a.b + (b.b - a.b) * t),
                (byte)(a.a + (b.a - a.a) * t));
        }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color clear => new Color(0f, 0f, 0f, 0f);
        public static Color black => new Color(0f, 0f, 0f, 1f);
        public static Color white => new Color(1f, 1f, 1f, 1f);

        public static implicit operator Color32(Color c) =>
            new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(c.a * 255f));

        public static Color Lerp(Color a, Color b, float t)
        {
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
        }

        // Standard RGB->HSV (only referenced by spectral-emission paths the headless
        // export never calls; present so BaseImageFile compiles).
        public static void RGBToHSV(Color c, out float h, out float s, out float v)
        {
            float max = System.Math.Max(c.r, System.Math.Max(c.g, c.b));
            float min = System.Math.Min(c.r, System.Math.Min(c.g, c.b));
            float d = max - min;
            v = max;
            s = (max <= 0f) ? 0f : d / max;
            if (d <= 0f) { h = 0f; return; }
            if (max == c.r) h = ((c.g - c.b) / d) % 6f;
            else if (max == c.g) h = (c.b - c.r) / d + 2f;
            else h = (c.r - c.g) / d + 4f;
            h /= 6f;
            if (h < 0f) h += 1f;
        }
    }

    public static class Mathf
    {
        public static float Pow(float f, float p) => (float)System.Math.Pow(f, p);
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}

namespace DaggerfallWorkshop
{
    /// Settings surface the readers consult. Headless = vanilla behavior.
    public static class DaggerfallUnity
    {
        public sealed class SettingsShim
        {
            public bool SmallerDungeons => false;
        }

        public static readonly SettingsShim Settings = new SettingsShim();

        public static void LogMessage(string message, bool showInEditor = false)
            => System.Console.WriteLine("[dfu] " + message);
    }

    public static class DaggerfallDungeon
    {
        // Main-story dungeons are never shrunk; with SmallerDungeons off the
        // answer is unused, so a constant false keeps vanilla parsing.
        public static bool IsMainStoryDungeon(int mapId) => false;
    }
}

namespace DaggerfallConnect.Save
{
    /// Classic-save import surface FactionFile.Merge consumes. Headless never
    /// imports classic saves; an empty faction array makes Merge a no-op clone.
    public sealed class SaveVars
    {
        public DaggerfallConnect.Arena2.FactionFile.FactionData[] Factions
            => new DaggerfallConnect.Arena2.FactionFile.FactionData[0];
    }
}

namespace DaggerfallWorkshop.Game.Utility
{
    /// MapsFile only consumes the BankTypes enum (values copied verbatim from
    /// Game/Utility/NameHelper.cs); the name-generation machinery stays out.
    public static class NameHelper
    {
        public enum BankTypes
        {
            Breton,
            Redguard,
            Nord,
            DarkElf,
            HighElf,
            WoodElf,
            Khajiit,
            Imperial,
            Monster1,
            Monster2,
            Monster3,
        }
    }
}

namespace DaggerfallWorkshop.Game.Questing
{
    public enum SiteTypes { None, Town, Dungeon, Building }

    public enum QuestSmallerDungeonsState { None, Enabled, Disabled }

    public struct SiteLink
    {
        public ulong questUID;
    }

    public sealed class Quest
    {
        public QuestSmallerDungeonsState SmallerDungeonsState => QuestSmallerDungeonsState.None;
    }

    /// Quests are deleted from the sim design; no quest ever influences
    /// dungeon layout headlessly.
    public sealed class QuestMachine
    {
        public static readonly QuestMachine Instance = new QuestMachine();

        public SiteLink[] GetSiteLinks(SiteTypes siteType, int mapId) => null;
        public Quest GetQuest(ulong questUID) => null;
    }
}

namespace DaggerfallWorkshop.Utility.AssetInjection
{
    /// Matches the fields BlocksFile copies out of a replacement record.
    public struct BuildingReplacementData
    {
        public ushort FactionId;
        public int BuildingType;
        public byte Quality;
        public ushort NameSeed;
        public DFBlock.RmbSubRecord RmbSubRecord;
        public byte[] AutoMapData;
    }

    /// Mod world-data injection — headless build has no mods, every lookup
    /// reports "no replacement" so vanilla file data is used untouched.
    public static class WorldDataReplacement
    {
        public static string GetNewDFBlockName(int block) => null;
        public static int GetNewDFBlockIndex(string blockName) => -1;
        public static bool GetDFBlockReplacementData(int block, string blockName, out DFBlock dfBlock)
        {
            dfBlock = new DFBlock();
            return false;
        }
        public static bool GetBuildingReplacementData(string blockName, int block, int recordIndex, out BuildingReplacementData buildingData)
        {
            buildingData = new BuildingReplacementData();
            return false;
        }
        public static void ApplyBuildingReplacementAutoMapData(BuildingReplacementData buildingData, ref byte[] autoMapData) { }
        public static void GetDFRegionAdditionalLocationData(int region, ref DFRegion dfRegion) { }
        public static bool GetDFLocationReplacementData(int region, int location, out DFLocation dfLocation)
        {
            dfLocation = new DFLocation();
            return false;
        }
    }
}
