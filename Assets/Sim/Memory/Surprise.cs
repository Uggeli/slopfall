namespace DaggerfallWorkshop.Sim.Memory
{
    /// <summary>
    /// Per-atom prediction error, aggregated two ways for its two consumers (the p2 lesson: same
    /// operator family, two sites, chosen per site). ATTENTION = MAX — the worst single violation,
    /// so one alarming feature spikes attention and is never averaged away. ENCODE = MEAN
    /// (sum/count) — an encoding-volume question, so one trivial new atom among many matches scores
    /// low and does NOT etch trivia at full strength (the notched-ear test). Error per atom type
    /// over the union of percept and prediction: both present -> |Δ|; one side only -> |value|.
    /// </summary>
    public readonly struct Surprise
    {
        public readonly Fixed Attention;
        public readonly Fixed Encode;

        public Surprise(Fixed attention, Fixed encode) { Attention = attention; Encode = encode; }

        /// <summary>Recognition found no category — nothing to predict against, maximal surprise.</summary>
        public static readonly Surprise Maximal = new Surprise(Fixed.One, Fixed.One);

        public static Surprise Against(AtomBag percept, AtomBag prediction)
        {
            long max = 0;
            long sum = 0;
            int n = 0;
            int i = 0, j = 0;
            while (i < percept.Count && j < prediction.Count)
            {
                int cmp = percept[i].Type.CompareTo(prediction[j].Type);
                long e;
                if (cmp < 0) { e = Abs(percept[i].Value.Raw); i++; }
                else if (cmp > 0) { e = Abs(prediction[j].Value.Raw); j++; }
                else { e = Abs((long)percept[i].Value.Raw - prediction[j].Value.Raw); i++; j++; }
                sum += e; if (e > max) max = e; n++;
            }
            while (i < percept.Count) { long e = Abs(percept[i].Value.Raw); sum += e; if (e > max) max = e; n++; i++; }
            while (j < prediction.Count) { long e = Abs(prediction[j].Value.Raw); sum += e; if (e > max) max = e; n++; j++; }

            if (n == 0) return new Surprise(Fixed.Zero, Fixed.Zero);
            return new Surprise(new Fixed((int)max), new Fixed((int)(sum / n)));
        }

        static long Abs(long x) { return x < 0 ? -x : x; }
    }
}
