using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>
    /// Witness-learned danger: when a creature kills near a building, every agent that
    /// currently senses the victim or the killer stamps DangerHere onto that building in
    /// its PLACES memory. The dead don't learn — witnesses do. Reads the prior tick's
    /// DeathEvent (uniform one-tick latency) plus the settled sensed/position state, and
    /// publishes PlaceObserveIntent the AgentMemoryRegistry applies next tick. Danger
    /// fades via sleep consolidation if the place stops being deadly.
    ///
    /// <para>The witness set (agents who currently sense the victim or killer) is the reusable
    /// primitive a later justice layer can consume to report crimes — a witnessed kill/theft is
    /// the same observation, routed to bounties/guards instead of (or as well as) place memory.</para>
    /// </summary>
    public sealed class PlaceDangerSystem : SimSystem
    {
        readonly SensedRegistry _sensed;
        readonly CreatureRegistry _creatures;
        readonly PositionRegistry _position;
        readonly BuildingRegistry _buildings;

        static readonly Fixed Severity = Fixed.One;   // a witnessed kill = max danger; consolidation fades it

        public PlaceDangerSystem(EventBus events, SensedRegistry sensed, CreatureRegistry creatures,
            PositionRegistry position, BuildingRegistry buildings) : base(events)
        {
            _sensed = sensed; _creatures = creatures; _position = position; _buildings = buildings;
        }

        public override void Update(long tick)
        {
            var deaths = Events.GetEvents<DeathEvent>();
            for (int i = 0; i < deaths.Length; i++)
            {
                var d = deaths[i];
                if (!_creatures.Contains(d.Killer)) continue;       // only creature kills mark a place dangerous
                int building = NearestBuilding(d.Entity);
                if (building < 0) continue;

                // Witnesses: agents whose current sensed set includes the victim or the killer.
                foreach (var kv in _sensed.All)
                {
                    if (kv.Key == d.Entity) continue;               // the dead don't learn
                    var seen = kv.Value;
                    if (seen == null || (!seen.Contains(d.Entity) && !seen.Contains(d.Killer))) continue;
                    Events.Publish(new PlaceObserveIntent
                    { Agent = kv.Key, Building = building, Atom = PlaceAtoms.Danger, Value = Severity });

                    // A witness also SHOUTS the danger — bystanders in earshot who didn't see it learn
                    // it second-hand (word of mouth). Phase E: the shout also names the KIND that attacked
                    // (the killer is a creature — _creatures.Contains(d.Killer) above — so {EnemyMonster}),
                    // so hearers deepen their monster-belief, not just the place-danger.
                    Events.Publish(new Utterance
                    {
                        Speaker = kv.Key, Audience = EntityId.None, Channel = CommChannel.Shout,
                        Act = SpeechAct.Inform, SubjectBuilding = building, Confidence = Severity,
                        Content = AtomBag.Create(new[] { new Atom(PlaceAtoms.Danger, Severity) }),
                        SubjectKind = AtomBag.Create(new[] { new Atom(PerceivableAtoms.Kind(EntityKind.EnemyMonster), Fixed.One) })
                    });
                }
            }
        }

        /// <summary>Nearest known building to a position (linear scan — deaths are rare). -1 if the
        /// victim has no position or there are no buildings.</summary>
        int NearestBuilding(EntityId victim)
        {
            if (!_position.TryGet(victim, out var p) || p == null) return -1;
            int best = -1;
            float bestSq = float.MaxValue;
            foreach (var kv in _buildings.All)
            {
                var row = kv.Value;
                if (row == null || row.Kind == BuildingKind.None) continue;
                float dx = row.X - p.X, dz = row.Z - p.Z;
                float sq = dx * dx + dz * dz;
                if (sq < bestSq) { bestSq = sq; best = kv.Key; }
            }
            return best;
        }
    }
}
