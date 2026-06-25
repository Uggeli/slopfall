using System;
using Xunit;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Memory;

namespace Sim.MemoryTests
{
    public class AtomCatalogTests
    {
        // Completeness: every non-None AtomName has exactly one catalog entry (no holes).
        [Fact]
        public void Catalog_HasEntry_ForEveryAtomName()
        {
            foreach (AtomName n in Enum.GetValues(typeof(AtomName)))
            {
                if (n == AtomName.None) continue;
                Assert.True(AtomCatalog.IsDefined(n), "no catalog entry for AtomName." + n);
            }
        }

        // Per-family Category / Salience / Shareable, transcribed from the old band scheme.
        [Theory]
        [InlineData(AtomName.Civilian,       AtomCategory.Kind,            (byte)160, MemoryFlags.None,     false, true)]
        [InlineData(AtomName.Keeper,         AtomCategory.Role,            (byte)160, MemoryFlags.None,     false, true)]
        [InlineData(AtomName.RaceBreton,     AtomCategory.Race,            (byte)160, MemoryFlags.None,     false, true)]
        [InlineData(AtomName.ActIdle,        AtomCategory.Activity,        (byte)160, MemoryFlags.None,     false, false)]
        [InlineData(AtomName.SomaticHunger,  AtomCategory.Somatic,         (byte)160, MemoryFlags.None,     false, false)]
        [InlineData(AtomName.PlaceTavern,    AtomCategory.PlaceKind,       (byte)255, MemoryFlags.Innate,   true,  false)]
        [InlineData(AtomName.PlaceProvisions,AtomCategory.PlaceProvisions, (byte)160, MemoryFlags.None,     true,  false)]
        [InlineData(AtomName.PlaceDanger,    AtomCategory.PlaceDanger,     (byte)255, MemoryFlags.Surprise, true,  false)]
        public void Catalog_Entry_HasExpectedFaces(
            AtomName name, AtomCategory cat, byte strength, MemoryFlags flags, bool shareable, bool identity)
        {
            AtomEntry e = AtomCatalog.For(name);
            Assert.Equal(cat, e.Category);
            Assert.Equal(strength, e.Salience.Strength);
            Assert.Equal(flags, e.Salience.Flags);
            Assert.Equal(shareable, e.Shareable);
            Assert.Equal(identity, e.IsIdentity);
        }

        // Tone is reserved (neutral) in Phase A — declared, not filled.
        // All catalog entries share the same AtomTone.Neutral constant; checking one representative atom is sufficient.
        [Fact]
        public void Catalog_Tone_IsNeutral_InPhaseA()
        {
            Assert.Equal(Fixed.Zero, AtomCatalog.For(AtomName.PlaceDanger).Tone.Valence);
            Assert.Equal(Fixed.Zero, AtomCatalog.For(AtomName.PlaceDanger).Tone.Arousal);
        }

        // Reverse resolution: a stamped atom resolves back to its AtomName and entry.
        // (Works whatever ints the helpers currently emit — old bands today, dense after Task 8.)
        [Fact]
        public void For_AtomTypeId_ResolvesThroughTheHelpers()
        {
            AtomTypeId tavern = PlaceAtoms.Kind(BuildingKind.Tavern);
            Assert.Equal(AtomName.PlaceTavern, AtomCatalog.NameOf(tavern));
            Assert.Equal(AtomCategory.PlaceKind, AtomCatalog.For(tavern).Category);

            AtomTypeId hunger = SomaticAtoms.Hunger;
            Assert.Equal(AtomName.SomaticHunger, AtomCatalog.NameOf(hunger));
            Assert.False(AtomCatalog.For(hunger).IsIdentity);
        }

        [Fact]
        public void Catalog_FormAtoms_HaveExpectedToneAndCategory()
        {
            AtomEntry fanged = AtomCatalog.For(AtomName.Fanged);
            Assert.Equal(AtomCategory.Form, fanged.Category);
            Assert.False(fanged.IsIdentity);                                   // not a recognition-signature atom
            Assert.True(fanged.Tone.Valence.ToDouble() < 0.0, "Fanged is aversive");
            Assert.True(fanged.Tone.Arousal.ToDouble() > 0.0, "Fanged is alarming");

            // Fanged is the more aversive weapon than Fast.
            Assert.True(AtomCatalog.For(AtomName.Fanged).Tone.Valence.ToDouble()
                      < AtomCatalog.For(AtomName.Fast).Tone.Valence.ToDouble());

            // Size is a neutral modulator (no tone of its own).
            AtomEntry size = AtomCatalog.For(AtomName.Size);
            Assert.Equal(AtomCategory.Form, size.Category);
            Assert.Equal(Fixed.Zero, size.Tone.Valence);
            Assert.Equal(Fixed.Zero, size.Tone.Arousal);
        }
    }
}
