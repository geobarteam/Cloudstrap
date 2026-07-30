namespace Cloudstrap.Observability
{
    /// <summary>
    /// How Cloudstrap relates to the host's OpenTelemetry pipeline. Cloudstrap coexists with platform
    /// defaults such as Aspire ServiceDefaults without depending on them: in <see cref="Owner"/> mode it
    /// stands up the whole pipeline; in <see cref="Contribute"/> mode it only enriches a pipeline the host
    /// already owns — no duplicate exporters, no duplicate spans, no <c>service.name</c> takeover.
    /// </summary>
    public enum ObservabilityPipelineMode
    {
        /// <summary>
        /// Cloudstrap owns the pipeline: resource identity, instrumentation, exporter selection per
        /// <c>Cloudstrap:OpenTelemetry:Mode</c>, samplers and noise filters. The default.
        /// </summary>
        Owner = 0,

        /// <summary>
        /// The host owns the pipeline (for example Aspire ServiceDefaults); Cloudstrap contributes only its
        /// differentiated pieces — the <c>cloudstrap.*</c> resource attributes, the sampler chain (unless
        /// <see cref="CloudstrapObservabilityOptions.ApplySampler"/> is <see langword="false"/>; note the
        /// OpenTelemetry <c>SetSampler</c> is last-wins, so a host that sets its own sampler after Cloudstrap
        /// overrides it) and the trace noise filters. No instrumentation, no exporters, no log pipeline, no
        /// Azure Monitor startup guard.
        /// </summary>
        Contribute = 1,
    }
}
