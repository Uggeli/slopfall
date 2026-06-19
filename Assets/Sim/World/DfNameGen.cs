using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Pure-C# port of DaggerfallWorkshop.Game.Utility.NameHelper's generation logic,
    /// usable from the headless sim (no UnityEngine, no Resources, no DFRandom). The
    /// name-bank DATA (Assets/Resources/NameGen.txt) is parsed by the host layer and
    /// handed in already-split — this class is pure logic so it dual-compiles under
    /// Unity too. Determinism comes from a caller-supplied seed (a per-entity /
    /// per-family hash), NOT a shared RNG stream — so naming never perturbs the
    /// spawn RNG that soak runs depend on.
    ///
    /// A bank is sets[setIndex][partIndex]. The fragment layout per race matches
    /// NameHelper: male first = sets 0+1, female first = 2+3, surname = 4+5; Nord
    /// surname = 0+1+"sen"; Redguard is a single name (0+1+2 + a 4th part) with no
    /// surname.
    public sealed class DfNameGen
    {
        // Bank indices — order matches NameHelper.BankTypes.
        public const int Breton = 0, Redguard = 1, Nord = 2, DarkElf = 3,
                         HighElf = 4, WoodElf = 5, Khajiit = 6, Imperial = 7;

        readonly Dictionary<int, string[][]> _banks;

        public DfNameGen(Dictionary<int, string[][]> banks) { _banks = banks; }

        /// First (given) name for a bank+gender, seeded for determinism. For Redguard
        /// this is the whole name (they carry no surname).
        public string FirstName(int bank, int gender, uint seed)
        {
            if (_banks == null || !_banks.TryGetValue(bank, out var sets)) return string.Empty;
            uint s = seed == 0 ? 0x9E3779B9u : seed;
            if (bank == Redguard) return RedguardName(sets, gender, ref s);
            int a = gender == 0 ? 0 : 2;   // 0 = male, 1 = female
            int b = gender == 0 ? 1 : 3;
            return Pick(Set(sets, a), ref s) + Pick(Set(sets, b), ref s);
        }

        /// Family (sur)name for a bank, seeded so a household shares one. Empty for
        /// Redguard (single-name culture).
        public string Surname(int bank, uint seed)
        {
            if (_banks == null || !_banks.TryGetValue(bank, out var sets)) return string.Empty;
            if (bank == Redguard) return string.Empty;
            uint s = seed == 0 ? 0x9E3779B9u : seed;
            if (bank == Nord) return Pick(Set(sets, 0), ref s) + Pick(Set(sets, 1), ref s) + "sen";
            return Pick(Set(sets, 4), ref s) + Pick(Set(sets, 5), ref s);
        }

        // Redguard: 0+1+2, then +3 (75% of males) / +4 (females). One draw per part.
        static string RedguardName(string[][] sets, int gender, ref uint s)
        {
            string name = Pick(Set(sets, 0), ref s) + Pick(Set(sets, 1), ref s) + Pick(Set(sets, 2), ref s);
            if (gender != 0) name += Pick(Set(sets, 4), ref s);
            else if (Next(ref s) % 100 < 75) name += Pick(Set(sets, 3), ref s);
            return name;
        }

        static string[] Set(string[][] sets, int i) => (sets != null && i < sets.Length) ? sets[i] : null;

        static string Pick(string[] parts, ref uint s)
            => (parts == null || parts.Length == 0) ? string.Empty : parts[Next(ref s) % (uint)parts.Length];

        // xorshift32 — small, fast, deterministic; quality is irrelevant for fragment picks.
        static uint Next(ref uint s) { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }

        // --- race mapping (FactionFile.FactionRaces ints → bank / Races enum) ---
        // FactionRaces: None=-1, Nord=0, Khajiit=1, Redguard=2, Breton=3, Argonian=4,
        //               WoodElf=5, HighElf=6, DarkElf=7.

        /// The name bank for a location's climate race. Argonian has no bank (DFU uses
        /// Imperial in its place); anything unmapped falls back to Breton.
        public static int BankFromFactionRace(int factionRace)
        {
            switch (factionRace)
            {
                case 0: return Nord;
                case 1: return Khajiit;
                case 2: return Redguard;
                case 3: return Breton;
                case 4: return Imperial;   // Argonian → Imperial names (NameHelper convention)
                case 5: return WoodElf;
                case 6: return HighElf;
                case 7: return DarkElf;
                default: return Breton;
            }
        }

        /// The Races enum value (EntityEnums.Races) for IdentityData.Race.
        /// Races: Breton=1, Redguard=2, Nord=3, DarkElf=4, HighElf=5, WoodElf=6, Khajiit=7, Argonian=8.
        public static int RaceFromFactionRace(int factionRace)
        {
            switch (factionRace)
            {
                case 0: return 3;   // Nord
                case 1: return 7;   // Khajiit
                case 2: return 2;   // Redguard
                case 3: return 1;   // Breton
                case 4: return 8;   // Argonian
                case 5: return 6;   // WoodElf
                case 6: return 5;   // HighElf
                case 7: return 4;   // DarkElf
                default: return 1;  // Breton
            }
        }

        /// Bank index for a NameGen.txt top-level key ("Breton", "Nord", …). Returns
        /// -1 for keys with no playable bank (the Monster* banks), so the host skips them.
        public static int BankIndexFromName(string name)
        {
            switch (name)
            {
                case "Breton": return Breton;
                case "Redguard": return Redguard;
                case "Nord": return Nord;
                case "DarkElf": return DarkElf;
                case "HighElf": return HighElf;
                case "WoodElf": return WoodElf;
                case "Khajiit": return Khajiit;
                case "Imperial": return Imperial;
                default: return -1;
            }
        }
    }
}
