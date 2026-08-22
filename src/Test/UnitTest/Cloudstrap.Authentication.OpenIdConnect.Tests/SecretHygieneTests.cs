namespace Cloudstrap.Authentication.OpenIdConnect.Tests
{
    using System.Diagnostics;
    using System.Net;
    using System.Text.Json;
    using Cloudstrap.Authentication.OpenIdConnect.Tests.Infrastructure;
    using Microsoft.IdentityModel.Logging;
    using NUnit.Framework;

    /// <summary>
    /// AC-OIDC7: through a full successful login, a failing one, and everything the app emits — logs
    /// at Debug, exported telemetry, problem-details bodies and exception text — no secret,
    /// authorization code, token or PII appears.
    /// </summary>
    [TestFixture]
    public sealed class SecretHygieneTests
    {
        [Test]
        public async Task SuccessfulLogin_LeaksNothingAcrossAllOutputChannels()
        {
            // Arrange
            await using HygieneHost host = await HygieneHost.StartAsync();
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act — a full round trip with a configured secret, everything captured at Debug
            using HttpResponseMessage final = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);
            string authorizationCode = ExtractAuthorizationCode(agent);
            (string accessToken, string refreshToken, string idToken) = await ReadSessionTokensAsync(agent);
            host.FlushTelemetry();

            string[] sensitiveValues =
                [OidcTestHost.ClientSecret, authorizationCode, accessToken, refreshToken, idToken];
            List<string> logMessages = [.. host.Logs.Entries.Select(static entry => entry.Message)];
            List<string> activityTexts = CollectActivityTexts(host.Activities);
            List<string> applicationBodies = await CollectApplicationBodiesAsync(agent);

            // Assert — none of the values appears in any log message, activity tag/event, or
            // application response body; and nothing verifier-shaped is emitted anywhere
            Assert.Multiple(() =>
            {
                Assert.That(final.StatusCode, Is.EqualTo(HttpStatusCode.OK));

                foreach (string sensitive in sensitiveValues)
                {
                    Assert.That(logMessages, Has.None.Contains(sensitive), "A log message leaked a value.");
                    Assert.That(activityTexts, Has.None.Contains(sensitive), "An activity leaked a value.");
                    Assert.That(applicationBodies, Has.None.Contains(sensitive), "A response body leaked a value.");
                }

                Assert.That(logMessages, Has.None.Contains("code_verifier="));
                Assert.That(activityTexts, Has.None.Contains("code_verifier="));
            });
        }

        [Test]
        public async Task FailedLogin_LeaksNothingAndYieldsAnIdentifiedError()
        {
            // Arrange — complete the provider round trip, then tamper the code before the callback so
            // the code exchange fails mid-flow
            await using HygieneHost host = await HygieneHost.StartAsync();
            using BrowserlessUserAgent agent = host.CreateAgent();
            using HttpResponseMessage loginForm =
                await agent.GetAsync(new Uri(OidcTestHost.AppBase, "protected/page"));
            using HttpResponseMessage formPostPage = await agent.SubmitLoginFormAsync(
                loginForm,
                OidcTestHost.Username,
                OidcTestHost.Password);
            string formHtml = await formPostPage.Content.ReadAsStringAsync();
            Dictionary<string, string> fields = BrowserlessUserAgent.ParseInputFields(formHtml);
            string realCode = fields["code"];
            fields["code"] = "tampered-not-a-real-code";

            // Act
            using HttpRequestMessage callback = new(
                HttpMethod.Post,
                BrowserlessUserAgent.ResolveFormAction(
                    formHtml,
                    formPostPage.RequestMessage!.RequestUri!))
            {
                Content = new FormUrlEncodedContent(fields),
            };
            using HttpResponseMessage response = await agent.SendAsync(callback);
            string body = await response.Content.ReadAsStringAsync();
            host.FlushTelemetry();

            List<string> logMessages = [.. host.Logs.Entries.Select(static entry => entry.Message)];
            List<string> activityTexts = CollectActivityTexts(host.Activities);

            // Assert — an identified error, with no secret, code or token anywhere
            Assert.Multiple(() =>
            {
                Assert.That(response.IsSuccessStatusCode, Is.False);
                Assert.That(body, Is.Not.Empty, "The failure must surface as an identified error.");
                Assert.That(body, Does.Not.Contain(OidcTestHost.ClientSecret));
                Assert.That(body, Does.Not.Contain(realCode));
                Assert.That(logMessages, Has.None.Contains(OidcTestHost.ClientSecret));
                Assert.That(logMessages, Has.None.Contains(realCode));
                Assert.That(activityTexts, Has.None.Contains(OidcTestHost.ClientSecret));
                Assert.That(activityTexts, Has.None.Contains(realCode));
            });
        }

        [Test]
        public async Task AuthorizationCodeNeverAppearsInAUrl()
        {
            // Arrange
            await using HygieneHost host = await HygieneHost.StartAsync();
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act — the callback arrives by form_post, so nothing code-shaped can reach an access log
            // or #2's request telemetry
            using HttpResponseMessage final = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);

            List<Uri> applicationRequestUris = [.. agent.Responses
                .Select(static response => response.RequestMessage?.RequestUri)
                .Where(static uri => uri is not null
                    && string.Equals(uri.Authority, OidcTestHost.AppBase.Authority, StringComparison.OrdinalIgnoreCase))
                .Select(static uri => uri!)];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(final.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(applicationRequestUris, Is.Not.Empty);

                foreach (Uri uri in applicationRequestUris)
                {
                    bool carriesCodeParameter = uri.Query.TrimStart('?')
                        .Split('&', StringSplitOptions.RemoveEmptyEntries)
                        .Any(static pair => pair.StartsWith("code=", StringComparison.OrdinalIgnoreCase));
                    Assert.That(
                        carriesCodeParameter,
                        Is.False,
                        () => "An application URL carried an authorization code: " + uri.PathAndQuery);
                }
            });
        }

        [Test]
        public async Task ShowPii_IsNeverEnabledByCloudstrap()
        {
            // Arrange
            await using HygieneHost host = await HygieneHost.StartAsync();
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act — a full registration and a full login
            using HttpResponseMessage final = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);

            // Assert — Cloudstrap never turns the IdentityModel PII switch on
            // (Deliberate Behavior Change 11 / source finding 4; the repository-wide "documentation
            // only" sweep runs in this step's VERIFY and again in Step 9's identifier sweep)
            Assert.Multiple(() =>
            {
                Assert.That(final.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(IdentityModelEventSource.ShowPII, Is.False);
            });
        }

        [Test]
        public async Task CookieValue_IsNotWrittenToLogsOrTelemetry()
        {
            // Arrange
            await using HygieneHost host = await HygieneHost.StartAsync();
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act
            using HttpResponseMessage final = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);
            string cookieValue = agent.Responses
                .SelectMany(static response =>
                    response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values)
                        ? values
                        : [])
                .Single(static value => value.StartsWith("__Host-Cloudstrap=", StringComparison.Ordinal))
                .Split(';')[0]["__Host-Cloudstrap=".Length..];
            host.FlushTelemetry();

            // Assert — the protected value now carrying the tokens (D-2) reaches no log or span
            Assert.Multiple(() =>
            {
                Assert.That(cookieValue, Is.Not.Empty);
                Assert.That(
                    host.Logs.Entries.Select(static entry => entry.Message),
                    Has.None.Contains(cookieValue));
                Assert.That(CollectActivityTexts(host.Activities), Has.None.Contains(cookieValue));
            });
        }

        /// <summary>
        /// Extracts the authorization code the provider issued from the <c>form_post</c> page in the
        /// agent's trace.
        /// </summary>
        private static string ExtractAuthorizationCode(BrowserlessUserAgent agent)
        {
            foreach (HttpResponseMessage response in agent.Responses)
            {
                if (response.RequestMessage?.RequestUri?.AbsolutePath != "/connect/authorize"
                    || response.StatusCode != HttpStatusCode.OK)
                {
                    continue;
                }

                string html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Dictionary<string, string> fields = BrowserlessUserAgent.ParseInputFields(html);
                if (fields.TryGetValue("code", out string? code))
                {
                    return code;
                }
            }

            throw new InvalidOperationException("No form_post page carrying a code was found in the trace.");
        }

        private static async Task<(string AccessToken, string RefreshToken, string IdToken)>
            ReadSessionTokensAsync(BrowserlessUserAgent agent)
        {
            using HttpResponseMessage response =
                await agent.GetAsync(new Uri(OidcTestHost.AppBase, "protected/tokens"));
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            return (
                document.RootElement.GetProperty("accessToken").GetString()!,
                document.RootElement.GetProperty("refreshToken").GetString()!,
                document.RootElement.GetProperty("idToken").GetString()!);
        }

        /// <summary>
        /// Flattens every exported activity into searchable text: display names, tag values and event
        /// names/tags.
        /// </summary>
        private static List<string> CollectActivityTexts(IEnumerable<Activity> activities)
        {
            List<string> texts = [];

            foreach (Activity activity in activities)
            {
                texts.Add(activity.DisplayName);

                foreach (KeyValuePair<string, object?> tag in activity.TagObjects)
                {
                    texts.Add(tag.Key + "=" + tag.Value);
                }

                foreach (ActivityEvent activityEvent in activity.Events)
                {
                    texts.Add(activityEvent.Name);
                    foreach (KeyValuePair<string, object?> tag in activityEvent.Tags)
                    {
                        texts.Add(tag.Key + "=" + tag.Value);
                    }
                }
            }

            return texts;
        }

        /// <summary>
        /// Reads the body of every application response in the trace — the identity provider's
        /// <c>form_post</c> page legitimately carries the code; the application's own output must not.
        /// The fixture's tokens endpoint is excluded: it exists to hand the test the values to search
        /// for.
        /// </summary>
        private static async Task<List<string>> CollectApplicationBodiesAsync(BrowserlessUserAgent agent)
        {
            List<string> bodies = [];

            foreach (HttpResponseMessage response in agent.Responses)
            {
                Uri? uri = response.RequestMessage?.RequestUri;
                if (uri is null
                    || !string.Equals(uri.Authority, OidcTestHost.AppBase.Authority, StringComparison.OrdinalIgnoreCase)
                    || uri.AbsolutePath == "/protected/tokens")
                {
                    continue;
                }

                bodies.Add(await response.Content.ReadAsStringAsync());
            }

            return bodies;
        }
    }
}
