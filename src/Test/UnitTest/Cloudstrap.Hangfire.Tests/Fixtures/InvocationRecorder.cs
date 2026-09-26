namespace Cloudstrap.Hangfire.Tests.Fixtures
{
    using System.Collections.Concurrent;

    /// <summary>Process-wide record of fixture-task runs (fixtures run one at a time: the assembly is non-parallel).</summary>
    public static class InvocationRecorder
    {
        private static readonly ConcurrentQueue<string> _runs = new();

        /// <summary>Gets a snapshot of the job ids run so far.</summary>
        public static IReadOnlyList<string> Runs => [.. _runs];

        /// <summary>Records one run of the given job id.</summary>
        public static void Record(string jobId)
        {
            _runs.Enqueue(jobId);
        }

        /// <summary>Forgets every recorded run.</summary>
        public static void Reset()
        {
            _runs.Clear();
        }
    }
}
