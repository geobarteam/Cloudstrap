namespace Cloudstrap.Hangfire.Tests.Fakes
{
    using global::Hangfire;
    using global::Hangfire.Common;

    /// <summary>One call recorded by <see cref="RecordingRecurringJobManager"/>.</summary>
    /// <param name="Kind">The method called: <c>AddOrUpdate</c>, <c>RemoveIfExists</c> or <c>Trigger</c>.</param>
    /// <param name="RecurringJobId">The recurring job id.</param>
    /// <param name="Job">The job, for <c>AddOrUpdate</c>.</param>
    /// <param name="Cron">The cron expression, for <c>AddOrUpdate</c>.</param>
    /// <param name="Options">The recurring-job options, for <c>AddOrUpdate</c>.</param>
    internal sealed record RecurringJobCall(string Kind, string RecurringJobId, Job? Job = null, string? Cron = null, RecurringJobOptions? Options = null);

    /// <summary>
    /// A hand-written <see cref="IRecurringJobManager"/> recording every call. It rejects a cron expression the
    /// way Hangfire does (an <see cref="ArgumentException"/>) when it does not have five or six fields, and can
    /// be told to fail every <c>AddOrUpdate</c>.
    /// </summary>
    internal sealed class RecordingRecurringJobManager : IRecurringJobManager
    {
        private readonly List<RecurringJobCall> _calls = [];
        private readonly Lock _gate = new();

        /// <summary>Gets a snapshot of the recorded calls, in order.</summary>
        public IReadOnlyList<RecurringJobCall> Calls
        {
            get
            {
                lock (_gate)
                {
                    return [.. _calls];
                }
            }
        }

        /// <summary>Gets or sets a value indicating whether every <c>AddOrUpdate</c> throws.</summary>
        public bool FailEveryAddOrUpdate
        {
            get; set;
        }

        /// <summary>Gets the recorded <c>AddOrUpdate</c> calls.</summary>
        public IReadOnlyList<RecurringJobCall> Added => [.. Calls.Where(call => call.Kind == nameof(AddOrUpdate))];

        /// <summary>Gets the ids passed to <c>RemoveIfExists</c>.</summary>
        public IReadOnlyList<string> Removed => [.. Calls.Where(call => call.Kind == nameof(RemoveIfExists)).Select(call => call.RecurringJobId)];

        /// <inheritdoc />
        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options)
        {
            if (FailEveryAddOrUpdate)
            {
                throw new InvalidOperationException("The storage rejected the write.");
            }

            int fields = cronExpression.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (fields is not (5 or 6))
            {
                throw new ArgumentException("CRON expression is invalid.", nameof(cronExpression));
            }

            Record(new RecurringJobCall(nameof(AddOrUpdate), recurringJobId, job, cronExpression, options));
        }

        /// <inheritdoc />
        public void RemoveIfExists(string recurringJobId)
        {
            Record(new RecurringJobCall(nameof(RemoveIfExists), recurringJobId));
        }

        /// <inheritdoc />
        public void Trigger(string recurringJobId)
        {
            Record(new RecurringJobCall(nameof(Trigger), recurringJobId));
        }

        private void Record(RecurringJobCall call)
        {
            lock (_gate)
            {
                _calls.Add(call);
            }
        }
    }
}
