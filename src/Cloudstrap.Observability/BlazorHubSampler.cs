namespace Cloudstrap.Observability
{
    using OpenTelemetry.Trace;

    /// <summary>
    /// A delegating sampler that drops Blazor Server SignalR component-hub spans. The hub is identified by
    /// the <c>rpc.service</c> tag, which SignalR only exposes in the sampling-time tags — sampling is the
    /// one moment these very-high-volume, low-value spans can be dropped.
    /// </summary>
    internal sealed class BlazorHubSampler : Sampler
    {
        private const string _rpcServiceTagKey = "rpc.service";
        private const string _componentHubServiceName = "ComponentHub";

        private readonly Sampler _innerSampler;

        public BlazorHubSampler(Sampler innerSampler)
        {
            _innerSampler = innerSampler;
        }

        public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
        {
            if (samplingParameters.Tags is not null)
            {
                foreach (KeyValuePair<string, object?> tag in samplingParameters.Tags)
                {
                    if (tag.Key == _rpcServiceTagKey && Equals(tag.Value, _componentHubServiceName))
                    {
                        return new SamplingResult(SamplingDecision.Drop);
                    }
                }
            }

            return _innerSampler.ShouldSample(in samplingParameters);
        }
    }
}
