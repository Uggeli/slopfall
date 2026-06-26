using System;
using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>The atom family — the thing the old id-band high-digits used to encode, now a field.</summary>
    public enum AtomCategory
    {
        None,
        Kind, Role, Race,          // identity
        Activity, Somatic,         // transient state
        PlaceKind, PlaceProvisions, PlaceDanger,
        Form,                      // descriptive form atoms (weapons, body) — Phase B
    }

    /// <summary>Reserved affective cell (Phase B fills it). Phase A leaves it neutral. The catalog
    /// has a TONE face and no VERDICT face — there is nowhere on an atom to write "threat."</summary>
    public readonly struct AtomTone
    {
        public readonly Fixed Valence;
        public readonly Fixed Arousal;
        public AtomTone(Fixed valence, Fixed arousal) { Valence = valence; Arousal = arousal; }
        public static readonly AtomTone Neutral = new AtomTone(Fixed.Zero, Fixed.Zero);
    }

    /// <summary>One atom's frozen, per-type metadata.</summary>
    public readonly struct AtomEntry
    {
        public readonly AtomCategory Category;
        public readonly AtomMeta Salience;     // memory encode/decay seed (was MemorySalience.For)
        public readonly bool Shareable;        // gossip-relayable (set per AtomName in the catalog)
        public readonly AtomTone Tone;         // reserved (Phase B)

        public AtomEntry(AtomCategory category, AtomMeta salience, bool shareable, AtomTone tone)
        {
            Category = category; Salience = salience; Shareable = shareable; Tone = tone;
        }

        public bool IsIdentity =>
            Category == AtomCategory.Kind || Category == AtomCategory.Role || Category == AtomCategory.Race;
    }

    /// <summary>
    /// The single frozen source of truth for per-atom metadata, keyed by AtomName. Replaced three
    /// scattered band-checks: MemorySalience's by-category switch, the identity test, and the
    /// gossip-gate. Populated by iterating the From maps so catalog and From stay one source of
    /// truth. For(AtomTypeId) resolves an emitted atom back to its entry via a reverse map.
    /// </summary>
    public static class AtomCatalog
    {
        const byte Ordinary = 160;   // a learned, fade-able fact (was MemorySalience.Ordinary)

        static readonly Dictionary<AtomName, AtomEntry> _entries = BuildEntries();
        static readonly Dictionary<int, AtomName> _byId = BuildReverse();

        static readonly AtomEntry Fallback =
            new AtomEntry(AtomCategory.None, new AtomMeta(Ordinary, MemoryFlags.None), false, AtomTone.Neutral);

        public static bool IsDefined(AtomName name) => _entries.ContainsKey(name);

        public static AtomEntry For(AtomName name) =>
            _entries.TryGetValue(name, out var e) ? e : Fallback;

        public static AtomName NameOf(AtomTypeId atom) =>
            _byId.TryGetValue(atom.Value, out var n) ? n : AtomName.None;

        public static AtomEntry For(AtomTypeId atom) => For(NameOf(atom));

        static Dictionary<AtomName, AtomEntry> BuildEntries()
        {
            var ordinary = new AtomMeta(Ordinary, MemoryFlags.None);
            var innate   = new AtomMeta(255, MemoryFlags.Innate);     // structural, permanent
            var surprise = new AtomMeta(255, MemoryFlags.Surprise);   // survival-grade, resists decay

            var m = new Dictionary<AtomName, AtomEntry>();

            void Put(AtomName n, AtomCategory c, AtomMeta sal, bool share, AtomTone tone = default)
            {
                if (n == AtomName.None) return;                       // sentinels carry no entry
                m[n] = new AtomEntry(c, sal, share, tone);            // default(AtomTone) == Neutral
            }

            foreach (EntityKind k in Enum.GetValues(typeof(EntityKind)))
                Put(AtomNames.From(k), AtomCategory.Kind, ordinary, false);
            foreach (ResidentRole r in Enum.GetValues(typeof(ResidentRole)))
                Put(AtomNames.From(r), AtomCategory.Role, ordinary, false);
            foreach (int race in AtomNames.RaceRoster)
                Put(AtomNames.FromRace(race), AtomCategory.Race, ordinary, false);
            foreach (ActivityKind a in Enum.GetValues(typeof(ActivityKind)))
                Put(AtomNames.From(a), AtomCategory.Activity, ordinary, false);

            Put(AtomName.SomaticHunger, AtomCategory.Somatic, ordinary, false);
            Put(AtomName.SomaticEnergy, AtomCategory.Somatic, ordinary, false);
            Put(AtomName.SomaticFear,   AtomCategory.Somatic, ordinary, false);

            foreach (BuildingKind b in Enum.GetValues(typeof(BuildingKind)))
                Put(AtomNames.From(b), AtomCategory.PlaceKind, innate, true);

            Put(AtomName.PlaceProvisions, AtomCategory.PlaceProvisions, ordinary, true);
            Put(AtomName.PlaceDanger,     AtomCategory.PlaceDanger,     surprise, true);

            // Form atoms (Phase B): weapons carry aversive tone; Size is a neutral modulator (B1).
            Put(AtomName.Fanged, AtomCategory.Form, ordinary, false,
                new AtomTone(Fixed.FromDouble(-0.8), Fixed.FromDouble(0.9)));   // FROZEN placeholders
            Put(AtomName.Fast,   AtomCategory.Form, ordinary, false,
                new AtomTone(Fixed.FromDouble(-0.4), Fixed.FromDouble(0.6)));
            Put(AtomName.Size,   AtomCategory.Form, ordinary, false);           // neutral — modulates weapon cues

            // Appearance atoms (Phase F): verdict-free neutral identity layer — what a thing LOOKS like.
            // Registered as Kind/identity (IsIdentity == true) so they form a signature + category.
            Put(AtomName.Beast,   AtomCategory.Kind, ordinary, false);
            Put(AtomName.Drifter, AtomCategory.Kind, ordinary, false);

            return m;
        }

        static Dictionary<int, AtomName> BuildReverse()
        {
            var m = new Dictionary<int, AtomName>();

            void Add(AtomTypeId id, AtomName n)
            {
                if (id.IsNone || n == AtomName.None) return;
                if (m.ContainsKey(id.Value))
                    throw new InvalidOperationException(
                        "Duplicate atom id " + id.Value + " for " + n + " and " + m[id.Value]);
                m[id.Value] = n;
            }

            foreach (EntityKind k in Enum.GetValues(typeof(EntityKind)))
                if (k != EntityKind.Unknown) Add(PerceivableAtoms.Kind(k), AtomNames.From(k));
            // ResidentRole has no None sentinel — every value is a valid atom.
            foreach (ResidentRole r in Enum.GetValues(typeof(ResidentRole)))
                Add(PerceivableAtoms.Role(r), AtomNames.From(r));
            foreach (int race in AtomNames.RaceRoster)
                Add(PerceivableAtoms.Race(race), AtomNames.FromRace(race));
            foreach (ActivityKind a in Enum.GetValues(typeof(ActivityKind)))
                if (a != ActivityKind.None) Add(PerceivableAtoms.Activity(a), AtomNames.From(a));

            Add(SomaticAtoms.Hunger, AtomName.SomaticHunger);
            Add(SomaticAtoms.Energy, AtomName.SomaticEnergy);
            Add(SomaticAtoms.Fear,   AtomName.SomaticFear);

            foreach (BuildingKind b in Enum.GetValues(typeof(BuildingKind)))
                if (b != BuildingKind.None) Add(PlaceAtoms.Kind(b), AtomNames.From(b));

            Add(PlaceAtoms.Provisions, AtomName.PlaceProvisions);
            Add(PlaceAtoms.Danger,     AtomName.PlaceDanger);

            // Form atoms — direct members (no source-enum helper); stamped via AtomName.X.ToId().
            Add(AtomName.Fanged.ToId(), AtomName.Fanged);
            Add(AtomName.Fast.ToId(),   AtomName.Fast);
            Add(AtomName.Size.ToId(),   AtomName.Size);

            // Appearance atoms (Phase F) — direct members; stamped via AtomName.X.ToId().
            Add(AtomName.Beast.ToId(),   AtomName.Beast);
            Add(AtomName.Drifter.ToId(), AtomName.Drifter);

            return m;
        }
    }
}
