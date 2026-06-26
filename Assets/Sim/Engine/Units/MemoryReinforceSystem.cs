using DaggerfallWorkshop.Sim.Memory;

namespace DaggerfallWorkshop.Sim.Engine
{
    /// <summary>
    /// Folds interaction outcomes (social + damage) into the perceiver's learned categories (the new
    /// MeaningsStore), mirroring the old MeaningsSystem's signal but landing it in per-agent atom-categories.
    /// Stateless; reads the 5 outcome events (4 social + DamageEvent for "this kind hurt me") + Perceivable
    /// (for the other's signature), emits MemoryReinforceIntents the AgentMemoryRegistry applies.
    /// </summary>
    public sealed class MemoryReinforceSystem : SimSystem
    {
        const double Grant = 1.0, Refuse = -1.0, Greet = 0.5, Dislike = -0.5, Hurt = -1.0;

        readonly PerceivableRegistry _perceivable;

        public MemoryReinforceSystem(EventBus events, PerceivableRegistry perceivable) : base(events) { _perceivable = perceivable; }

        public override void Update(long tick)
        {
            foreach (ref readonly var g in Events.GetEvents<HelpGrantedEvent>()) Emit(g.Asker, g.Giver, Grant);
            foreach (ref readonly var f in Events.GetEvents<HelpRefusedEvent>()) Emit(f.Asker, f.Refuser, Refuse);
            foreach (ref readonly var gr in Events.GetEvents<GreetingEvent>()) { Emit(gr.A, gr.B, Greet); Emit(gr.B, gr.A, Greet); }
            foreach (ref readonly var d in Events.GetEvents<DislikeNearbyEvent>()) Emit(d.Who, d.Whom, Dislike);
            // Phase D/L1: "this kind hurt me" — being attacked reinforces the victim's category for the
            // ATTACKER's kind, aversive. (Reinforce no-ops if the victim has no such category seeded.)
            foreach (ref readonly var dm in Events.GetEvents<DamageEvent>()) Emit(dm.Target, dm.Source, Hurt);
        }

        void Emit(EntityId self, EntityId other, double outcome)
        {
            AtomBag sig = _perceivable.Signature(other);
            if (sig.Count == 0) return;
            Events.Publish(new MemoryReinforceIntent { Perceiver = self, Signature = sig, Outcome = Fixed.FromDouble(outcome) });
        }
    }
}
