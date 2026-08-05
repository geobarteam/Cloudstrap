namespace Cloudstrap.Observability
{
    using Cloudstrap.Core;
    using OpenTelemetry.Exporter;

    /// <summary>
    /// OTLP exporter configuration for <see cref="OpenTelemetryMode.Otlp"/>: with an explicit endpoint the
    /// exporter speaks HTTP/protobuf to the per-signal <c>/v1/{signal}</c> path with Core's headers; with no
    /// explicit endpoint (validation guaranteed <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is present) nothing is set —
    /// endpoint, protocol and headers are the SDK's to resolve from the standard variables.
    /// </summary>
    internal static class OtlpExporterSetup
    {
        internal const string TracesSignal = "traces";
        internal const string MetricsSignal = "metrics";
        internal const string LogsSignal = "logs";

        /// <summary>
        /// Configures one signal's OTLP exporter options, then invokes the consumer's
        /// <see cref="CloudstrapObservabilityOptions.ConfigureOtlpExporter"/> hook last so it wins.
        /// </summary>
        public static void Configure(
            OtlpExporterOptions exporter,
            OpenTelemetryOptions telemetry,
            CloudstrapObservabilityOptions observabilityOptions,
            string signal)
        {
            if (telemetry.Endpoint is not null)
            {
                exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                exporter.Endpoint = AppendSignalPath(telemetry.Endpoint, signal);

                if (telemetry.Headers.Count > 0)
                {
                    exporter.Headers = string.Join(
                        ',',
                        telemetry.Headers.Select(header => $"{header.Key}={header.Value}"));
                }
            }

            observabilityOptions.ConfigureOtlpExporter?.Invoke(exporter);
        }

        private static Uri AppendSignalPath(Uri endpoint, string signal)
        {
            string baseUri = endpoint.AbsoluteUri.TrimEnd('/');

            return new Uri($"{baseUri}/v1/{signal}", UriKind.Absolute);
        }
    }
}
