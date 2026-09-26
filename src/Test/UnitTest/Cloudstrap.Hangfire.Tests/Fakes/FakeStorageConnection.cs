namespace Cloudstrap.Hangfire.Tests.Fakes
{
    using global::Hangfire.Common;
    using global::Hangfire.Server;
    using global::Hangfire.Storage;

    /// <summary>
    /// A hand-written <see cref="JobStorageConnection"/> backing the recurring-job set and hashes with
    /// in-memory collections (the source test technique, ported without a mocking library) and a scriptable
    /// distributed lock. Everything the package never calls throws <see cref="NotSupportedException"/>.
    /// </summary>
    internal sealed class FakeStorageConnection : JobStorageConnection
    {
        /// <summary>A serialized job payload whose type cannot be loaded (another component's job).</summary>
        private const string _unloadablePayload =
            "{\"t\":\"Contoso.Missing.Type, Contoso.Missing\",\"m\":\"Run\",\"p\":[],\"a\":[]}";

        private readonly Dictionary<string, HashSet<string>> _sets = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, string>> _hashes = new(StringComparer.Ordinal);
        private readonly HashSet<string> _heldLocks = new(StringComparer.Ordinal);
        private readonly Lock _gate = new();

        /// <summary>Gets the resources of every distributed lock requested, in order.</summary>
        public List<string> LockRequests { get; } = [];

        /// <summary>Gets or sets a value indicating whether every lock request fails as if the lock were held.</summary>
        public bool LockAlwaysBusy
        {
            get; set;
        }

        /// <summary>Gets the number of times the connection was disposed.</summary>
        public int DisposeCount
        {
            get; private set;
        }

        /// <summary>Seeds one stored recurring job, as Hangfire persists it.</summary>
        /// <param name="id">The recurring job id.</param>
        /// <param name="job">The job, or <see langword="null"/> to store a payload whose type cannot be loaded.</param>
        public void AddRecurringJob(string id, Job? job)
        {
            string payload = job is null
                ? _unloadablePayload
                : InvocationData.SerializeJob(job).SerializePayload();

            lock (_gate)
            {
                if (!_sets.TryGetValue("recurring-jobs", out HashSet<string>? ids))
                {
                    ids = new HashSet<string>(StringComparer.Ordinal);
                    _sets["recurring-jobs"] = ids;
                }

                ids.Add(id);
                _hashes[$"recurring-job:{id}"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Job"] = payload,
                    ["Cron"] = "* * * * *",
                };
            }
        }

        /// <inheritdoc />
        public override HashSet<string> GetAllItemsFromSet(string key)
        {
            lock (_gate)
            {
                return _sets.TryGetValue(key, out HashSet<string>? items)
                    ? new HashSet<string>(items, StringComparer.Ordinal)
                    : new HashSet<string>(StringComparer.Ordinal);
            }
        }

        /// <inheritdoc />
        public override Dictionary<string, string>? GetAllEntriesFromHash(string key)
        {
            lock (_gate)
            {
                return _hashes.TryGetValue(key, out Dictionary<string, string>? entries)
                    ? new Dictionary<string, string>(entries, StringComparer.Ordinal)
                    : null;
            }
        }

        /// <inheritdoc />
        public override IDisposable AcquireDistributedLock(string resource, TimeSpan timeout)
        {
            lock (_gate)
            {
                LockRequests.Add(resource);
                if (LockAlwaysBusy || !_heldLocks.Add(resource))
                {
                    throw new DistributedLockTimeoutException(resource);
                }
            }

            return new Releaser(this, resource);
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            DisposeCount++;
            base.Dispose();
        }

        /// <inheritdoc />
        public override IWriteOnlyTransaction CreateWriteTransaction()
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override string CreateExpiredJob(Job job, IDictionary<string, string> parameters, DateTime createdAt, TimeSpan expireIn)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override IFetchedJob FetchNextJob(string[] queues, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void SetJobParameter(string id, string name, string value)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override string GetJobParameter(string id, string name)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override JobData GetJobData(string jobId)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override StateData GetStateData(string jobId)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void AnnounceServer(string serverId, ServerContext context)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void RemoveServer(string serverId)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void Heartbeat(string serverId)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override int RemoveTimedOutServers(TimeSpan timeOut)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override string GetFirstByLowestScoreFromSet(string key, double fromScore, double toScore)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void SetRangeInHash(string key, IEnumerable<KeyValuePair<string, string>> keyValuePairs)
        {
            throw new NotSupportedException();
        }

        private void Release(string resource)
        {
            lock (_gate)
            {
                _heldLocks.Remove(resource);
            }
        }

        private sealed class Releaser(FakeStorageConnection owner, string resource) : IDisposable
        {
            public void Dispose()
            {
                owner.Release(resource);
            }
        }
    }
}
