namespace Cloudstrap.WebApi.Tests
{
    using System.Net;
    using System.Text.Json;
    using Cloudstrap.WebApi.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Diagnostics;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;

    /// <summary>
    /// AC-W6 and AC-W7: an unhandled exception produces an RFC 9457 <c>application/problem+json</c> response
    /// in every environment — generic by default, fully diagnosable on explicit opt-in, correlated, logged
    /// once, and never re-executing a dead error path.
    /// </summary>
    [TestFixture]
    public sealed class ProblemDetailsExceptionTests
    {
        [Test]
        public async Task Throwing_InProduction_Returns500ProblemJsonWithoutAnyExceptionDetail()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync();

            // Act
            using HttpResponseMessage response = await Boom(app);
            string body = await response.Content.ReadAsStringAsync(TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
                Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
                Assert.That(body, Does.Contain("\"title\""));
                Assert.That(body, Does.Contain("\"status\":500"));
                Assert.That(body, Does.Not.Contain("InvalidOperationException"));
                Assert.That(body, Does.Not.Contain(BoomController.RootCauseMessage));
                Assert.That(body, Does.Not.Contain("failure level"));
                Assert.That(body, Does.Not.Contain("stackTrace"));
            });
        }

        [Test]
        public async Task Throwing_InProduction_LogsTheExceptionExactlyOnce()
        {
            // Arrange
            CapturingLoggerProvider logs = new();
            await using WebApplication app = await WebApiTestHost.StartAsync(
                beforeBuild: builder => builder.Logging.AddProvider(logs));

            // Act
            using HttpResponseMessage response = await Boom(app);

            // Assert
            CapturedLogEntry[] errors =
            [
                .. logs.Entries.Where(entry =>
                    entry.Level == LogLevel.Error
                    && entry.Exception is not null
                    && entry.Category.StartsWith("Cloudstrap.WebApi", StringComparison.Ordinal)),
            ];

            Assert.That(errors, Has.Length.EqualTo(1));
        }

        [Test]
        public async Task Throwing_InDevelopment_IncludesTypeMessageAndStackTrace()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync(environment: "Development");

            // Act
            JsonElement problem = await BoomPayload(app);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(
                    problem.GetProperty("exceptionType").GetString(),
                    Is.EqualTo(typeof(InvalidOperationException).FullName));
                Assert.That(problem.GetProperty("exceptionMessage").GetString(), Is.EqualTo("failure level 8"));
                Assert.That(problem.GetProperty("stackTrace").GetString(), Is.Not.Empty);
            });
        }

        [Test]
        public async Task Throwing_InDevelopment_IncludesADepthBoundedInnerChain()
        {
            // Arrange — the fixture throws an eight-deep chain; the documented bound is five
            await using WebApplication app = await WebApiTestHost.StartAsync(environment: "Development");

            // Act
            JsonElement problem = await BoomPayload(app);
            JsonElement chain = problem.GetProperty("innerExceptions");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(chain.GetArrayLength(), Is.EqualTo(5));
                Assert.That(
                    chain[0].GetProperty("message").GetString(),
                    Is.EqualTo("failure level 7"));
                Assert.That(
                    chain.EnumerateArray().Select(entry => entry.GetProperty("message").GetString()),
                    Does.Not.Contain(BoomController.RootCauseMessage));
            });
        }

        [Test]
        public async Task Throwing_WithIncludeDetailsFalseInDevelopment_ExplicitWins()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:WebApi:ExceptionHandling:IncludeDetails"] = "false",
                },
                environment: "Development");

            // Act
            using HttpResponseMessage response = await Boom(app);
            string body = await response.Content.ReadAsStringAsync(TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.That(body, Does.Not.Contain("exceptionType"));
        }

        [Test]
        public async Task Throwing_WithIncludeDetailsTrueInProduction_ExplicitWins()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:WebApi:ExceptionHandling:IncludeDetails"] = "true",
                });

            // Act
            JsonElement problem = await BoomPayload(app);

            // Assert
            Assert.That(
                problem.GetProperty("exceptionType").GetString(),
                Is.EqualTo(typeof(InvalidOperationException).FullName));
        }

        [Test]
        public async Task Throwing_IncludesTheAmbientCorrelationId()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync();
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/api/boom", UriKind.Relative));
            request.Headers.Add("X-Correlation-ID", "contoso-be12");

            // Act
            using HttpResponseMessage response = await app.GetTestClient()
                .SendAsync(request, TestContext.CurrentContext.CancellationToken);
            string body = await response.Content.ReadAsStringAsync(TestContext.CurrentContext.CancellationToken);

            // Assert — a caller can quote this identifier in a support request
            Assert.That(
                JsonDocument.Parse(body).RootElement.GetProperty("correlationId").GetString(),
                Is.EqualTo("contoso-be12"));
        }

        [Test]
        public async Task Throwing_WithNoInboundCorrelationHeader_EchoesTheGeneratedIdentifier()
        {
            // Arrange — the caller sent nothing, so the identifier in the response is the generated one the
            // correlation middleware stored on the request
            await using WebApplication app = await WebApiTestHost.StartAsync();

            // Act
            JsonElement problem = await BoomPayload(app);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(problem.GetProperty("status").GetInt32(), Is.EqualTo(500));
                Assert.That(problem.GetProperty("correlationId").GetString(), Is.Not.Empty);
            });
        }

        [Test]
        public async Task Throwing_WithAConfiguredCorrelationHeaderName_EchoesThatHeader()
        {
            // Arrange — the header name is deliberately read from #2's shipped contract, never hard-coded
            await using WebApplication app = await WebApiTestHost.StartAsync(
                new Dictionary<string, string?> { ["Cloudstrap:Correlation:HeaderName"] = "X-Contoso-Trace" });
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/api/boom", UriKind.Relative));
            request.Headers.Add("X-Contoso-Trace", "contoso-5d90");

            // Act
            using HttpResponseMessage response = await app.GetTestClient()
                .SendAsync(request, TestContext.CurrentContext.CancellationToken);
            string body = await response.Content.ReadAsStringAsync(TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.That(
                JsonDocument.Parse(body).RootElement.GetProperty("correlationId").GetString(),
                Is.EqualTo("contoso-5d90"));
        }

        [Test]
        public async Task Throwing_WithAConsumerExceptionHandlerRegisteredFirst_TheConsumerHandlerWins()
        {
            // Arrange — registration order gives a consumer's handler the first attempt
            await using WebApplication app = await WebApiTestHost.StartAsync(
                beforeBuild: builder => builder.Services.AddExceptionHandler<ConsumerExceptionHandler>());

            // Act
            using HttpResponseMessage response = await Boom(app);
            string body = await response.Content.ReadAsStringAsync(TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
                Assert.That(body, Is.EqualTo(ConsumerExceptionHandler.Body));
                Assert.That(body, Does.Not.Contain("\"title\""));
            });
        }

        [Test]
        public async Task Throwing_WithExceptionHandlerPathConfigured_DoesNotReExecuteIt()
        {
            // Arrange — the source's re-execution path stays dead by design
            await using WebApplication app = await WebApiTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:Application:ExceptionHandlerPath"] = "/error",
                });

            // Act
            using HttpResponseMessage response = await Boom(app);
            string body = await response.Content.ReadAsStringAsync(TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
                Assert.That(body, Does.Not.Contain(ErrorController.Marker));
            });
        }

        [Test]
        public void ExceptionHandlingSettings_DefaultsToFollowingTheEnvironment()
        {
            // Arrange
            ExceptionHandlingSettings settings = new();

            // Act + Assert — null means "Development only", which is resolved per request
            Assert.That(settings.IncludeDetails, Is.Null);
        }

        private static async Task<HttpResponseMessage> Boom(WebApplication app)
        {
            return await app.GetTestClient().GetAsync(
                new Uri("/api/boom", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
        }

        private static async Task<JsonElement> BoomPayload(WebApplication app)
        {
            using HttpResponseMessage response = await Boom(app);
            string body = await response.Content.ReadAsStringAsync(TestContext.CurrentContext.CancellationToken);

            return JsonDocument.Parse(body).RootElement.Clone();
        }

        private sealed class ConsumerExceptionHandler : IExceptionHandler
        {
            public const string Body = "handled-by-the-consumer";

            public async ValueTask<bool> TryHandleAsync(
                HttpContext httpContext,
                Exception exception,
                CancellationToken cancellationToken)
            {
                httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
                await httpContext.Response.WriteAsync(Body, cancellationToken);

                return true;
            }
        }
    }
}
