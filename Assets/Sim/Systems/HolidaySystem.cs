namespace DaggerfallWorkshop.Sim
{
    /// Classic Daggerfall's holiday calendar, tables ported verbatim from
    /// DFU's FormulaHelper.GetHolidayId. 53 holidays; some celebrated
    /// everywhere (0xFF), some in one region only.
    public static class Holidays
    {
        static readonly byte[] RegionCelebrating =
        {
            0xFF, 0x19, 0x01, 0xFF, 0x1D, 0x05, 0x19, 0x06, 0x3C, 0xFF, 0x29, 0x1A,
            0xFF, 0x02, 0x19, 0x01, 0x0E, 0x12, 0x14, 0xFF, 0xFF, 0x1C, 0x21, 0x1F, 0x2C, 0xFF, 0x12,
            0x23, 0xFF, 0x38, 0xFF, 0x01, 0x30, 0x29, 0x0B, 0x16, 0xFF, 0xFF, 0x11, 0x17, 0x14, 0x01,
            0xFF, 0x13, 0xFF, 0x33, 0x3C, 0x2E, 0xFF, 0xFF, 0x01, 0x2D, 0x18,
        };

        static readonly short[] DayOfYear =
        {
            0x01, 0x02, 0x0C, 0x0F, 0x10, 0x12, 0x20, 0x23, 0x26, 0x2E, 0x39, 0x3A,
            0x43, 0x45, 0x55, 0x56, 0x5B, 0x67, 0x6E, 0x76, 0x7F, 0x81, 0x8C, 0x96, 0x97, 0xA6, 0xAD,
            0xAE, 0xBE, 0xC0, 0xC8, 0xD1, 0xD4, 0xDD, 0xE0, 0xE7, 0xED, 0xF3, 0xF6, 0xFC, 0x103, 0x113,
            0x11B, 0x125, 0x12C, 0x12F, 0x134, 0x13E, 0x140, 0x159, 0x15C, 0x162, 0x163,
        };

        /// Holiday id (1-53) celebrated in `regionIndex` on `dayOfYear`
        /// (1-based), or 0 for an ordinary day.
        public static int GetHolidayId(int dayOfYear, int regionIndex)
        {
            if (dayOfYear < 1 || dayOfYear > 355) return 0;
            for (int i = 0; i < DayOfYear.Length; i++)
            {
                if (DayOfYear[i] != dayOfYear) continue;
                if (RegionCelebrating[i] == 0xFF || RegionCelebrating[i] == regionIndex + 1)
                    return i + 1;
            }
            return 0;
        }
    }

    /// Today's festival, per simulation (a static here would bleed between
    /// test harnesses). 0 = ordinary day. Sole writer: HolidaySystem.
    public sealed class HolidayRegistry
    {
        int _currentId;

        public int CurrentId => System.Threading.Volatile.Read(ref _currentId);
        public void Set(int id) => System.Threading.Interlocked.Exchange(ref _currentId, id);
    }

    /// Watches the calendar and publishes "today is a festival" — OddSystem
    /// closes the shops and boosts the taverns on those days.
    public sealed class HolidaySystem : ISystem
    {
        SimulationContext _ctx;
        int _lastDayChecked = -1;

        public void Init(SimulationContext ctx) { _ctx = ctx; }
        public void ProcessEvents() { }

        public void Update(long tick)
        {
            var clock = _ctx.WorldClock.Current;
            if (clock.Year == 0) return;

            int dayOfYear = clock.Month * 30 + clock.Day + 1;
            if (dayOfYear == _lastDayChecked) return;
            _lastDayChecked = dayOfYear;

            var grid = _ctx.TownGrid.Current;
            int region = grid != null ? grid.RegionIndex : 17;      // Daggerfall by default

            int id = Holidays.GetHolidayId(dayOfYear, region);
            if (id != _ctx.Holiday.CurrentId && id > 0)
                _ctx.Events.Emit(new HolidayBeganEvent { HolidayId = id });
            _ctx.Holiday.Set(id);
        }
    }
}
