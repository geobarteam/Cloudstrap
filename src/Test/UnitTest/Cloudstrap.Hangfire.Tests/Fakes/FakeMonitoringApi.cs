namespace Cloudstrap.Hangfire.Tests.Fakes
{
    using global::Hangfire.Storage;
    using global::Hangfire.Storage.Monitoring;

    /// <summary>
    /// A hand-written <see cref="IMonitoringApi"/>: <see cref="GetStatistics"/> is scriptable (a value or a
    /// failure); every list returns empty.
    /// </summary>
    internal sealed class FakeMonitoringApi : IMonitoringApi
    {
        /// <summary>Gets or sets the statistics returned.</summary>
        public StatisticsDto Statistics { get; set; } = new();

        /// <summary>Gets or sets the failure <see cref="GetStatistics"/> throws, if any.</summary>
        public Exception? StatisticsFailure
        {
            get; set;
        }

        /// <inheritdoc />
        public StatisticsDto GetStatistics()
        {
            return StatisticsFailure is null ? Statistics : throw StatisticsFailure;
        }

        /// <inheritdoc />
        public IList<QueueWithTopEnqueuedJobsDto> Queues() => [];

        /// <inheritdoc />
        public IList<ServerDto> Servers() => [];

        /// <inheritdoc />
        public JobDetailsDto JobDetails(string jobId) => new();

        /// <inheritdoc />
        public JobList<EnqueuedJobDto> EnqueuedJobs(string queue, int from, int perPage) => new([]);

        /// <inheritdoc />
        public JobList<FetchedJobDto> FetchedJobs(string queue, int from, int perPage) => new([]);

        /// <inheritdoc />
        public JobList<ProcessingJobDto> ProcessingJobs(int from, int count) => new([]);

        /// <inheritdoc />
        public JobList<ScheduledJobDto> ScheduledJobs(int from, int count) => new([]);

        /// <inheritdoc />
        public JobList<SucceededJobDto> SucceededJobs(int from, int count) => new([]);

        /// <inheritdoc />
        public JobList<FailedJobDto> FailedJobs(int from, int count) => new([]);

        /// <inheritdoc />
        public JobList<DeletedJobDto> DeletedJobs(int from, int count) => new([]);

        /// <inheritdoc />
        public long ScheduledCount() => 0;

        /// <inheritdoc />
        public long EnqueuedCount(string queue) => 0;

        /// <inheritdoc />
        public long FetchedCount(string queue) => 0;

        /// <inheritdoc />
        public long FailedCount() => 0;

        /// <inheritdoc />
        public long ProcessingCount() => 0;

        /// <inheritdoc />
        public long SucceededListCount() => 0;

        /// <inheritdoc />
        public long DeletedListCount() => 0;

        /// <inheritdoc />
        public IDictionary<DateTime, long> SucceededByDatesCount() => new Dictionary<DateTime, long>();

        /// <inheritdoc />
        public IDictionary<DateTime, long> FailedByDatesCount() => new Dictionary<DateTime, long>();

        /// <inheritdoc />
        public IDictionary<DateTime, long> HourlySucceededJobs() => new Dictionary<DateTime, long>();

        /// <inheritdoc />
        public IDictionary<DateTime, long> HourlyFailedJobs() => new Dictionary<DateTime, long>();
    }
}
