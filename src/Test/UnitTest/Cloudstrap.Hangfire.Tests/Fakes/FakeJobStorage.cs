namespace Cloudstrap.Hangfire.Tests.Fakes
{
    using global::Hangfire;
    using global::Hangfire.Storage;

    /// <summary>
    /// A hand-written <see cref="JobStorage"/> (the repo pins no mocking library): it hands out one scriptable
    /// <see cref="FakeStorageConnection"/> and one scriptable <see cref="FakeMonitoringApi"/>, so the package's
    /// storage-facing code runs with no SQL Server.
    /// </summary>
    internal sealed class FakeJobStorage : JobStorage
    {
        /// <summary>Gets the connection every <see cref="GetConnection"/> call returns.</summary>
        public FakeStorageConnection Connection { get; } = new();

        /// <summary>Gets the monitoring API every <see cref="GetMonitoringApi"/> call returns.</summary>
        public FakeMonitoringApi Monitoring { get; } = new();

        /// <summary>Gets the number of connections handed out.</summary>
        public int ConnectionsOpened
        {
            get; private set;
        }

        /// <inheritdoc />
        public override IStorageConnection GetConnection()
        {
            ConnectionsOpened++;
            return Connection;
        }

        /// <inheritdoc />
        public override IMonitoringApi GetMonitoringApi()
        {
            return Monitoring;
        }
    }
}
