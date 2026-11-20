namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// How far the engine is run. The MVP steps a day only once that day's answers are all
    /// in (simulation.js step: record the day's answers, then resolve, read and select), so
    /// a stored day is decided once, on complete answers, and never moves afterwards. Here
    /// that day is yesterday: answers keep arriving all through today, and a run that
    /// included today would decide it on part of them and decide it again tomorrow — the
    /// day's state could change and an action could be selected, then replaced.
    /// </summary>
    public static class EngineRunWindow
    {
        /// <summary>The last day whose answers are complete: yesterday, in UTC.</summary>
        public static DateOnly LastCompleteDay(DateTime utcNow) => DateOnly.FromDateTime(utcNow).AddDays(-1);

        public static DateOnly LastCompleteDay() => LastCompleteDay(DateTime.UtcNow);
    }
}
