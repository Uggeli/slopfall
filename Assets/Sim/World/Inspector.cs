using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    public sealed class RelationDetail
    {
        public int OtherId;
        public string OtherName;
        public double Familiarity;
        public double Regard;
        public bool Friend;
    }

    public sealed class MemoryDetail
    {
        public string Kind;
        public int OtherId;
        public string OtherName;
        public int Building;
        public long Tick;
    }

    public sealed class EntityDetail
    {
        public int Id;
        public string Name;
        public string Role;
        public string Activity;
        public string Phase;
        public float X, Z;
        public double Hunger, Energy, Social, Coin;
        public double Money;        // actual coin, not the deficit
        public int HomeBuilding = -1;
        public string HomeKind;
        public int HomeQuality;
        public List<RelationDetail> Relations = new List<RelationDetail>();
        public List<MemoryDetail> Memories = new List<MemoryDetail>();
    }

    public sealed class OccupantDetail
    {
        public int Id;
        public string Name;
        public string Role;
        public string Activity;
        public bool Present;        // currently Doing something at this building
    }

    public sealed class BuildingDetail
    {
        public int Index;
        public string Kind;
        public int Quality;
        public int FactionId;
        public float X, Z;
        public List<OccupantDetail> People = new List<OccupantDetail>();
    }

    /// Builds the who-is-this-dude payload any client's inspector shows:
    /// identity, current activity, need bars, home, warmest relations, recent
    /// memories. Pure registry reads — safe from any thread.
    public static class Inspector
    {
        /// Ownership made visible: a building plus everyone whose Residency
        /// points at it (and whether they're inside right now).
        public static BuildingDetail InspectBuilding(SimulationContext ctx, int buildingIndex)
        {
            if (!ctx.Buildings.TryGet(buildingIndex, out var row)) return null;

            var detail = new BuildingDetail
            {
                Index = buildingIndex,
                Kind = row.Kind.ToString(),
                Quality = row.Quality,
                FactionId = row.FactionId,
                X = row.X,
                Z = row.Z,
            };

            foreach (var kv in ctx.Residency.All)
            {
                if (kv.Value.BuildingIndex != buildingIndex) continue;
                ctx.Identity.TryGet(kv.Key, out var identity);
                bool present = ctx.Behavior.TryGet(kv.Key, out var behavior)
                    && behavior.Phase == ActivityPhase.Doing
                    && behavior.TargetBuilding == buildingIndex;
                detail.People.Add(new OccupantDetail
                {
                    Id = kv.Key.Value,
                    Name = identity != null ? identity.Name : "?",
                    Role = kv.Value.Role.ToString(),
                    Activity = behavior != null ? behavior.Activity.ToString() : "?",
                    Present = present,
                });
            }
            detail.People.Sort((a, b) => a.Id.CompareTo(b.Id));
            return detail;
        }

        public static EntityDetail Inspect(SimulationContext ctx, EntityId id, int maxRelations = 5, int maxMemories = 8)
        {
            if (!ctx.Identity.TryGet(id, out var identity)) return null;

            var detail = new EntityDetail
            {
                Id = id.Value,
                Name = identity.Name,
            };

            if (ctx.Position.TryGet(id, out var pos)) { detail.X = pos.X; detail.Z = pos.Z; }

            if (ctx.Behavior.TryGet(id, out var behavior))
            {
                detail.Activity = behavior.Activity.ToString();
                detail.Phase = behavior.Phase.ToString();
            }

            if (ctx.Needs.TryGet(id, out var needs))
            {
                detail.Hunger = needs.V[NeedAxis.Hunger];
                detail.Energy = needs.V[NeedAxis.EnergyDef];
                detail.Social = needs.V[NeedAxis.SocialDef];
                detail.Coin = needs.V[NeedAxis.CoinDef];
            }
            detail.Money = ctx.Coin.Get(id);

            if (ctx.Residency.TryGet(id, out var residency))
            {
                detail.Role = residency.Role.ToString();
                detail.HomeBuilding = residency.BuildingIndex;
                if (ctx.Buildings.TryGet(residency.BuildingIndex, out var home))
                {
                    detail.HomeKind = home.Kind.ToString();
                    detail.HomeQuality = home.Quality;
                }
            }

            if (ctx.Relations.TryGet(id, out var relations))
            {
                var sorted = new List<KeyValuePair<EntityId, RelationData>>(relations.Of);
                sorted.Sort((a, b) =>
                {
                    int cmp = b.Value.Familiarity.CompareTo(a.Value.Familiarity);
                    return cmp != 0 ? cmp : a.Key.Value.CompareTo(b.Key.Value);
                });
                for (int i = 0; i < sorted.Count && i < maxRelations; i++)
                {
                    ctx.Identity.TryGet(sorted[i].Key, out var other);
                    detail.Relations.Add(new RelationDetail
                    {
                        OtherId = sorted[i].Key.Value,
                        OtherName = other != null ? other.Name : "?",
                        Familiarity = sorted[i].Value.Familiarity,
                        Regard = sorted[i].Value.Regard,
                        Friend = sorted[i].Value.FriendAnnounced,
                    });
                }
            }

            if (ctx.Memory.TryGet(id, out var memory))
            {
                for (int i = 0; i < memory.Entries.Count && i < maxMemories; i++)
                {
                    var e = memory.Entries[i];
                    ctx.Identity.TryGet(e.Other, out var other);
                    detail.Memories.Add(new MemoryDetail
                    {
                        Kind = e.Kind.ToString(),
                        OtherId = e.Other.Value,
                        OtherName = other != null ? other.Name : "?",
                        Building = e.Building,
                        Tick = e.Tick,
                    });
                }
            }

            return detail;
        }
    }
}
