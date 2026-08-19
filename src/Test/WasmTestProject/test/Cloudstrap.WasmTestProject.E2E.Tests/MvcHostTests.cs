namespace Cloudstrap.WasmTestProject.E2E.Tests
{
    using System.Net;
    using System.Text.Json;
    using Cloudstrap.WasmTestProject.E2E.Tests.Infrastructure;
    using Microsoft.Playwright;
    using NUnit.Framework;

    /// <summary>
    /// The demonstration slice of deliverable #6 (AC-MVC13): a separate MVC host built on nothing but
    /// <c>Cloudstrap.Mvc</c>'s two calls — the README consumer example, live — serves a session-backed
    /// visit counter and the hardened browser error page, proven through a real Chromium while every
    /// existing E2E fixture stays untouched on its own ports.
    /// </summary>
    [TestFixture]
    public sealed class MvcHostTests : PageTestBase
    {
        private const string _mvcBaseUrl = "http://127.0.0.1:5320";

        private const string _mvcHostProjectPath =
            "src/Test/WasmTestProject/src/Host/Mvc/Cloudstrap.WasmTestProject.Host.Mvc.csproj";

        private SutProcess? _mvcHost;

        [OneTimeSetUp]
        public async Task StartMvcHostAsync()
        {
            _mvcHost = SutProcess.Start(_mvcBaseUrl, null, _mvcHostProjectPath);
            using HttpClient client = new HttpClient { BaseAddress = new Uri(_mvcBaseUrl) };

            // Readiness polls /healthz — which is itself AC-MVC1's probe clause, live.
            await WaitUntilReadyAsync(client, _mvcHost, "/healthz");
        }

        [OneTimeTearDown]
        public void StopMvcHost()
        {
            _mvcHost?.Dispose();
        }

        [Test]
        public async Task MvcHost_VisitCounter_RoundTripsWithTheHardenedSessionCookie()
        {
            // Act — two navigations: the session round-trips through a real browser
            await Page.GotoAsync(_mvcBaseUrl + "/");
            await Assertions.Expect(Page.GetByTestId("visit-count")).ToHaveTextAsync("1");
            await Page.ReloadAsync();
            await Assertions.Expect(Page.GetByTestId("visit-count")).ToHaveTextAsync("2");

            // Assert — exactly one hardened session cookie, inspected in the browser itself (the
            // Secure cookie works over http://127.0.0.1 because loopback is a trustworthy origin)
            IReadOnlyList<BrowserContextCookiesResult> cookies = await Page.Context.CookiesAsync();
            BrowserContextCookiesResult session = cookies.Single();
            Assert.Multiple(() =>
            {
                Assert.That(session.Name, Is.EqualTo(".Cloudstrap.Session"));
                Assert.That(session.Secure, Is.True);
                Assert.That(session.HttpOnly, Is.True);
                Assert.That(session.SameSite, Is.EqualTo(SameSiteAttribute.Lax));
            });
        }

        [Test]
        public async Task MvcHost_ThrowingAction_ShowsTheConsumersErrorPageNotAStackTrace()
        {
            // Act
            await Page.GotoAsync(_mvcBaseUrl + "/home/boom");

            // Assert — the neutral error page, with none of the exception surfacing in the browser
            await Assertions.Expect(Page.GetByTestId("error-page")).ToBeVisibleAsync();
            string content = await Page.ContentAsync();
            Assert.Multiple(() =>
            {
                Assert.That(content, Does.Not.Contain("InvalidOperationException"));
                Assert.That(content, Does.Not.Contain("demo root cause"));
                Assert.That(content, Does.Not.Contain("at Cloudstrap"));
            });
        }

        [Test]
        public async Task MvcHost_ThrowingAction_PreferringJson_GetsGenericProblemDetails()
        {
            // Arrange
            using HttpClient client = new HttpClient { BaseAddress = new Uri(_mvcBaseUrl) };
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/home/boom", UriKind.Relative));
            request.Headers.Add("Accept", "application/json");

            // Act
            using HttpResponseMessage response = await client.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();
            JsonElement problem = JsonDocument.Parse(body).RootElement;

            // Assert — IncludeDetails is pinned false in the host, so the payload stays generic
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
                Assert.That(
                    response.Content.Headers.ContentType?.MediaType,
                    Is.EqualTo("application/problem+json"));
                Assert.That(problem.GetProperty("status").GetInt32(), Is.EqualTo(500));
                Assert.That(problem.TryGetProperty("exceptionType", out _), Is.False);
                Assert.That(body, Does.Not.Contain("demo root cause"));
            });
        }

        [Test]
        public async Task MvcHost_HomePage_LoadsWithStaticAssetsAndNoConsoleErrors()
        {
            // Arrange
            using HttpClient client = new HttpClient { BaseAddress = new Uri(_mvcBaseUrl) };

            // Act — the real .cshtml view over the conventional route, with the stylesheet it links
            await Page.GotoAsync(_mvcBaseUrl + "/");
            using HttpResponseMessage stylesheet = await client.GetAsync(
                new Uri("/site.css", UriKind.Relative));

            // Assert
            await Assertions.Expect(Page.GetByTestId("visit-count")).ToBeVisibleAsync();
            Assert.Multiple(() =>
            {
                Assert.That(stylesheet.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(stylesheet.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/css"));
                Assert.That(ConsoleErrors, Is.Empty, string.Join(Environment.NewLine, ConsoleErrors));
            });
        }

        private static async Task WaitUntilReadyAsync(HttpClient client, SutProcess process, string probePath)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    break;
                }

                try
                {
                    using HttpResponseMessage response = await client.GetAsync(
                        new Uri(probePath, UriKind.Relative));
                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                    // Not listening yet — keep polling until the deadline.
                }

                await Task.Delay(250);
            }

            throw new InvalidOperationException(
                $"The MVC host did not become ready at {client.BaseAddress}.{Environment.NewLine}{process.CapturedOutput}");
        }
    }
}
