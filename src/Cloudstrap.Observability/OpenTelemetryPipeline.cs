namespace Cloudstrap.Observability
{
    using System.Diagnostics;
    using Cloudstrap.Core;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using OpenTelemetry;
    using OpenTelemetry.Instrumentation.AspNetCore;
    using OpenTelemetry.Instrumentation.Http;
    using OpenTelemetry.Logs;
    using OpenTelemetry.Metrics;
    using OpenTelemetry.Trace;

    /// <summary>
    /// Owner-mode composition of the vendor-neutral OpenTelemetry pipeline: resource identity,
    /// per-signal registration gated by the <c>Cloudstrap:OpenTelemetry</c> flags, console export when
    /// enabled, and the consumer hooks applied last so they always have the final say.
    /// </summary>
    internal static class OpenTelemetryPipeline
    {
        /// <summary>
        /// Registers the owner-mode pipeline. Call only when <see cref="OpenTelemetryOptions.IsActive"/>.
        /// </summary>
        /// <param name="services">The service collection to register the pipeline into.</param>
        /// <param name="options">The Cloudstrap settings the registration decisions are made from.</param>
        /// <param name="observabilityOptions">The code-level options carrying the consumer hooks.</param>
        /// <param name="environment">The host environment used for the resource identity.</param>
        public static void ConfigureOwner(
            IServiceCollection services,
            CloudstrapOptions options,
            CloudstrapObservabilityOptions observabilityOptions,
            IHostEnvironment environment)
        {
            OpenTelemetryOptions telemetry = options.OpenTelemetry;

            ConfigureInstrumentationDefaults(services, options, observabilityOptions);

            OpenTelemetryBuilder openTelemetry = services.AddOpenTelemetry();

            openTelemetry.ConfigureResource(resourceBuilder =>
            {
                CloudstrapResourceAttributes.ApplyOwnerIdentity(resourceBuilder, options.Application, environment);
                observabilityOptions.ConfigureResource?.Invoke(resourceBuilder);
            });

            if (telemetry.EnableTracing)
            {
                openTelemetry.WithTracing(tracing =>
                {
                    // An Azure Monitor exporter installs its own sampler after this point and wins, so a
                    // sampler set here would be silently discarded. Hub suppression moves to an export-time
                    // scrub instead — deliberately not subject to ApplySampler, which governs sampler
                    // installation, and in this mode Cloudstrap installs no sampler at all.
                    bool exporterOwnsSampler = telemetry.Mode == OpenTelemetryMode.AzureMonitor;

                    if (observabilityOptions.ApplySampler && !exporterOwnsSampler)
                    {
                        tracing.SetSampler(BuildSampler(telemetry));
                    }

                    if (exporterOwnsSampler && !telemetry.EnableBlazorHubTracing)
                    {
                        tracing.AddProcessor(new BlazorHubScrubProcessor());
                    }

                    tracing.AddSource(CloudstrapActivitySources.Business);
                    tracing.AddAspNetCoreInstrumentation();
                    tracing.AddHttpClientInstrumentation(instrumentation => instrumentation.RecordException = true);

                    if (telemetry.EnableSqlClientInstrumentation)
                    {
                        tracing.AddSqlClientInstrumentation();
                    }

                    if (telemetry.IsConsoleEnabled)
                    {
                        tracing.AddConsoleExporter();
                    }

                    if (telemetry.Mode == OpenTelemetryMode.Otlp)
                    {
                        tracing.AddOtlpExporter(exporter => OtlpExporterSetup.Configure(
                            exporter, telemetry, observabilityOptions, OtlpExporterSetup.TracesSignal));
                    }

                    observabilityOptions.ConfigureTracing?.Invoke(tracing);
                });
            }

            if (telemetry.EnableMetrics)
            {
                openTelemetry.WithMetrics(metrics =>
                {
                    if (telemetry.EnableRuntimeMetrics)
                    {
                        metrics.AddRuntimeInstrumentation();
                    }

                    if (telemetry.EnableHttpClientMetrics)
                    {
                        metrics.AddHttpClientInstrumentation();
                    }

                    if (telemetry.EnableAspNetCoreMetrics)
                    {
                        metrics.AddAspNetCoreInstrumentation();
                    }

                    if (telemetry.IsConsoleEnabled)
                    {
                        metrics.AddConsoleExporter();
                    }

                    if (telemetry.Mode == OpenTelemetryMode.Otlp)
                    {
                        metrics.AddOtlpExporter(exporter => OtlpExporterSetup.Configure(
                            exporter, telemetry, observabilityOptions, OtlpExporterSetup.MetricsSignal));
                    }

                    observabilityOptions.ConfigureMetrics?.Invoke(metrics);
                });
            }

            if (telemetry.EnableLogs)
            {
                openTelemetry.WithLogging(
                    configureBuilder: null,
                    configureOptions: loggerOptions =>
                    {
                        if (telemetry.IsConsoleEnabled)
                        {
                            loggerOptions.AddConsoleExporter();
                        }

                        if (telemetry.Mode == OpenTelemetryMode.Otlp)
                        {
                            loggerOptions.AddOtlpExporter(exporter => OtlpExporterSetup.Configure(
                                exporter, telemetry, observabilityOptions, OtlpExporterSetup.LogsSignal));
                        }

                        observabilityOptions.ConfigureLogging?.Invoke(loggerOptions);
                    });
            }

            if (telemetry.Mode == OpenTelemetryMode.AzureMonitor)
            {
                services.AddHostedService<AzureMonitorContributionGuard>();
            }
        }

        /// <summary>
        /// Contributes Cloudstrap's differentiated pieces to a pipeline the host already owns: the
        /// <c>cloudstrap.*</c> resource attributes (no <c>service.name</c> takeover), the sampler chain
        /// (subject to <see cref="CloudstrapObservabilityOptions.ApplySampler"/>), and the noise-filter
        /// post-configurations. No instrumentation, no exporters, no log pipeline, no Azure Monitor guard —
        /// exporter selection is entirely the host's. Call only when <see cref="OpenTelemetryOptions.IsActive"/>.
        /// </summary>
        /// <param name="services">The service collection carrying the host's pipeline.</param>
        /// <param name="options">The Cloudstrap settings the contribution decisions are made from.</param>
        /// <param name="observabilityOptions">The code-level options carrying the consumer hooks.</param>
        public static void ConfigureContribute(
            IServiceCollection services,
            CloudstrapOptions options,
            CloudstrapObservabilityOptions observabilityOptions)
        {
            OpenTelemetryOptions telemetry = options.OpenTelemetry;

            ConfigureInstrumentationDefaults(services, options, observabilityOptions);

            OpenTelemetryBuilder openTelemetry = services.AddOpenTelemetry();

            openTelemetry.ConfigureResource(resourceBuilder =>
            {
                CloudstrapResourceAttributes.ApplyCloudstrapAttributes(resourceBuilder, options.Application);
                observabilityOptions.ConfigureResource?.Invoke(resourceBuilder);
            });

            if (telemetry.EnableTracing)
            {
                openTelemetry.WithTracing(tracing =>
                {
                    tracing.AddSource(CloudstrapActivitySources.Business);

                    if (observabilityOptions.ApplySampler)
                    {
                        tracing.SetSampler(BuildSampler(telemetry));
                    }
                });
            }
        }

        /// <summary>
        /// Registers the noise-filter and enrichment shape as options post-configuration, composing with —
        /// never overwriting — anything the host already set. Registration-shaped on purpose: it applies to
        /// the instrumentation options regardless of who added the instrumentation.
        /// </summary>
        internal static void ConfigureInstrumentationDefaults(
            IServiceCollection services,
            CloudstrapOptions options,
            CloudstrapObservabilityOptions observabilityOptions)
        {
            services.PostConfigure<AspNetCoreTraceInstrumentationOptions>(instrumentation =>
            {
                if (observabilityOptions.EnableDefaultTraceNoiseFilter)
                {
                    Func<HttpContext, bool>? existingFilter = instrumentation.Filter;
                    instrumentation.Filter = context =>
                        (existingFilter?.Invoke(context) ?? true)
                        && TraceNoiseFilter.ShouldTrace(
                            context,
                            options.HealthChecks,
                            observabilityOptions.IgnoredPathSegments);
                }

                Action<Activity, HttpResponse>? existingEnrich = instrumentation.EnrichWithHttpResponse;
                instrumentation.EnrichWithHttpResponse = (activity, response) =>
                {
                    existingEnrich?.Invoke(activity, response);

                    HttpContext context = response.HttpContext;
                    activity.DisplayName = $"{context.Request.Method} {context.Request.Path}";

                    string? endpointName = context.GetEndpoint()?.DisplayName;
                    if (endpointName is not null)
                    {
                        activity.SetTag("endpoint.name", endpointName);
                    }
                };
            });

            services.PostConfigure<HttpClientTraceInstrumentationOptions>(instrumentation =>
            {
                if (observabilityOptions.EnableDefaultTraceNoiseFilter)
                {
                    Func<HttpRequestMessage, bool>? existingFilter = instrumentation.FilterHttpRequestMessage;
                    instrumentation.FilterHttpRequestMessage = request =>
                        (existingFilter?.Invoke(request) ?? true)
                        && TraceNoiseFilter.ShouldTrace(request, observabilityOptions.IgnoredPathSegments);
                }
            });
        }

        private static Sampler BuildSampler(OpenTelemetryOptions telemetry)
        {
            Sampler innerSampler = telemetry.AlwaysOnSampler
                ? new AlwaysOnSampler()
                : new ParentBasedSampler(new AlwaysOnSampler());

            return telemetry.EnableBlazorHubTracing ? innerSampler : new BlazorHubSampler(innerSampler);
        }
    }
}
