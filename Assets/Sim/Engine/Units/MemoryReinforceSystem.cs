using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>
    /// Folds social-interaction outcomes into the perceiver's learned categories (the new MeaningsStore),
    /// mirroring the old MeaningsSystem's signal but landing it in per-agent atom-categories. Stateless;
    /// reads the 4 outcome events + Perceivable (for the other's signature), emits MemoryReinforceIntents
    /// the AgentMemoryRegistry applies.
    /// </summary>
    public sealed class MemoryReinforceSystem : SimSystem
    {
        const double Grant = 1.0, Refuse = -1.0, Greet = 0.5, Dislike = -0.5;

        readonly PerceivableRegistry _perceivable;

        public MemoryReinforceSystem(EventBus events, PerceivableRegistry perceivable) : base(events) { _perceivable = perceivable; }

        public override void Update(long tick)
        {
            foreach (ref readonly var g in Events.GetEvents<HelpGrantedEvent>()) Emit(g.Asker, g.Giver, Grant);
            foreach (ref readonly var f in Events.GetEvents<HelpRefusedEvent>()) Emit(f.Asker, f.Refuser, Refuse);
            foreach (ref readonly var gr in Events.GetEvents<GreetingEvent>()) { Emit(gr.A, gr.B, Greet); Emit(gr.B, gr.A, Greet); }
            foreach (ref readonly var d in Events.GetEvents<DislikeNearbyEvent>()) Emit(d.Who, d.Whom, Dislike);
        }

        void Emit(EntityId self, EntityId other, double outcome)
        {
            AtomBag sig = _perceivable.Signature(other);
            if (sig.Count == 0) return;
            Events.Publish(new MemoryReinforceIntent { Perceiver = self, Signature = sig, Outcome = Fixed.FromDouble(outcome) });
        }
    }
}
