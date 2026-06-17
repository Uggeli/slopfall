using System.Collections.Generic;

namespace DaggerfallWorkshop.Sim
{
    /// Emotion (Atoms drive doc): turns interactions into directed emotional
    /// residuals — gratitude / resentment / affection / aversion toward specific
    /// others — held in AffectsRegistry and decaying over time. It is the ONE
    /// place an interaction's STAKES become a regard change: it emits the
    /// (stakes-scaled) RelationImpulseEvent the dossier owner (SocialSystem)
    /// applies, replacing the literal ±0.x constants that used to live at each
    /// call site. The acute affect also colors SubjectiveSystem.Interpret while it
    /// lasts (emotion-as-controller). See docs/cognitive_substrate_S2_emotion.md.
    public sealed class AffectsSystem : ISystem
    {
        // FROZEN placeholders. Stakes asymmetry: help-when-destitute is high-stakes
        // (strong, lasting gratitude); a routine begging refusal is LOW-stakes
        // (mild) — so broadcast begging no longer mass-sours the town (the L4
        // finding, resolved by the model rather than a tuned grudge).
        const double GratitudeAcute = 0.3, GrantRegard = 0.3, GrantFam = 0.05;
        const double GaveHelpRegard = 0.05, GaveHelpFam = 0.05;
        const double ResentAcute = 0.1, RefusedRegard = -0.05, RefusedFam = 0.02;   // was -0.2 (targeted-plea era)
        const double RefuserRegard = -0.02, RefuserFam = 0.02;
        const double GreetRegard = 0.02, GreetFam = 0.01, AffectionAcute = 0.05;
        const double DislikeAcute = 0.1;
        const double AffectDecayPerHour = 0.5;     // acute feelings fade within hours
        const double Epsilon = 0.01;

        SimulationContext _ctx;
        readonly List<HelpGrantedEvent> _granted = new List<HelpGrantedEvent>();
        readonly List<HelpRefusedEvent> _refused = new List<HelpRefusedEvent>();
        readonly List<GreetingEvent> _greets = new List<GreetingEvent>();
        readonly List<DislikeNearbyEvent> _dislikes = new List<DislikeNearbyEvent>();
        readonly List<EntityId> _keys = new List<EntityId>();

        public void Init(SimulationContext ctx)
        {
            _ctx = ctx;
            ctx.Events.Subscribe<HelpGrantedEvent>(e => _granted.Add(e));
            ctx.Events.Subscribe<HelpRefusedEvent>(e => _refused.Add(e));
            ctx.Events.Subscribe<GreetingEvent>(e => _greets.Add(e));
            ctx.Events.Subscribe<DislikeNearbyEvent>(e => _dislikes.Add(e));
        }

        public void ProcessEvents()
        {
            for (int i = 0; i < _granted.Count; i++)
            {
                var e = _granted[i];
                Feel(e.Asker, e.Giver, AffectKind.Gratitude, GratitudeAcute);
                Impulse(e.Asker, e.Giver, GrantRegard, GrantFam, MemoryKind.ReceivedHelp);
                Impulse(e.Giver, e.Asker, GaveHelpRegard, GaveHelpFam, MemoryKind.GaveHelp);
            }
            _granted.Clear();

            for (int i = 0; i < _refused.Count; i++)
            {
                var e = _refused[i];
                Feel(e.Asker, e.Refuser, AffectKind.Resentment, ResentAcute);
                Impulse(e.Asker, e.Refuser, RefusedRegard, RefusedFam, MemoryKind.WasRefused);
                Impulse(e.Refuser, e.Asker, RefuserRegard, RefuserFam, MemoryKind.RefusedToHelp);
            }
            _refused.Clear();

            for (int i = 0; i < _greets.Count; i++)
            {
                var e = _greets[i];
                Feel(e.A, e.B, AffectKind.Affection, AffectionAcute);
                Feel(e.B, e.A, AffectKind.Affection, AffectionAcute);
                Impulse(e.A, e.B, GreetRegard, GreetFam, MemoryKind.Met, record: false);
                Impulse(e.B, e.A, GreetRegard, GreetFam, MemoryKind.Met, record: false);
            }
            _greets.Clear();

            for (int i = 0; i < _dislikes.Count; i++)
                Feel(_dislikes[i].Who, _dislikes[i].Whom, AffectKind.Aversion, DislikeAcute);   // acute only; no regard change
            _dislikes.Clear();
        }

        public void Update(long tick)
        {
            var clock = _ctx.WorldClock.Current;
            if (clock.Year == 0) return;
            double gameHours = clock.DeltaGameSeconds / 3600.0;
            if (gameHours <= 0) return;

            _keys.Clear();
            foreach (var kv in _ctx.Affects.All) _keys.Add(kv.Key);
            _keys.Sort((a, b) => a.Value.CompareTo(b.Value));   // key order → determinism
            for (int k = 0; k < _keys.Count; k++)
            {
                if (!_ctx.Affects.TryGet(_keys[k], out var data)) continue;
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

        void Feel(EntityId self, EntityId target, AffectKind kind, double add)
        {
            if (!_ctx.Affects.TryGet(self, out var data)) { data = new AffectsData(); _ctx.Affects.Set(self, data); }
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
            _ctx.Events.Emit(new RelationImpulseEvent
            {
                Who = who, Other = other, RegardDelta = regard, FamiliarityDelta = fam,
                Memory = mem, RecordMemory = record,
            });
        }
    }
}
