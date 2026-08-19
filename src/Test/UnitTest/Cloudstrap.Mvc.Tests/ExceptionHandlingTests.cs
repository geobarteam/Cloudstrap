namespace Cloudstrap.Mvc.Tests
{
    using System.Net;
    using System.Text.Json;
    using Cloudstrap.Mvc.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Diagnostics;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;

    /// <summary>
    /// AC-MVC5 and AC-MVC6: an unhandled exception produces the right shape for every caller — the
    /// consumer's error page for browsers, generic RFC 9457 for JSON clients with detail only on explicit
    /// opt-in, the developer page exactly where configured — each selection overridable in both
    /// directions, correlated, and logged exactly once on both branches.
    /// </summary>
    [TestFixture]
    public sealed class ExceptionHandlingTests
    {
        [Test]
        public async Task Throwing_InProductionWithAcceptTextHtml_ReExecutesTheConsumersErrorPage()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync();

            // Act
            using HttpResponseMessage response = await SendAsync(app, accept: "text/html");
            string body = await response.Content.ReadAsStringAsync(
                TestContext.CurrentContext.CancellationToken);

            // Assert — the source's raw-JSON-to-browsers defect provably gone, nothing leaked
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
                Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
                Assert.That(body, Does.Contain(ErrorController.Marker));
                Assert.That(body, Does.Not.Contain(nameof(InvalidOperationException)));
                Assert.That(body, Does.Not.Contain(BoomController.RootCauseMessage));
                Assert.That(body, Does.Not.Contain("failure level"));
            });
        }

        [Test]
        public async Task Throwing_InProductionPreferringJson_ReturnsGenericProblemJson()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync();

            // Act
            using HttpResponseMessage response = await SendAsync(app, accept: "application/json");
            JsonElement problem = await ParseAsync(response);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
                Assert.That(
                    response.Content.Headers.ContentType?.MediaType,
                    Is.EqualTo("application/problem+json"));
                Assert.That(problem.GetProperty("title").GetString(), Is.Not.Empty);
                Assert.That(problem.GetProperty("status").GetInt32(), Is.EqualTo(500));
                Assert.That(problem.TryGetProperty("exceptionType", out _), Is.False);
                Assert.That(problem.TryGetProperty("stackTrace", out _), Is.False);
            });
        }

        [Test]
        public async Task Throwing_WithAcceptAnyOnly_IsTreatedAsJsonPreferring()
        {
            // Arrange — mechanic (d): */* alone is not a browser
            await using WebApplication app = await MvcTestHost.StartAsync();

            // Act
            using HttpResponseMessage response = await SendAsync(app, accept: "*/*");

            // Assert
            Assert.That(
                response.Content.Headers.ContentType?.MediaType,
                Is.EqualTo("application/problem+json"));
        }

        [Test]
        public async Task Throwing_WithNoAcceptHeader_IsTreatedAsJsonPreferring()
        {
            // Arrange — mechanic (d): an absent Accept header is an API client, not a browser
            await using WebApplication app = await MvcTestHost.StartAsync();

            // Act
            using HttpResponseMessage response = await SendAsync(app, accept: null);

            // Assert
            Assert.That(
                response.Content.Headers.ContentType?.MediaType,
                Is.EqualTo("application/problem+json"));
        }

        [Test]
        public async Task Throwing_JsonPath_IncludesTheAmbientCorrelationId()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync();
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/boom", UriKind.Relative));
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("X-Correlation-ID", "contoso-9b2e");

            // Act
            using HttpResponseMessage response = await app.GetTestClient()
                .SendAsync(request, TestContext.CurrentContext.CancellationToken);
            JsonElement problem = await ParseAsync(response);

            // Assert
            Assert.That(problem.GetProperty("correlationId").GetString(), Is.EqualTo("contoso-9b2e"));
        }

        [Test]
        public async Task Throwing_InDevelopmentJsonPath_IncludesTypeMessageStackAndBoundedInnerChain()
        {
            // Arrange — IncludeDetails unset resolves true in Development; the developer page is pinned
            // off so the Cloudstrap handler is the one answering
            await using WebApplication app = await MvcTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:Mvc:ExceptionHandling:UseDeveloperExceptionPage"] = "false",
                },
                environment: "Development");

            // Act
            using HttpResponseMessage response = await SendAsync(app, accept: "application/json");
            JsonElement problem = await ParseAsync(response);

            // Assert — an 8-deep chain surfaces at most 5 levels
            Assert.Multiple(() =>
            {
                Assert.That(
                    problem.GetProperty("exceptionType").GetString(),
                    Is.EqualTo(typeof(InvalidOperationException).FullName));
                Assert.That(problem.GetProperty("exceptionMessage").GetString(), Is.Not.Empty);
                Assert.That(problem.GetProperty("stackTrace").GetString(), Is.Not.Empty);
                Assert.That(problem.GetProperty("innerExceptions").GetArrayLength(), Is.EqualTo(5));
            });
        }

        [Test]
        public async Task Throwing_HtmlPath_NeverIncludesDetailEvenWithIncludeDetailsTrue()
        {
            // Arrange — D-2's confirmed sub-question: IncludeDetails is JSON-only
            await using WebApplication app = await MvcTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:Mvc:ExceptionHandling:IncludeDetails"] = "true",
                });

            // Act
            using HttpResponseMessage response = await SendAsync(app, accept: "text/html");
            string body = await response.Content.ReadAsStringAsync(
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(body, Does.Contain(ErrorController.Marker));
                Assert.That(body, Does.Not.Contain(nameof(InvalidOperationException)));
                Assert.That(body, Does.Not.Contain(BoomController.RootCauseMessage));
            });
        }

        [Test]
        public async Task Throwing_WithIncludeDetailsExplicit_WinsInBothDirections()
        {
            // Arrange — explicit false strips detail in Development; explicit true adds it in Production
            await using WebApplication development = await MvcTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:Mvc:ExceptionHandling:UseDeveloperExceptionPage"] = "false",
                    ["Cloudstrap:Mvc:ExceptionHandling:IncludeDetails"] = "false",
                },
                environment: "Development");
            await using WebApplication production = await MvcTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:Mvc:ExceptionHandling:IncludeDetails"] = "true",
                });

            // Act
            using HttpResponseMessage stripped = await SendAsync(development, accept: "application/json");
            JsonElement strippedProblem = await ParseAsync(stripped);
            using HttpResponseMessage detailed = await SendAsync(production, accept: "application/json");
            JsonElement detailedProblem = await ParseAsync(detailed);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(strippedProblem.TryGetProperty("exceptionType", out _), Is.False);
                Assert.That(detailedProblem.TryGetProperty("exceptionType", out _), Is.True);
            });
        }

        [Test]
        public async Task Throwing_InDevelopmentBrowserRequest_RendersTheDeveloperExceptionPage()
        {
            // Arrange — the unset switch resolves true in Development: the framework page, not ours
            await using WebApplication app = await MvcTestHost.StartAsync(environment: "Development");

            // Act
            using HttpResponseMessage response = await SendAsync(app, accept: "text/html");
            string body = await response.Content.ReadAsStringAsync(
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
                Assert.That(body, Does.Contain(nameof(InvalidOperationException)));
                Assert.That(body, Does.Not.Contain(ErrorController.Marker));
            });
        }

        [Test]
        public async Task Throwing_WithUseDeveloperExceptionPageFalseInDevelopment_ReExecutesTheErrorPageInstead()
        {
            // Arrange — the override, and the Step 8 SUT posture (mechanic (j))
            await using WebApplication app = await MvcTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:Mvc:ExceptionHandling:UseDeveloperExceptionPage"] = "false",
                },
                environment: "Development");

            // Act
            using HttpResponseMessage response = await SendAsync(app, accept: "text/html");
            string body = await response.Content.ReadAsStringAsync(
                TestContext.CurrentContext.CancellationToken);

            // Assert — the inner handler catches first; the auto-inserted developer page never renders
            Assert.Multiple(() =>
            {
                Assert.That(body, Does.Contain(ErrorController.Marker));
                Assert.That(body, Does.Not.Contain(BoomController.RootCauseMessage));
            });
        }

        [Test]
        public async Task Throwing_WithUseDeveloperExceptionPageTrueInProduction_RendersTheDeveloperPage()
        {
            // Arrange — the other direction: explicitly selected outside Development
            await using WebApplication app = await MvcTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:Mvc:ExceptionHandling:UseDeveloperExceptionPage"] = "true",
                });

            // Act
            using HttpResponseMessage response = await SendAsync(app, accept: "text/html");
            string body = await response.Content.ReadAsStringAsync(
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(body, Does.Contain(nameof(InvalidOperationException)));
                Assert.That(body, Does.Not.Contain(ErrorController.Marker));
            });
        }

        [Test]
        public async Task Throwing_IsLoggedExactlyOnce_OnTheJsonPath()
        {
            // Arrange
            CapturingLoggerProvider capture = new();
            await using WebApplication app = await MvcTestHost.StartAsync(loggerProvider: capture);

            // Act
            using HttpResponseMessage response = await SendAsync(app, accept: "application/json");
            CapturedLogEntry[] errors =
            [
                .. capture.Entries.Where(entry =>
                    entry.Level == LogLevel.Error && entry.Exception is not null),
            ];

            // Assert
            Assert.That(errors, Has.Length.EqualTo(1));
        }

        [Test]
        public async Task Throwing_IsLoggedExactlyOnce_OnTheHtmlPath()
        {
            // Arrange — the handler falls through without logging; the framework middleware logs once
            CapturingLoggerProvider capture = new();
            await using WebApplication app = await MvcTestHost.StartAsync(loggerProvider: capture);

            // Act
            using HttpResponseMessage response = await SendAsync(app, accept: "text/html");
            CapturedLogEntry[] errors =
            [
                .. capture.Entries.Where(entry =>
                    entry.Level == LogLevel.Error && entry.Exception is not null),
            ];

            // Assert
            Assert.That(errors, Has.Length.EqualTo(1));
        }

        [Test]
        public async Task Throwing_WithAConsumerExceptionHandlerRegisteredFirst_TheConsumerWins()
        {
            // Arrange — registered before AddCloudstrapMvc, so it gets the first attempt
            await using WebApplication app = await MvcTestHost.StartAsync(
                beforeBuild: builder =>
                    builder.Services.AddExceptionHandler<ConsumerExceptionHandler>());
            using HttpClient client = app.GetTestClient();

            // Act
            using HttpResponseMessage response = await SendAsync(app, accept: "text/html");
            string body = await response.Content.ReadAsStringAsync(
                TestContext.CurrentContext.CancellationToken);

            // Assert — the consumer's response; no Cloudstrap payload, no re-execution
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
                Assert.That(body, Is.EqualTo(ConsumerExceptionHandler.Marker));
            });
        }

        [Test]
        public async Task Throwing_HtmlWithNoEndpointAtTheErrorPath_SurfacesA500()
        {
            // Arrange — no consumer endpoint at the configured path: stock semantics, nothing swallowed
            await using WebApplication app = await MvcTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:Application:ExceptionHandlerPath"] = "/missing-error",
                });

            // Act + Assert — the stock middleware never silently swallows the failure
            Assert.That(
                async () => await SendAsync(app, accept: "text/html"),
                Throws.Exception);
        }

        private static async Task<HttpResponseMessage> SendAsync(WebApplication app, string? accept)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/boom", UriKind.Relative));

            if (accept is not null)
            {
                request.Headers.Add("Accept", accept);
            }

            return await app.GetTestClient()
                .SendAsync(request, TestContext.CurrentContext.CancellationToken);
        }

        private static async Task<JsonElement> ParseAsync(HttpResponseMessage response)
        {
            string body = await response.Content.ReadAsStringAsync(
                TestContext.CurrentContext.CancellationToken);

            return JsonDocument.Parse(body).RootElement;
        }

        /// <summary>
        /// A consumer-owned handler answering every exception itself.
        /// </summary>
        private sealed class ConsumerExceptionHandler : IExceptionHandler
        {
            public const string Marker = "consumer-handled";

            public async ValueTask<bool> TryHandleAsync(
                HttpContext httpContext,
                Exception exception,
                CancellationToken cancellationToken)
            {
                httpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
                await httpContext.Response.WriteAsync(Marker, cancellationToken);

                return true;
            }
        }
    }
}
