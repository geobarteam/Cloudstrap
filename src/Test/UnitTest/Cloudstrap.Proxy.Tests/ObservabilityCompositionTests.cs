namespace Cloudstrap.Proxy.Tests
{
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.Net;
    using System.Reflection;
    using Cloudstrap.Core;
    using Cloudstrap.Extensions;
    using Cloudstrap.Observability.Correlation;
    using Cloudstrap.Proxy.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Testing;
    using NUnit.Framework;
    using Yarp.ReverseProxy.Configuration;
    using Yarp.ReverseProxy.Forwarder;

    /// <summary>
    /// AC-PX7 and AC-PX9 (mechanic (e), correlation): the proxied hop carries the front door's correlation id and
    /// continues its trace; Cloudstrap adds no resilience handler and no OpenTelemetry pipeline of its own; the
    /// application's token never shows up in an activity or a log entry.
    /// </summary>
    [TestFixture]
    public sealed class ObservabilityCompositionTests
    {
        private const string _correlationId = "corr-front-door-1";
        private const string _traceId = "0af7651916cd43dd8448eb211c80319c";
        private const string _callerSpanId = "b7ad6b7169203331";
        private const string _callerTraceparent = $"00-{_traceId}-{_callerSpanId}-01";
        private const string _appToken = $"app:{ProxyConfig.ClusterId}";

        private EchoServer _echo = null!;

        [SetUp]
        public async Task StartEchoAsync()
        {
            _echo = await EchoServer.StartAsync();
        }

        [TearDown]
        public async Task StopEchoAsync()
        {
            await _echo.DisposeAsync();
        }

        [Test]
        public async Task ProxiedRequest_CarriesTheFrontDoorsCorrelationId()
        {
            // Arrange: the front door establishes the ambient id (the caller sent none)
            WebApplicationBuilder builder = CreateCorrelatedBuilder(nameof(ProxyTokenMode.None));
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await StartCorrelatedAsync(builder);

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.GetValueOrDefault("X-Correlation-ID"), Is.EqualTo(_correlationId));
            });
        }

        [Test]
        public async Task ProxiedRequest_CustomCorrelationHeaderName_IsHonoured()
        {
            // Arrange
            WebApplicationBuilder builder = CreateCorrelatedBuilder(nameof(ProxyTokenMode.None));
            builder.Services.Configure<CorrelationOptions>(options => options.HeaderName = "X-Request-Chain");
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await StartCorrelatedAsync(builder);

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.GetValueOrDefault("X-Request-Chain"), Is.EqualTo(_correlationId));
                Assert.That(_echo.Requests[0].Headers.ContainsKey("X-Correlation-ID"), Is.False);
            });
        }

        /// <summary>
        /// Regression guard (green on arrival): YARP copies the caller's header, and the front door adopts it as the
        /// ambient id, so the destination sees the caller's value. Set-if-absent keeps it that way.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task ProxiedRequest_CallerSentCorrelationId_IsKept()
        {
            // Arrange
            WebApplicationBuilder builder = CreateCorrelatedBuilder(nameof(ProxyTokenMode.None));
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await StartCorrelatedAsync(builder);
            using HttpRequestMessage request = new(HttpMethod.Get, "/api/orders");
            request.Headers.Authorization = new("Test", "alice");
            request.Headers.Add("X-Correlation-ID", "caller-chain-7");

            // Act
            using HttpResponseMessage response = await frontDoor.Client.SendAsync(request);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.GetValueOrDefault("X-Correlation-ID"), Is.EqualTo("caller-chain-7"));
            });
        }

        /// <summary>
        /// Regression guard (green on arrival): YARP's propagator replaces the caller's <c>traceparent</c> with the
        /// outgoing hop's, which belongs to the front door's trace.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task ProxiedRequest_Traceparent_ContinuesTheFrontDoorsTrace()
        {
            // Arrange
            ConcurrentQueue<Activity> activities = new();
            using ActivityListener listener = ListenToEverything(activities);
            WebApplicationBuilder builder = CreateCorrelatedBuilder(nameof(ProxyTokenMode.None));
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await StartCorrelatedAsync(builder);

            // Act
            using HttpResponseMessage response = await SendTracedAsync(frontDoor);

            // Assert
            string? traceparent = _echo.Requests.SingleOrDefault()?.Headers.GetValueOrDefault("traceparent");
            string[] parts = traceparent?.Split('-') ?? [];
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(parts, Has.Length.EqualTo(4), $"traceparent '{traceparent}'");
                Assert.That(parts.ElementAtOrDefault(1), Is.EqualTo(_traceId));
                Assert.That(parts.ElementAtOrDefault(2), Is.Not.EqualTo(_callerSpanId), "the caller's span was forwarded as the parent");
            });
        }

        [Test]
        public async Task ConfigureHttpClientDefaultsResilience_IsNotAppliedToTheForwarder()
        {
            // Arrange: a host-wide default handler, standing in for a resilience handler
            ConcurrentQueue<string> defaultHandlerCalls = new();
            WebApplicationBuilder builder = CreateCorrelatedBuilder(nameof(ProxyTokenMode.Client));
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider>(new FakeClientTokenHandlerProvider());
            builder.Services.ConfigureHttpClientDefaults(client => client.AddHttpMessageHandler(() => new RecordingHandler(defaultHandlerCalls)));
            builder.Services.AddHttpClient("typed");
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await StartCorrelatedAsync(builder);

            // Act
            using HttpResponseMessage proxied = await frontDoor.GetAsync("/api/orders");
            int callsAfterProxiedRequest = defaultHandlerCalls.Count;
            using (HttpClient typed = frontDoor.App.Services.GetRequiredService<IHttpClientFactory>().CreateClient("typed"))
            using (HttpResponseMessage direct = await typed.GetAsync(new Uri(_echo.Address, "direct")))
            {
                Assert.That(direct.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            }

            Type[] chain = ForwarderChain(frontDoor, nameof(ProxyTokenMode.Client));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(proxied.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(callsAfterProxiedRequest, Is.Zero, "the host-wide default handler ran on the proxied hop");
                Assert.That(defaultHandlerCalls, Has.Count.EqualTo(1), "the typed client lost its host-wide handler");
                Assert.That(chain, Has.Length.EqualTo(3), string.Join(" -> ", chain.Select(type => type.Name)));
                Assert.That(chain.ElementAtOrDefault(0), Is.EqualTo(typeof(CorrelationHttpDelegatingHandler)));
                Assert.That(chain.ElementAtOrDefault(1)?.DeclaringType, Is.EqualTo(typeof(FakeClientTokenHandlerProvider)));
                Assert.That(chain.ElementAtOrDefault(2), Is.EqualTo(typeof(SocketsHttpHandler)));
            });
        }

        [Test]
        public async Task NoneCluster_ForwarderChain_IsCorrelationThenYarpsHandler()
        {
            // Arrange
            WebApplicationBuilder builder = CreateCorrelatedBuilder(nameof(ProxyTokenMode.None));
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await StartCorrelatedAsync(builder);

            // Act
            Type[] chain = ForwarderChain(frontDoor, nameof(ProxyTokenMode.None));

            // Assert
            Assert.That(chain, Is.EqualTo(new[] { typeof(CorrelationHttpDelegatingHandler), typeof(SocketsHttpHandler) }));
        }

        /// <summary>
        /// Regression guard (green on arrival): the package adds no OpenTelemetry provider or exporter; the host's own
        /// pipeline (Cloudstrap.Observability or another owner) is the only one.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public void AddCloudstrapProxy_RegistersNoTracerOrMeterProviderOrExporter()
        {
            // Arrange
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address));
            HashSet<ServiceDescriptor> before = new(builder.Services, ReferenceEqualityComparer.Instance);

            // Act
            builder.AddCloudstrapProxy();

            // Assert
            string[] added = [.. builder.Services
                .Where(descriptor => !before.Contains(descriptor))
                .SelectMany(TypesOf)
                .Select(type => type.FullName ?? type.Name)];
            Assert.Multiple(() =>
            {
                Assert.That(added, Is.Not.Empty);
                Assert.That(
                    added.Where(name => name.StartsWith("OpenTelemetry", StringComparison.Ordinal)
                        || name.Contains("TracerProvider", StringComparison.Ordinal)
                        || name.Contains("MeterProvider", StringComparison.Ordinal)
                        || name.Contains("Exporter", StringComparison.Ordinal)),
                    Is.Empty);
            });
        }

        /// <summary>
        /// Regression guard (green on arrival): the application's token travels only in the outgoing
        /// <c>Authorization</c> header. No activity (name, tag, event, baggage) and no log entry at any level carries
        /// it, with the correlation handler now in the chain.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task ProxiedRequest_ApplicationToken_AppearsInNoActivityOrLogEntry()
        {
            // Arrange
            ConcurrentQueue<Activity> activities = new();
            using ActivityListener listener = ListenToEverything(activities);
            FakeLogCollector logs = new();
            WebApplicationBuilder builder = CreateCorrelatedBuilder(nameof(ProxyTokenMode.Client));
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider>(new FakeClientTokenHandlerProvider());
            builder.Logging.SetMinimumLevel(LogLevel.Trace);
            builder.Logging.AddProvider(new FakeLoggerProvider(logs));
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await StartCorrelatedAsync(builder);

            // Act
            using HttpResponseMessage response = await SendTracedAsync(frontDoor);

            // Assert
            string[] activityTexts = [.. activities.Where(activity => activity.TraceId.ToHexString() == _traceId).SelectMany(Describe)];
            string[] logTexts = [.. logs.GetSnapshot().SelectMany(Describe)];
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests.SingleOrDefault()?.Headers.GetValueOrDefault("Authorization"), Is.EqualTo($"Bearer {_appToken}"), "the token was not attached, so the check below proves nothing");
                Assert.That(activityTexts, Is.Not.Empty);
                Assert.That(logTexts, Is.Not.Empty);
                Assert.That(activityTexts.Where(text => text.Contains(_appToken, StringComparison.Ordinal)), Is.Empty);
                Assert.That(logTexts.Where(text => text.Contains(_appToken, StringComparison.Ordinal)), Is.Empty);
            });
        }

        /// <summary>
        /// A front door that runs the Cloudstrap correlation middleware, with a fixed generated id.
        /// </summary>
        private WebApplicationBuilder CreateCorrelatedBuilder(string tokenMode)
        {
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address, tokenMode: tokenMode));
            builder.Services.AddSingleton<ICorrelationSource>(new FixedCorrelationSource(_correlationId));
            builder.Services.AddCloudstrapCorrelation();
            return builder;
        }

        private static Task<FrontDoor> StartCorrelatedAsync(WebApplicationBuilder builder) =>
            FrontDoor.StartAsync(builder, app => app.UseCloudstrapCorrelation());

        private static Task<HttpResponseMessage> SendTracedAsync(FrontDoor frontDoor)
        {
            HttpRequestMessage request = new(HttpMethod.Get, "/api/orders");
            request.Headers.Authorization = new("Test", "alice");
            request.Headers.Add("traceparent", _callerTraceparent);
            return frontDoor.Client.SendAsync(request);
        }

        /// <summary>
        /// Builds a client with the front door's forwarder factory, as YARP does for a cluster, and lists the handler
        /// types from the outermost to YARP's own handler.
        /// </summary>
        private static Type[] ForwarderChain(FrontDoor frontDoor, string tokenMode)
        {
            IForwarderHttpClientFactory factory = frontDoor.App.Services.GetRequiredService<IForwarderHttpClientFactory>();
            using HttpMessageInvoker invoker = factory.CreateClient(new ForwarderHttpClientContext
            {
                ClusterId = ProxyConfig.ClusterId,
                NewConfig = HttpClientConfig.Empty,
                NewMetadata = new Dictionary<string, string> { [ProxyMetadataKeys.TokenMode] = tokenMode },
            });

            FieldInfo field = typeof(HttpMessageInvoker).GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("HttpMessageInvoker._handler not found.");
            List<Type> chain = [];
            HttpMessageHandler? handler = (HttpMessageHandler?)field.GetValue(invoker);
            while (handler is not null)
            {
                chain.Add(handler.GetType());
                handler = (handler as DelegatingHandler)?.InnerHandler;
            }

            return [.. chain];
        }

        private static ActivityListener ListenToEverything(ConcurrentQueue<Activity> activities)
        {
            ActivityListener listener = new()
            {
                ShouldListenTo = _ => true,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activities.Enqueue,
            };
            ActivitySource.AddActivityListener(listener);
            return listener;
        }

        private static IEnumerable<string> Describe(Activity activity)
        {
            yield return activity.DisplayName;
            yield return activity.OperationName;
            foreach (KeyValuePair<string, object?> tag in activity.TagObjects)
            {
                yield return $"{tag.Key}={tag.Value}";
            }

            foreach (ActivityEvent activityEvent in activity.Events)
            {
                yield return activityEvent.Name;
                foreach (KeyValuePair<string, object?> tag in activityEvent.Tags)
                {
                    yield return $"{tag.Key}={tag.Value}";
                }
            }

            foreach (KeyValuePair<string, string?> item in activity.Baggage)
            {
                yield return $"{item.Key}={item.Value}";
            }
        }

        private static IEnumerable<string> Describe(FakeLogRecord record)
        {
            yield return record.Message;
            if (record.Exception is not null)
            {
                yield return record.Exception.ToString();
            }

            foreach (KeyValuePair<string, string?> item in record.StructuredState ?? [])
            {
                yield return $"{item.Key}={item.Value}";
            }

            foreach (object? scope in record.Scopes)
            {
                yield return scope?.ToString() ?? string.Empty;
            }
        }

        private static IEnumerable<Type> TypesOf(ServiceDescriptor descriptor)
        {
            yield return descriptor.ServiceType;
            Type? implementation = descriptor.IsKeyedService
                ? descriptor.KeyedImplementationType ?? descriptor.KeyedImplementationInstance?.GetType()
                : descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();
            if (implementation is not null)
            {
                yield return implementation;
            }
        }

        private sealed class FixedCorrelationSource(string correlationId) : ICorrelationSource
        {
            public string GenerateCorrelation() => correlationId;
        }

        private sealed class RecordingHandler(ConcurrentQueue<string> calls) : DelegatingHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                calls.Enqueue(request.RequestUri?.AbsolutePath ?? string.Empty);
                return base.SendAsync(request, cancellationToken);
            }
        }
    }
}
