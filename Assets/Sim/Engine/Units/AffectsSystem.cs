using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim.Engine
{
    // CQRS conversion of DaggerfallWorkshop.Sim.AffectsSystem — Emotion (Atoms drive doc).
    // Turns interactions into directed emotional residuals (gratitude / resentment /
    // affection / aversion toward specific others), held in AffectsRegistry and decaying over
    // time. It is the ONE place an interaction's STAKES become a regard change: it emits the
    // (stakes-scaled) RelationImpulseEvent the dossier owner (SocialSystem) applies.
    //
    // Conversion notes (vs the old ISystem):
    //   * The _granted / _refused / _greets / _dislikes spool lists + Subscribe<> are gone:
    //     each Update reads last tick's HelpGranted/HelpRefused/Greeting/DislikeNearby via
    //     Events.GetEvents<T>().
    //   * ProcessEvents (fold) and Update (decay) collapse into one Update(long tick) with the
    //     SAME ordering the old TickLoop used: FOLD first, then DECAY — both on a per-entity
    //     COPY (copy-on-write) of the settled AffectsData. The registry is the sole applier;
    //     every touched entity gets a whole-value AffectsSetIntent.
    //   * RelationImpulseEvent emissions are unchanged (still a signal the dossier consumes).
    //
    // Domain constants and math preserved EXACTLY.
    public sealed class AffectsSystem : SimSystem
    {
        // FROZEN placeholders. Stakes asymmetry: help-when-destitute is high-stakes
        // (strong, lasting gratitude); a routine begging refusal is LOW-stakes (mild).
        const double GratitudeAcute = 0.3, GrantRegard = 0.3, GrantFam = 0.05;
        const double GaveHelpRegard = 0.05, GaveHelpFam = 0.05;
        const double ResentAcute = 0.1, RefusedRegard = -0.05, RefusedFam = 0.02;
        const double RefuserRegard = -0.02, RefuserFam = 0.02;
        const double GreetRegard = 0.02, GreetFam = 0.01, AffectionAcute = 0.05;
        const double DislikeAcute = 0.1;
        const double AffectDecayPerHour = 0.5;     // acute feelings fade within hours
        const double Epsilon = 0.01;

        readonly WorldClockRegistry _clock;
        readonly AffectsRegistry _affects;

        public AffectsSystem(EventBus events, WorldClockRegistry clock, AffectsRegistry affects)
            : base(events)
        {
            _clock = clock;
            _affects = affects;
        }

        public override void Update(long tick)
        {
            var clock = _clock.Current;
            if (clock.Year == 0) return;

            // Working copies of the settled per-entity AffectsData, built lazily as folds /
            // decay touch each entity. Copy-on-write: we never mutate the registry's objects.
            var work = new Dictionary<EntityId, AffectsData>();

            // --- FOLD (old ProcessEvents): turn last tick's interactions into affects + impulses ---
            var granted = Events.GetEvents<HelpGrantedEvent>();
            for (int i = 0; i < granted.Length; i++)
            {
                var e = granted[i];
                Feel(work, e.Asker, e.Giver, AffectKind.Gratitude, GratitudeAcute);
                Impulse(e.Asker, e.Giver, GrantRegard, GrantFam, MemoryKind.ReceivedHelp);
                Impulse(e.Giver, e.Asker, GaveHelpRegard, GaveHelpFam, MemoryKind.GaveHelp);
            }

            var refused = Events.GetEvents<HelpRefusedEvent>();
            for (int i = 0; i < refused.Length; i++)
            {
                var e = refused[i];
                Feel(work, e.Asker, e.Refuser, AffectKind.Resentment, ResentAcute);
                Impulse(e.Asker, e.Refuser, RefusedRegard, RefusedFam, MemoryKind.WasRefused);
                Impulse(e.Refuser, e.Asker, RefuserRegard, RefuserFam, MemoryKind.RefusedToHelp);
            }

            var greets = Events.GetEvents<GreetingEvent>();
            for (int i = 0; i < greets.Length; i++)
            {
                var e = greets[i];
                Feel(work, e.A, e.B, AffectKind.Affection, AffectionAcute);
                Feel(work, e.B, e.A, AffectKind.Affection, AffectionAcute);
                Impulse(e.A, e.B, GreetRegard, GreetFam, MemoryKind.Met, record: false);
                Impulse(e.B, e.A, GreetRegard, GreetFam, MemoryKind.Met, record: false);
            }

            var dislikes = Events.GetEvents<DislikeNearbyEvent>();
            for (int i = 0; i < dislikes.Length; i++)
                Feel(work, dislikes[i].Who, dislikes[i].Whom, AffectKind.Aversion, DislikeAcute);   // acute only; no regard change

            // --- DECAY (old Update): erode every active affect toward 0 by game-time elapsed ---
            double gameHours = clock.DeltaGameSeconds / 3600.0;
            if (gameHours > 0)
            {
                // Deterministic key order (matches the old _keys.Sort by id).
                var keys = new List<EntityId>();
                foreach (var kv in _affects.All) keys.Add(kv.Key);
                keys.Sort((a, b) => a.Value.CompareTo(b.Value));
                for (int k = 0; k < keys.Count; k++)
                {
                    var data = WorkingCopy(work, keys[k]);   // ensures every decaying entity is emitted
                    if (data == null) continue;
                    var list = data.Active;
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        var a = list[i];
                        double next = Decay.TowardBaseline(a.Intensity, 0.0, AffectDecayPerHour, gameHours);
                        if (next < Epsilon) list.RemoveAt(i);
                        else { a.Intensity = next; list[i] = a; }
                    }
                }
            }

            // --- emit whole-value set intents for every entity touched this tick ---
            foreach (var kv in work)
                Events.Publish(new AffectsSetIntent { Id = kv.Key, Data = kv.Value });
        }

        /// Add an acute feeling toward target, into the entity's COPY-on-write AffectsData
        /// (folding into an existing same-(target,kind) affect). Mirrors the old Feel exactly.
        void Feel(Dictionary<EntityId, AffectsData> work, EntityId self, EntityId target, AffectKind kind, double add)
        {
            var data = WorkingCopyOrNew(work, self);
            for (int i = 0; i < data.Active.Count; i++)
                if (data.Active[i].Target == target && data.Active[i].Kind == kind)
                {
                    var a = data.Active[i];
                    a.Intensity += add;
                    data.Active[i] = a;
                    return;
                }
            data.Active.Add(new Affect { Target = target, Kind = kind, Intensity = add });
        }

        void Impulse(EntityId who, EntityId other, double regard, double fam, MemoryKind mem, bool record = true)
        {
            Events.Publish(new RelationImpulseEvent
            {
                Who = who, Other = other, RegardDelta = regard, FamiliarityDelta = fam,
                Memory = mem, RecordMemory = record,
            });
        }

        // --- copy-on-write helpers ---

        /// The working copy for `id`, materialized from the settled registry value if not yet
        /// touched this tick. Returns null only if the entity has no settled affects (so decay
        /// has nothing to do and emits nothing for it).
        AffectsData WorkingCopy(Dictionary<EntityId, AffectsData> work, EntityId id)
        {
            if (work.TryGetValue(id, out var existing)) return existing;
            if (!_affects.TryGet(id, out var settled)) return null;
            var copy = Clone(settled);
            work[id] = copy;
            return copy;
        }

        /// Like WorkingCopy but never null — creates a fresh AffectsData for a brand-new
        /// emotional store (the old `Feel` did `new AffectsData()` + Set when absent).
        AffectsData WorkingCopyOrNew(Dictionary<EntityId, AffectsData> work, EntityId id)
        {
            if (work.TryGetValue(id, out var existing)) return existing;
            var copy = _affects.TryGet(id, out var settled) ? Clone(settled) : new AffectsData();
            work[id] = copy;
            return copy;
        }

        static AffectsData Clone(AffectsData src)
        {
            var c = new AffectsData();
            c.Active.AddRange(src.Active);      // Affect is a struct — shallow copy is a value copy
            return c;
        }
    }
}
