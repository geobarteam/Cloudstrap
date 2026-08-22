namespace Cloudstrap.Observability
{
    using System.Diagnostics;

    /// <summary>
    /// How a Blazor Server SignalR component-hub span is recognized. Shared by the two suppression
    /// mechanisms — <see cref="BlazorHubSampler"/> drops the span at sampling time, and
    /// <see cref="BlazorHubScrubProcessor"/> at export time when an exporter owns the sampler.
    /// </summary>
    internal static class BlazorHubActivity
    {
        /// <summary>
        /// The tag SignalR sets to the name of the hub handling the call.
        /// </summary>
        internal const string RpcServiceTagKey = "rpc.service";

        /// <summary>
        /// The hub name Blazor Server's interactive circuit runs on.
        /// </summary>
        internal const string ComponentHubServiceName = "ComponentHub";

        /// <summary>
        /// Determines whether an activity that has already started is a component-hub span.
        /// </summary>
        /// <param name="activity">The activity to test.</param>
        /// <returns><see langword="true"/> when the activity is a component-hub span.</returns>
        internal static bool IsComponentHub(Activity activity)
            => Equals(activity.GetTagItem(RpcServiceTagKey), ComponentHubServiceName);
    }
}
