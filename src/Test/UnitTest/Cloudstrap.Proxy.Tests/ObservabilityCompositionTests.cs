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
    using Microsoft.AspNetCore.Http;
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
        /// <c>Authorization</c> header. No activity, log record or exception carries it, or any part of it, with the
        /// correlation handler now in the chain.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task ProxiedRequest_ApplicationToken_AppearsInNoActivityOrLogEntry()
        {
            // Act
            TokenLeakRecording recording = await ProxyAndRecordAsync(failTokenAcquisition: false);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(recording.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests.SingleOrDefault()?.Headers.GetValueOrDefault("Authorization"), Is.EqualTo($"Bearer {SecretTokenHandlerProvider.Token}"), "the token was not attached, so the check below proves nothing");
                AssertTokenAppearsNowhere(recording);
            });
        }

        /// <summary>
        /// Regression guard (green on arrival): the token handler fails after it has put the token on the request (the
        /// worst case of a failed acquisition). YARP logs and traces the failure and answers 502; neither the token nor
        /// any part of it reaches a log record, an activity, an exception or the response.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task TokenAcquisitionFailure_ApplicationToken_AppearsInNoActivityLogOrException()
        {
            // Act
            TokenLeakRecording recording = await ProxyAndRecordAsync(failTokenAcquisition: true);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(recording.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
                Assert.That(_echo.Requests, Is.Empty);
                Assert.That(recording.ExceptionTexts, Is.Not.Empty, "the failure raised no exception, so the check below proves nothing");
                AssertTokenAppearsNowhere(recording);
            });
        }

        /// <summary>
        /// Regression guard (green on arrival): the destination rejects the application's token with a 401. The 401 is
        /// passed through, and the token appears in no log record, activity, exception or response.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task Downstream401_ApplicationToken_AppearsInNoActivityLogOrException()
        {
            // Arrange
            _echo.StatusCode = StatusCodes.Status401Unauthorized;

            // Act
            TokenLeakRecording recording = await ProxyAndRecordAsync(failTokenAcquisition: false);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(recording.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(_echo.Requests.SingleOrDefault()?.Headers.GetValueOrDefault("Authorization"), Is.EqualTo($"Bearer {SecretTokenHandlerProvider.Token}"), "the token was not attached, so the check below proves nothing");
                AssertTokenAppearsNowhere(recording);
            });
        }

        /// <summary>
        /// Every 8-character window of the token: a match on any of them is a leak of part of the token.
        /// </summary>
        private static string[] TokenFragments()
        {
            const int length = 8;
            string token = SecretTokenHandlerProvider.Token;
            return [.. Enumerable.Range(0, token.Length - length + 1).Select(start => token.Substring(start, length))];
        }

        private static void AssertTokenAppearsNowhere(TokenLeakRecording recording)
        {
            string[] fragments = TokenFragments();
            static string[] Leaks(IEnumerable<string> texts, string[] fragments) =>
                [.. texts.Where(text => fragments.Any(fragment => text.Contains(fragment, StringComparison.Ordinal)))];

            Assert.That(recording.ActivityTexts, Is.Not.Empty, "no activity was recorded, so the check proves nothing");
            Assert.That(recording.LogTexts, Is.Not.Empty, "no log record was written, so the check proves nothing");
            Assert.That(Leaks(recording.ActivityTexts, fragments), Is.Empty, "the token leaked into an activity");
            Assert.That(Leaks(recording.LogTexts, fragments), Is.Empty, "the token leaked into a log record");
            Assert.That(Leaks(recording.ExceptionTexts, fragments), Is.Empty, "the token leaked into an exception");
            Assert.That(Leaks(recording.ResponseTexts, fragments), Is.Empty, "the token leaked into the response");
        }

        /// <summary>
        /// Sends one traced, signed-in request through a <c>Client</c> cluster whose token is
        /// <see cref="SecretTokenHandlerProvider.Token"/>, recording every activity started during it (any source),
        /// every log record at every level, every exception thrown in the process and the response.
        /// </summary>
        private async Task<TokenLeakRecording> ProxyAndRecordAsync(bool failTokenAcquisition)
        {
            ConcurrentQueue<Activity> activities = new();
            ConcurrentQueue<string> exceptions = new();
            using ActivityListener listener = new()
            {
                ShouldListenTo = _ => true,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = activities.Enqueue,
            };
            ActivitySource.AddActivityListener(listener);
            FakeLogCollector logs = new();
            WebApplicationBuilder builder = CreateCorrelatedBuilder(nameof(ProxyTokenMode.Client));
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider>(new SecretTokenHandlerProvider(failTokenAcquisition));
            builder.Logging.SetMinimumLevel(LogLevel.Trace);
            builder.Logging.AddProvider(new FakeLoggerProvider(logs));
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await StartCorrelatedAsync(builder);

            void OnFirstChance(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs args) =>
                exceptions.Enqueue(args.Exception.ToString());

            AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
            HttpStatusCode statusCode;
            List<string> responseTexts = [];
            try
            {
                using HttpResponseMessage response = await SendTracedAsync(frontDoor);
                statusCode = response.StatusCode;
                responseTexts.Add(await response.Content.ReadAsStringAsync());
                responseTexts.AddRange(response.Headers.Concat(response.Content.Headers).Select(header => $"{header.Key}={string.Join(",", header.Value)}"));
            }
            finally
            {
                AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
            }

            IReadOnlyList<FakeLogRecord> records = logs.GetSnapshot();
            return new TokenLeakRecording(
                statusCode,
                [.. activities.SelectMany(Describe)],
                [.. records.SelectMany(Describe)],
                [.. exceptions, .. records.Where(record => record.Exception is not null).Select(record => record.Exception!.ToString())],
                [.. responseTexts]);
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
            yield return activity.StatusDescription ?? string.Empty;
            yield return activity.TraceStateString ?? string.Empty;
            foreach (ActivityLink link in activity.Links)
            {
                yield return $"{link.Context.TraceId}-{link.Context.SpanId} {link.Context.TraceState}";
                foreach (KeyValuePair<string, object?> tag in link.Tags ?? [])
                {
                    yield return $"{tag.Key}={tag.Value}";
                }
            }

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

        /// <summary>What one proxied request left behind, as text.</summary>
        private sealed record TokenLeakRecording(
            HttpStatusCode StatusCode,
            string[] ActivityTexts,
            string[] LogTexts,
            string[] ExceptionTexts,
            string[] ResponseTexts);

        /// <summary>
        /// A client-token seam with a distinctive token that shares no 8-character window with a cluster id, a path or
        /// a header name, so any fragment of it found elsewhere is a leak. When <c>fail</c> is set, the handler puts the
        /// token on the request and then fails, as a token endpoint that errors mid-acquisition would.
        /// </summary>
        private sealed class SecretTokenHandlerProvider(bool fail) : IClientAccessTokenHandlerProvider
        {
            public const string Token = "q7XvMp2RLw9z.Zt4nKe8sHd3J.uY6cBf1GaW5o";

            public DelegatingHandler CreateClientTokenHandler(string clientName, TokenRequestOptions? tokenRequest) =>
                new SecretTokenHandler(fail);

            private sealed class SecretTokenHandler(bool fail) : DelegatingHandler
            {
                protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
                    return fail
                        ? throw new HttpRequestException("The token endpoint returned an error (simulated).")
                        : base.SendAsync(request, cancellationToken);
                }
            }
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
