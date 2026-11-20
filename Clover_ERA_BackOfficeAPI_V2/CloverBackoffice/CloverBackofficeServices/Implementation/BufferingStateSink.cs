using Rrida.State;

namespace CloverBackoffice.Services.Implementation
{
    /// <summary>
    /// The resolver's sink, buffered.
    ///
    /// <see cref="IStateSink.Write"/> is synchronous and is called inside the day loop, once
    /// per team per day. Writing to the database there would mean blocking on async work in
    /// the middle of a replay, so the run collects its records and the store writes them
    /// afterwards, in one transaction. A run of a year is a few hundred small records.
    ///
    /// <see cref="StateLog.Of"/> does the mapping, so the rounding is the resolver's own:
    /// the log record is rounded to six decimal places while the readings behind it are not.
    /// </summary>
    public sealed class BufferingStateSink : IStateSink
    {
        private readonly string _configId;

        public BufferingStateSink(string configId) => _configId = configId;

        /// <summary>In the order the days were fed, which is the order they must be read back.</summary>
        public List<StateLog.StateRecord> Records { get; } = [];

        public string ConfigId => _configId;

        public void Write(string teamId, ResolvedRecord record) =>
            Records.Add(StateLog.Of(teamId, record, _configId));
    }
}
