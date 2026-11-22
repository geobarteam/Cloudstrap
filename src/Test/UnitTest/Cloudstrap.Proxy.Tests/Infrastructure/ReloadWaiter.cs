namespace Cloudstrap.Proxy.Tests.Infrastructure
{
    using System.Net;
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
    }
}
