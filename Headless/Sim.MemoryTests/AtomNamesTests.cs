using System;
using System.Collections.Generic;
using Xunit;
using DaggerfallWorkshop.Sim;
using DaggerfallWorkshop.Sim.Memory;

namespace Sim.MemoryTests
{
    public class AtomNamesTests
    {
        // Every real (non-sentinel) source value maps to a distinct, non-None AtomName.
        [Fact]
        public void From_IsTotalAndInjective_OverEverySourceEnum()
        {
            var seen = new HashSet<AtomName>();

            void Check(AtomName n, string what)
            {
                Assert.True(n != AtomName.None, what + " mapped to AtomName.None");
                Assert.True(seen.Add(n), what + " collided with another atom (" + n + ")");
            }

            foreach (EntityKind k in Enum.GetValues(typeof(EntityKind)))
                if (k != EntityKind.Unknown) Check(AtomNames.From(k), "EntityKind." + k);

            foreach (ResidentRole r in Enum.GetValues(typeof(ResidentRole)))
                Check(AtomNames.From(r), "ResidentRole." + r);

            foreach (int race in AtomNames.RaceRoster)
                Check(AtomNames.FromRace(race), "Race " + race);

            foreach (ActivityKind a in Enum.GetValues(typeof(ActivityKind)))
                if (a != ActivityKind.None) Check(AtomNames.From(a), "ActivityKind." + a);

            foreach (BuildingKind b in Enum.GetValues(typeof(BuildingKind)))
                if (b != BuildingKind.None) Check(AtomNames.From(b), "BuildingKind." + b);
        }

        // Sentinels are non-perceivable: they map to None, not a real atom.
        [Fact]
        public void From_Sentinels_MapToNone()
        {
            Assert.Equal(AtomName.None, AtomNames.From(EntityKind.Unknown));
            Assert.Equal(AtomName.None, AtomNames.From(ActivityKind.None));
            Assert.Equal(AtomName.None, AtomNames.From(BuildingKind.None));
            Assert.Equal(AtomName.None, AtomNames.FromRace(-1));
            Assert.Equal(AtomName.None, AtomNames.FromRace(999));
        }

        // ToId is the free conversion: (int)AtomName IS the AtomTypeId payload.
        [Fact]
        public void ToId_RoundTrips_ThroughAtomTypeIdValue()
        {
            AtomTypeId id = AtomName.Civilian.ToId();
            Assert.Equal((int)AtomName.Civilian, id.Value);
            Assert.Equal(AtomName.Civilian, (AtomName)id.Value);
            Assert.True(AtomName.None.ToId().IsNone);
        }
    }
}
