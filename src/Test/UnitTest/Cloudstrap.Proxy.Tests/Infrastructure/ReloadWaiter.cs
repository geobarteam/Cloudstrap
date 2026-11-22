namespace Cloudstrap.Proxy.Tests.Infrastructure
{
    using System.Net;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Testing;
    using NUnit.Framework;

    /// <summary>Waits for a configuration reload to reach YARP.</summary>
    internal static class ReloadWaiter
    {
        /// <summary>Polls as a signed-in user until the reloaded route forwards, so the reload is known applied.</summary>
        /// <param name="frontDoor">The running front door.</param>
        /// <param name="path">A path only the reloaded route matches.</param>
        /// <returns>A task that completes once the route answers 200.</returns>
        public static async Task WaitUntilForwardedAsync(FrontDoor frontDoor, string path)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (true)
            {
                using HttpResponseMessage probe = await frontDoor.GetAsync(path, user: "alice");
                if (probe.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }

                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail($"The reloaded route '{path}' was not applied within 10 s (last status {probe.StatusCode}).");
                }

                await Task.Delay(50);
            }
        }

        /// <summary>
        /// Waits until YARP logs that it refused a reloaded configuration because of an exception whose text contains
        /// <paramref name="expected"/>. YARP keeps the previous configuration in that case, so the reload is known
        /// rejected rather than not yet applied.
        /// </summary>
        /// <param name="logs">The collector of the front door's logs.</param>
        /// <param name="expected">Text the rejection's exception must contain.</param>
        /// <returns>The rejection's exception text.</returns>
        public static async Task<string> WaitUntilRejectedAsync(FakeLogCollector logs, string expected)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (true)
            {
                string? rejection = logs.GetSnapshot()
                    .Where(entry => entry.Level == LogLevel.Error)
                    .Select(entry => entry.Exception?.ToString())
                    .FirstOrDefault(text => text is not null && text.Contains(expected, StringComparison.Ordinal));
                if (rejection is not null)
                {
                    return rejection;
                }

                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail($"No reloaded configuration was rejected with '{expected}' within 10 s.");
                }

                await Task.Delay(50);
            }
        }
    }
}
