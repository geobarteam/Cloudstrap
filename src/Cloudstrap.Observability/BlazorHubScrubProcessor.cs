namespace Cloudstrap.Observability
{
    using System.Diagnostics;
    using Cloudstrap.Core;
    using OpenTelemetry;

    /// <summary>
    /// Drops Blazor Server SignalR component-hub spans at export time, for the modes where an exporter
    /// package installs its own sampler and <see cref="BlazorHubSampler"/> would be discarded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registered ahead of every exporter, so clearing the recorded flag in
    /// <see cref="OnEnd(Activity)"/> is enough: export processors skip activities that are not recorded.
    /// </para>
    /// <para>
    /// This is suppression, not sampling. Under
    /// <see cref="OpenTelemetryMode.AzureMonitor"/>'s rate-limited default the hub spans still consume
    /// sampling budget before being scrubbed, so Blazor Server applications should prefer a fixed
    /// percentage.
    /// </para>
    /// </remarks>
    internal sealed class BlazorHubScrubProcessor : BaseProcessor<Activity>
    {
        /// <summary>
        /// Clears the recorded flag of a component-hub span, so no exporter downstream receives it.
        /// </summary>
        /// <param name="data">The activity that just ended.</param>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
        public override void OnEnd(Activity data)
        {
            ArgumentNullException.ThrowIfNull(data);

            if (BlazorHubActivity.IsComponentHub(data))
            {
                data.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
            }
        }
    }
}
