using DaggerfallWorkshop.Sim;
using Xunit;

namespace Sim.Tests
{
    /// Items v1 — things EXIST and can change hands. Pure unit tests: registry
    /// mechanics, the subjective-value model, and the universal Take/Drop verbs +
    /// criminal guilt. Clock-independent (no ticking), so unaffected by the
    /// in-flight time-model work.
    public class ItemTests
    {
        // --- Registry ---

        [Fact]
        public void Allocate_IsSequential_NeverNone()
        {
            var reg = new ItemRegistry();
            var a = reg.Allocate();
            var b = reg.Allocate();
            Assert.False(a.IsNone);
            Assert.NotEqual(a, b);
            Assert.Equal(a.Value + 1, b.Value);
        }

        [Fact]
        public void Set_TryGet_Remove_RoundTrips()
        {
            var reg = new ItemRegistry();
            var id = reg.Allocate();
            var data = new ItemData
            {
                Name = "test ring", Valuable = 0.5, Owner = new EntityId(7),
                LocationKind = ItemLocationKind.InBuilding, Building = 3,
            };
            reg.Set(id, data);
            Assert.True(reg.TryGet(id, out var got));
            Assert.Same(data, got);                 // whole-row reference, like the other registries
            Assert.Equal(1, reg.Count);
            reg.Remove(id);
            Assert.False(reg.TryGet(id, out _));
            Assert.Equal(0, reg.Count);
        }

        [Fact]
        public void Location_AndOwner_AreSeparateFields()
        {
            // The crux of "stolen": held by one agent while owned by another — a
            // state the fungible Good model cannot represent.
            var it = new ItemData
            {
                Owner = new EntityId(10),
                LocationKind = ItemLocationKind.CarriedBy,
                Holder = new EntityId(20),
            };
            Assert.NotEqual(it.Owner, it.Holder);
            Assert.Equal(ItemLocationKind.CarriedBy, it.LocationKind);
        }

        [Fact]
        public void Inventory_QueriesByHolder_AndBuilding_SortedById()
        {
            var reg = new ItemRegistry();
            var alice = new EntityId(1);
            var bob = new EntityId(2);

            var i1 = reg.Allocate(); reg.Set(i1, new ItemData { Owner = alice, LocationKind = ItemLocationKind.CarriedBy, Holder = alice });
            var i2 = reg.Allocate(); reg.Set(i2, new ItemData { Owner = bob, LocationKind = ItemLocationKind.CarriedBy, Holder = alice });   // alice carries bob's (held ≠ owned)
            var i3 = reg.Allocate(); reg.Set(i3, new ItemData { Owner = bob, LocationKind = ItemLocationKind.InBuilding, Building = 7 });

            var carried = new System.Collections.Generic.List<ItemId>();
            reg.CarriedBy(alice, carried);
            Assert.Equal(new[] { i1, i2 }, carried);    // everything alice holds, owner-agnostic, id-sorted
            reg.CarriedBy(bob, carried);
            Assert.Empty(carried);

            var inB = new System.Collections.Generic.List<ItemId>();
            reg.InBuilding(7, inB);
            Assert.Equal(new[] { i3 }, inB);
        }

        // --- Subjective value: what makes a thing matter to an entity ---

        [Fact]
        public void Value_Market_IsTheValuableAtom()
        {
            Assert.Equal(0.7, ItemValue.Market(new ItemData { Valuable = 0.7 }), 9);
        }

        [Fact]
        public void Value_Utility_RisesWithTheNeedItServes()
        {
            var loaf = new ItemData { Edible = 0.5 };
            var hungry = new double[NeedAxis.Count]; hungry[NeedAxis.Hunger] = 0.9;
            var sated = new double[NeedAxis.Count]; sated[NeedAxis.Hunger] = 0.1;
            Assert.True(ItemValue.Utility(loaf, hungry) > ItemValue.Utility(loaf, sated));
            // A non-edible has no utility regardless of hunger.
            Assert.Equal(0, ItemValue.Utility(new ItemData { Valuable = 1.0 }, hungry), 9);
        }

        [Fact]
        public void Value_Provenance_OnlyOwnerSinceOrigin_GrowsWithTimeHeld()
        {
            var owner = new EntityId(5);
            var thief = new EntityId(9);
            var keepsake = new ItemData { Valuable = 0.1, Owner = owner, OriginOwner = owner };

            Assert.True(ItemValue.Provenance(keepsake, owner, 400) > 0);          // owner, long held: a bond
            Assert.Equal(0, ItemValue.Provenance(keepsake, thief, 400), 9);       // a stranger feels none
            Assert.True(ItemValue.Provenance(keepsake, owner, 400)
                      > ItemValue.Provenance(keepsake, owner, 5));                // grows with time held
            // Owned but acquired (not the origin owner) → no sentimental bond yet.
            Assert.Equal(0, ItemValue.Provenance(new ItemData { Valuable = 0.1, Owner = owner, OriginOwner = thief }, owner, 400), 9);
        }

        [Fact]
        public void Value_To_VictimValuesItAboveThief_TheTheftAsymmetry()
        {
            var owner = new EntityId(5);
            var thief = new EntityId(9);
            var needs = new double[NeedAxis.Count];   // nobody hungry → utility out of it
            var heirloom = new ItemData { Valuable = 0.3, Owner = owner, OriginOwner = owner };

            double toOwner = ItemValue.To(heirloom, owner, needs, 400);
            double toThief = ItemValue.To(heirloom, thief, needs, 400);
            Assert.True(toOwner > toThief, "owner values the kept thing above its market price");
            Assert.Equal(ItemValue.Market(heirloom), toThief, 9);   // to the thief it's just resale value
        }

        // --- Universal verbs: Take / Drop / criminal guilt ---

        [Fact]
        public void Take_MovesLocation_NotOwnership_AndFlagsTheft()
        {
            var owner = new EntityId(3);
            var thief = new EntityId(8);
            var item = new ItemData { Owner = owner, LocationKind = ItemLocationKind.InBuilding, Building = 12 };

            bool theft = ItemOps.Take(item, thief);
            Assert.True(theft);                                   // owner ≠ taker
            Assert.Equal(ItemLocationKind.CarriedBy, item.LocationKind);
            Assert.Equal(thief, item.Holder);                     // held by the thief
            Assert.Equal(owner, item.Owner);                      // still owned by the victim
        }

        [Fact]
        public void Take_OfOwnThing_OrUnowned_IsNotTheft()
        {
            var a = new EntityId(3);
            Assert.False(ItemOps.Take(new ItemData { Owner = a }, a));            // picking up your own thing
            Assert.False(ItemOps.Take(new ItemData { Owner = EntityId.None }, a)); // nobody's → not theft
        }

        [Fact]
        public void Drop_PutsItDown_ClearsHolder_KeepsOwner()
        {
            var owner = new EntityId(3);
            var item = new ItemData { Owner = owner, LocationKind = ItemLocationKind.CarriedBy, Holder = owner };

            ItemOps.Drop(item, building: -1, x: 5f, z: 7f);
            Assert.Equal(ItemLocationKind.OnGround, item.LocationKind);
            Assert.True(item.Holder.IsNone);
            Assert.Equal(owner, item.Owner);
            Assert.Equal(5f, item.X); Assert.Equal(7f, item.Z);

            ItemOps.Drop(item, building: 9, x: 0, z: 0);
            Assert.Equal(ItemLocationKind.InBuilding, item.LocationKind);
            Assert.Equal(9, item.Building);
        }

        [Fact]
        public void ChargeCriminalGuilt_RaisesStealQualm_ClampedAtOne()
        {
            var ctx = new SimulationContext(new EventBus(), new SimulationTime(0.1), new SimRandom(1), new InputBus());
            var thief = new EntityId(4);

            Assert.Equal(0, ctx.Conscience.ChargeFor(thief, ActivityKind.Steal), 9);
            ItemOps.ChargeCriminalGuilt(ctx, thief, ActivityKind.Steal, 0.3);
            Assert.Equal(0.3, ctx.Conscience.ChargeFor(thief, ActivityKind.Steal), 9);
            ItemOps.ChargeCriminalGuilt(ctx, thief, ActivityKind.Steal, 5.0);    // clamps
            Assert.Equal(1.0, ctx.Conscience.ChargeFor(thief, ActivityKind.Steal), 9);
        }

        // --- Individuation: theft turns fungible stock into a discrete carried loaf ---

        [Fact]
        public void Theft_IndividuatesACarriedLoaf_OwnedByKeeper_AndChargesGuilt()
        {
            var h = new SimHarness();
            var keeper = new EntityId(50);
            var thief = new EntityId(60);

            // Inject "one unit lifted off building 3's shelf" directly — exercises the
            // EconomySystem→ItemSystem handoff without the steal decision or the clock.
            h.Ctx.Events.Emit(new ProvisionsTakenEvent { Taker = thief, Owner = keeper, Building = 3, Units = 1.0 });
            h.Step(2);   // Flush → Drain → ItemSystem.ProcessEvents mints

            ItemData loaf = null;
            foreach (var kv in h.Ctx.Items.All) loaf = kv.Value;
            Assert.NotNull(loaf);
            Assert.Equal("loaf of bread", loaf.Name);
            Assert.True(loaf.Edible > 0);
            Assert.Equal(ItemLocationKind.CarriedBy, loaf.LocationKind);
            Assert.Equal(thief, loaf.Holder);      // carried by the thief…
            Assert.Equal(keeper, loaf.Owner);      // …still owned by the keeper: the stolen state
            Assert.True(h.Ctx.Conscience.ChargeFor(thief, ActivityKind.Steal) > 0, "theft charges guilt");
        }

        [Fact]
        public void Theft_AccruesWholeLoaves_FromFractionalTakes()
        {
            var h = new SimHarness();
            var keeper = new EntityId(50);
            var thief = new EntityId(60);
            // A half-unit take isn't a loaf yet; crossing 1.0 mints exactly one.
            h.Ctx.Events.Emit(new ProvisionsTakenEvent { Taker = thief, Owner = keeper, Building = 3, Units = 0.5 });
            h.Step(2);
            Assert.Equal(0, h.Ctx.Items.Count);
            h.Ctx.Events.Emit(new ProvisionsTakenEvent { Taker = thief, Owner = keeper, Building = 3, Units = 0.6 });
            h.Step(2);
            Assert.Equal(1, h.Ctx.Items.Count);
        }

        // --- The emergent chain's later links: eat / store a carried item ---

        [Fact]
        public void Carrying_AnEdible_Affords_EatAndStore()
        {
            var ctx = new SimulationContext(new EventBus(), new SimulationTime(0.1), new SimRandom(1), new InputBus());
            int home = ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.House1 });
            var agent = ctx.Identity.Allocate();
            ctx.Position.Set(agent, 0f, 0f, 0f, 0f);
            ctx.Residency.Set(agent, new ResidencyData { BuildingIndex = home, Role = ResidentRole.Resident });
            ctx.Needs.Set(agent, new NeedsData());
            var loaf = ctx.Items.Allocate();
            ctx.Items.Set(loaf, new ItemData
            {
                Name = "loaf of bread", Edible = 0.5, Owner = agent, OriginOwner = agent,
                LocationKind = ItemLocationKind.CarriedBy, Holder = agent,
            });

            // Holding a loaf surfaces both endings as ads — nothing about the sequence
            // is authored; they're just blocks gated by what's in hand.
            var ads = ActionDiscovery.GatherAds(ctx, agent);
            Assert.Contains(ads, a => a.Verb == ActivityKind.UseItem && a.Item == loaf);
            Assert.Contains(ads, a => a.Verb == ActivityKind.StoreItem && a.Item == loaf);
        }

        [Fact]
        public void Eat_ConsumesTheCarriedLoaf()
        {
            var h = new SimHarness();
            var agent = h.SpawnEntity("Eater");
            var loaf = h.Ctx.Items.Allocate();
            h.Ctx.Items.Set(loaf, new ItemData
            {
                Name = "loaf of bread", Edible = 0.5, Owner = agent, OriginOwner = agent,
                LocationKind = ItemLocationKind.CarriedBy, Holder = agent,
            });
            h.Ctx.Behavior.Set(agent, new BehaviorData
            {
                Activity = ActivityKind.UseItem, Phase = ActivityPhase.Doing,
                TargetBuilding = -1, TargetItem = loaf, RemainingGameMinutes = 100,
            });

            Assert.Equal(1, h.Ctx.Items.Count);
            h.Step(2);
            Assert.Equal(0, h.Ctx.Items.Count);    // eaten → consumed (relief is NeedsSystem's)
        }

        [Fact]
        public void Store_PutsTheCarriedLoaf_IntoTheHome_KeepingOwner()
        {
            var h = new SimHarness();
            var keeper = new EntityId(99);          // the loaf belongs to someone else (stolen)
            var agent = h.SpawnEntity("Stasher");
            int home = h.Ctx.Buildings.Add(new BuildingRow { Kind = BuildingKind.House1 });
            var loaf = h.Ctx.Items.Allocate();
            h.Ctx.Items.Set(loaf, new ItemData
            {
                Name = "loaf of bread", Valuable = 0.1, Owner = keeper, OriginOwner = keeper,
                LocationKind = ItemLocationKind.CarriedBy, Holder = agent,
            });
            h.Ctx.Behavior.Set(agent, new BehaviorData
            {
                Activity = ActivityKind.StoreItem, Phase = ActivityPhase.Doing,
                TargetBuilding = home, TargetItem = loaf, RemainingGameMinutes = 100,
            });

            h.Step(2);
            Assert.True(h.Ctx.Items.TryGet(loaf, out var stored));
            Assert.Equal(ItemLocationKind.InBuilding, stored.LocationKind);
            Assert.Equal(home, stored.Building);
            Assert.True(stored.Holder.IsNone);          // set down — no longer carried
            Assert.Equal(keeper, stored.Owner);         // …but still owned by the keeper (stashed loot)
        }
    }
}
