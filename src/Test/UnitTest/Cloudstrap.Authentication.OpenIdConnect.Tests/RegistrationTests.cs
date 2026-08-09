namespace Cloudstrap.Authentication.OpenIdConnect.Tests
{
    using System.Net;
    using Cloudstrap.Authentication.OpenIdConnect.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;

    /// <summary>
    /// AC-OIDC6 and AC-OIDC10: a misconfigured section fails startup naming the exact key — never
    /// echoing a value — and a second registration changes nothing.
    /// </summary>
    [TestFixture]
    public sealed class RegistrationTests
    {
        [Test]
        public async Task MissingSection_FailsStartupNamingTheSection()
        {
            // Act
            OptionsValidationException failure = await CaptureStartupFailureAsync([]);

            // Assert
            Assert.That(failure.Message, Does.Contain("Cloudstrap:OpenIdConnect"));
        }

        [Test]
        public async Task MissingAuthority_FailsNamingTheKey()
        {
            // Act
            OptionsValidationException failure = await CaptureStartupFailureAsync(new Dictionary<string, string?>
            {
                ["Cloudstrap:OpenIdConnect:ClientId"] = OidcTestHost.ClientId,
            });

            // Assert
            Assert.That(failure.Message, Does.Contain("Cloudstrap:OpenIdConnect:Authority"));
        }

        [Test]
        public async Task MissingClientId_FailsNamingTheKey()
        {
            // Act
            OptionsValidationException failure = await CaptureStartupFailureAsync(new Dictionary<string, string?>
            {
                ["Cloudstrap:OpenIdConnect:Authority"] = OidcTestHost.IdpBase.AbsoluteUri,
            });

            // Assert
            Assert.That(failure.Message, Does.Contain("Cloudstrap:OpenIdConnect:ClientId"));
        }

        [Test]
        public async Task RelativeAuthority_FailsNamingTheKey()
        {
            // Act
            OptionsValidationException failure = await CaptureStartupFailureAsync(new Dictionary<string, string?>
            {
                ["Cloudstrap:OpenIdConnect:Authority"] = "idp.example.com/oidc",
                ["Cloudstrap:OpenIdConnect:ClientId"] = OidcTestHost.ClientId,
            });

            // Assert
            Assert.That(failure.Message, Does.Contain("Cloudstrap:OpenIdConnect:Authority"));
        }

        [Test]
        public async Task NonPositiveCookieLifetime_FailsNamingTheKey()
        {
            // Act
            OptionsValidationException failure = await CaptureStartupFailureAsync(new Dictionary<string, string?>
            {
                ["Cloudstrap:OpenIdConnect:Authority"] = OidcTestHost.IdpBase.AbsoluteUri,
                ["Cloudstrap:OpenIdConnect:ClientId"] = OidcTestHost.ClientId,
                ["Cloudstrap:OpenIdConnect:Cookie:Lifetime"] = "00:00:00",
            });

            // Assert
            Assert.That(failure.Message, Does.Contain("Cloudstrap:OpenIdConnect:Cookie:Lifetime"));
        }

        [Test]
        public async Task ValidationFailure_NeverEchoesTheConfiguredSecret()
        {
            // Arrange — a secret is configured; a different key is broken
            OptionsValidationException failure = await CaptureStartupFailureAsync(new Dictionary<string, string?>
            {
                ["Cloudstrap:OpenIdConnect:ClientSecret"] = OidcTestHost.ClientSecret,
            });

            // Assert — the failure names keys, never values (AC-OIDC6, AC-OIDC7)
            Assert.That(failure.Message, Does.Not.Contain(OidcTestHost.ClientSecret));
        }

        [Test]
        public async Task NoClientSecretConfigured_StartsAndChallengesNormally()
        {
            // Arrange — the secret is optional (D-3): omit it entirely
            Dictionary<string, string?> configuration = OidcTestHost.DefaultConfig();
            configuration.Remove("Cloudstrap:OpenIdConnect:ClientSecret");

            await using OidcTestHost host = await OidcTestHost.StartAsync(configuration);
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act
            using HttpResponseMessage challenge =
                await agent.GetNoRedirectAsync(new Uri(OidcTestHost.AppBase, "protected/page"));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(challenge.StatusCode, Is.EqualTo(HttpStatusCode.Found));
                Assert.That(challenge.Headers.Location!.Authority, Is.EqualTo(OidcTestHost.IdpBase.Authority));
            });
        }

        [Test]
        public async Task CalledTwice_RegistersEverythingOnce()
        {
            // Arrange — the descriptor-level half: a second call adds nothing at all
            ServiceCollection services = new();
            services.AddCloudstrapOpenIdConnect();
            int descriptorCount = services.Count;
            services.AddCloudstrapOpenIdConnect();

            // The behavioral half: a host registering twice still signs in once, with one session cookie
            await using OidcTestHost host = await OidcTestHost.StartAsync(
                afterRegistration: static (builder, _) => builder.Services.AddCloudstrapOpenIdConnect());
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act
            using HttpResponseMessage final = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);
            int sessionCookieCount = agent.Responses
                .SelectMany(static response =>
                    response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values)
                        ? values
                        : [])
                .Count(static value => value.StartsWith("__Host-Cloudstrap=", StringComparison.Ordinal));

            // Assert (AC-OIDC10)
            Assert.Multiple(() =>
            {
                Assert.That(services.Count, Is.EqualTo(descriptorCount));
                Assert.That(final.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(sessionCookieCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void OnNullServices_ThrowsArgumentNullException()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(static () =>
                ((IServiceCollection)null!).AddCloudstrapOpenIdConnect());
        }

        [Test]
        public async Task RequireAuthenticatedEndpointsFalse_LeavesEndpointsAnonymous()
        {
            // Arrange — D-6's documented whole-application opt-out
            await using OidcTestHost host = await OidcTestHost.StartAsync(new Dictionary<string, string?>
            {
                ["Cloudstrap:OpenIdConnect:RequireAuthenticatedEndpoints"] = "false",
            });
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act — an unannotated endpoint, no cookie
            using HttpResponseMessage response =
                await agent.GetNoRedirectAsync(new Uri(OidcTestHost.AppBase, "protected/page"));

            // Assert — served anonymously rather than challenged
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        /// <summary>
        /// Builds an application around the given configuration and captures the startup validation
        /// failure <c>ValidateOnStart</c> produces.
        /// </summary>
        /// <param name="configuration">The <c>Cloudstrap:OpenIdConnect</c> entries, possibly broken.</param>
        /// <returns>The captured failure.</returns>
        private static async Task<OptionsValidationException> CaptureStartupFailureAsync(
            Dictionary<string, string?> configuration)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Production",
                ApplicationName = "Cloudstrap.Authentication.OpenIdConnect.Tests",
            });
            builder.Configuration.AddInMemoryCollection(configuration);
            builder.WebHost.UseTestServer();
            builder.Services.AddCloudstrapOpenIdConnect();

            await using WebApplication app = builder.Build();

            try
            {
                await app.StartAsync(TestContext.CurrentContext.CancellationToken);
            }
            catch (OptionsValidationException exception)
            {
                return exception;
            }

            throw new InvalidOperationException("Startup unexpectedly succeeded with broken configuration.");
        }
    }
}
