namespace DaggerfallWorkshop.Sim
{
    /// Emitted by WeatherSystem when the WeatherRegistry transitions to a new
    /// WeatherKind. Carries both the new kind and the previous one so handlers
    /// can react to specific transitions (e.g. "rain started", "thunder ended").
    public sealed class WeatherChangedSimEvent : ISimEvent
    {
        public WeatherKind From;
        public WeatherKind To;
    }
}
