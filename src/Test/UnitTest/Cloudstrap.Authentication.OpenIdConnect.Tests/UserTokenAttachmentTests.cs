namespace Cloudstrap.Authentication.OpenIdConnect.Tests
{
    using System.Net;
    using Cloudstrap.Authentication.ClientCredentials;
    using Cloudstrap.Authentication.OpenIdConnect.Tests.Infrastructure;
    using Cloudstrap.Core;
    using Cloudstrap.Extensions;
    using Cloudstrap.Observability.Correlation;
    using Duende.AccessTokenManagement.OpenIdConnect;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.IdentityModel.JsonWebTokens;
    using NUnit.Framework;

    /// <summary>
    /// AC-OIDC3, AC-OIDC8 and the AC-CC13 re-proof: a flagged typed client calls a downstream API
    /// <em>as the signed-in user</em>, two users never see each other's token, no signed-in user means
    /// no request at all, and a client flagged for both token kinds sends the user's token.
    /// </summary>
    [TestFixture]
    public sealed class UserTokenAttachmentTests
    {
        private const string _catalogBaseAddress = "https://catalog.contoso.example/";

        [Test]
        public async Task FlaggedClient_AfterSignIn_CarriesTheSignedInUsersAccessToken()
        {
            // Arrange — the shipped typed-client registration with only the configuration flag; no
            // consumer code change beyond the registration call
            CapturingPrimaryHandler capturing = new();
            await using OidcTestHost host = await StartHostWithCatalogClientAsync(capturing);
            using BrowserlessUserAgent agent = host.CreateAgent();
            using HttpResponseMessage signedIn = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);

            // Act — the endpoint relays exactly the bearer token the peer saw
            using HttpResponseMessage response =
                await agent.GetAsync(new Uri(OidcTestHost.AppBase, "protected/call-catalog"));
            string relayedToken = await response.Content.ReadAsStringAsync();
            JsonWebToken jwt = new JsonWebTokenHandler().ReadJsonWebToken(relayedToken);

            // Assert — the signed-in user's token, minted for the OIDC client (AC-OIDC3)
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(jwt.Subject, Is.EqualTo(OidcTestHost.Username));
                Assert.That(jwt.GetClaim("client_id").Value, Is.EqualTo(OidcTestHost.ClientId));
            });
        }

        [Test]
        public async Task TwoSignedInUsers_NeverObserveEachOthersToken()
        {
            // Arrange — two agents are two users' browsers against the same application
            CapturingPrimaryHandler capturing = new();
            await using OidcTestHost host = await StartHostWithCatalogClientAsync(
                capturing,
                configureIdentityProvider: static options => options.Users.Add(
                    new Cloudstrap.TestIdentityProvider.TestIdentityProviderUser
                    {
                        Username = "contoso.admin",
                        Password = "placeholder-not-a-real-password-2",
                        Claims = { ["name"] = ["Contoso Admin"], ["role"] = ["admin"] },
                    }));
            using BrowserlessUserAgent firstAgent = host.CreateAgent();
            using BrowserlessUserAgent secondAgent = host.CreateAgent();
            using HttpResponseMessage firstSignIn = await firstAgent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);
            using HttpResponseMessage secondSignIn = await secondAgent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                "contoso.admin",
                "placeholder-not-a-real-password-2");

            // Act — parallel requests from both users
            Task<HttpResponseMessage> firstCall =
                firstAgent.GetAsync(new Uri(OidcTestHost.AppBase, "protected/call-catalog"));
            Task<HttpResponseMessage> secondCall =
                secondAgent.GetAsync(new Uri(OidcTestHost.AppBase, "protected/call-catalog"));
            await Task.WhenAll(firstCall, secondCall);
            using HttpResponseMessage firstResponse = firstCall.Result;
            using HttpResponseMessage secondResponse = secondCall.Result;

            JsonWebTokenHandler handler = new();
            string firstSubject = handler
                .ReadJsonWebToken(await firstResponse.Content.ReadAsStringAsync()).Subject;
            string secondSubject = handler
                .ReadJsonWebToken(await secondResponse.Content.ReadAsStringAsync()).Subject;

            // Assert — each request carried its own user's token; the two never cross
            Assert.Multiple(() =>
            {
                Assert.That(firstSubject, Is.EqualTo(OidcTestHost.Username));
                Assert.That(secondSubject, Is.EqualTo("contoso.admin"));
            });
        }

        [Test]
        public async Task UnflaggedClient_IsUntouched()
        {
            // Arrange — the same client registration without the flag
            CapturingPrimaryHandler capturing = new();
            await using OidcTestHost host = await StartHostWithCatalogClientAsync(
                capturing,
                addUserAccessToken: false);
            using BrowserlessUserAgent agent = host.CreateAgent();
            using HttpResponseMessage signedIn = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);

            // Act
            using HttpResponseMessage response =
                await agent.GetAsync(new Uri(OidcTestHost.AppBase, "protected/call-catalog"));

            // Assert
            Assert.That(capturing.LastRequest!.Headers.Authorization, Is.Null);
        }

        [Test]
        public async Task FlaggedClient_StillCarriesTheCorrelationHeader()
        {
            // Arrange
            CapturingPrimaryHandler capturing = new();
            await using OidcTestHost host = await StartHostWithCatalogClientAsync(
                capturing,
                mapEndpoints: static app => app.MapGet(
                    "/protected/call-catalog-correlated",
                    async (ICatalogClient catalog, ICorrelationContextAccessor correlation) =>
                    {
                        correlation.CorrelationId = "corr-oidc";
                        using HttpResponseMessage downstream =
                            await catalog.Client.GetAsync(new Uri("data", UriKind.Relative));

                        return Results.Text(downstream.StatusCode.ToString());
                    }));
            using BrowserlessUserAgent agent = host.CreateAgent();
            using HttpResponseMessage signedIn = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);

            // Act
            using HttpResponseMessage response =
                await agent.GetAsync(new Uri(OidcTestHost.AppBase, "protected/call-catalog-correlated"));

            // Assert — #4's ordering preserved: the correlation header survives downstream of the
            // token handler
            Assert.Multiple(() =>
            {
                Assert.That(capturing.LastRequest!.Headers.Authorization, Is.Not.Null);
                Assert.That(
                    capturing.LastRequest.Headers.GetValues("X-Correlation-ID").Single(),
                    Is.EqualTo("corr-oidc"));
            });
        }

        [Test]
        public async Task FlaggedClient_WithNoSignedInUser_ThrowsNamingTheFlag_AndSendsNothing()
        {
            // Arrange — an anonymous endpoint drives the flagged client with no signed-in user
            CapturingPrimaryHandler capturing = new();
            await using OidcTestHost host = await StartHostWithCatalogClientAsync(
                capturing,
                mapEndpoints: static app => app.MapGet(
                        "/anonymous/call-catalog",
                        async (ICatalogClient catalog) =>
                        {
                            try
                            {
                                using HttpResponseMessage downstream =
                                    await catalog.Client.GetAsync(new Uri("data", UriKind.Relative));

                                return Results.Text("sent:" + downstream.StatusCode);
                            }
                            catch (InvalidOperationException exception)
                            {
                                return Results.Text("failed:" + exception.Message, statusCode: 500);
                            }
                        })
                    .AllowAnonymous());
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act
            using HttpResponseMessage response =
                await agent.GetAsync(new Uri(OidcTestHost.AppBase, "anonymous/call-catalog"));
            string body = await response.Content.ReadAsStringAsync();

            // Assert — a loud failure naming the flag and the alternative; zero requests sent
            Assert.Multiple(() =>
            {
                Assert.That(body, Does.StartWith("failed:"));
                Assert.That(body, Does.Contain("Cloudstrap:HttpClients:Catalog:AddUserAccessToken"));
                Assert.That(body, Does.Contain("no signed-in user"));
                Assert.That(body, Does.Contain("AddClientAccessToken"));
                Assert.That(capturing.RequestCount, Is.Zero);
            });
        }

        [Test]
        public async Task FlaggedClient_FromABackgroundServiceWithNoHttpContext_SameContract()
        {
            // Arrange — the client resolved and driven outside any request (AC-OIDC8's second arm)
            CapturingPrimaryHandler capturing = new();
            await using OidcTestHost host = await StartHostWithCatalogClientAsync(capturing);
            ICatalogClient catalog = host.App.Services.GetRequiredService<ICatalogClient>();

            // Act + Assert
            InvalidOperationException? failure = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await catalog.Client.GetAsync(new Uri("data", UriKind.Relative)));
            Assert.Multiple(() =>
            {
                Assert.That(failure!.Message, Does.Contain("Cloudstrap:HttpClients:Catalog:AddUserAccessToken"));
                Assert.That(failure.Message, Does.Contain("AddClientAccessToken"));
                Assert.That(capturing.RequestCount, Is.Zero);
            });
        }

        [Test]
        public void PerClientScopeAndResource_ReachTheTokenRequest()
        {
            // Arrange — spec finding 10: all five TokenRequestOptions members map onto Duende's
            // UserTokenRequestParameters (three here, the two schemes in the next test)
            TokenRequestOptions options = new()
            {
                Scope = "catalog.read",
                Resource = "https://catalog.contoso.example/",
                ForceRenewal = true,
            };

            // Act
            UserTokenRequestParameters parameters = UserTokenRequestParameterMapper.Map(options);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(parameters.Scope?.ToString(), Is.EqualTo("catalog.read"));
                Assert.That(parameters.Resource?.ToString(), Is.EqualTo("https://catalog.contoso.example/"));
                Assert.That(parameters.ForceTokenRenewal, Is.True);
            });
        }

        [Test]
        public void PerClientSignInAndChallengeScheme_AreHonored()
        {
            // Arrange — the two members #9 ignores with a warning are honored here (spec finding 10)
            TokenRequestOptions options = new()
            {
                SignInScheme = "Cookies",
                ChallengeScheme = "OpenIdConnect",
            };

            // Act
            UserTokenRequestParameters parameters = UserTokenRequestParameterMapper.Map(options);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(parameters.SignInScheme?.ToString(), Is.EqualTo("Cookies"));
                Assert.That(parameters.ChallengeScheme?.ToString(), Is.EqualTo("OpenIdConnect"));
            });
        }

        [Test]
        public async Task Response401_TriggersExactlyOneForcedRenewalThroughTheIntactInnerChain()
        {
            // Arrange — a downstream that 401s the first token and accepts a renewed one, plus a
            // consumer handler added through ConfigureHttpClientDefaults
            UnauthorizedForFirstTokenHandler downstream = new();
            CountingMarkerHandler marker = new();
            await using OidcTestHost host = await OidcTestHost.StartAsync(
                configuration: CatalogConfig(addUserAccessToken: true),
                afterRegistration: (builder, _) =>
                {
                    builder.Services.ConfigureHttpClientDefaults(http => http.AddHttpMessageHandler(() => marker));
                    builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                        .ConfigurePrimaryHttpMessageHandler(() => downstream);
                },
                mapEndpoints: static app => app.MapGet(
                    "/protected/call-catalog",
                    async (ICatalogClient catalog) =>
                    {
                        using HttpResponseMessage response =
                            await catalog.Client.GetAsync(new Uri("data", UriKind.Relative));

                        return Results.Text(response.StatusCode.ToString());
                    }));
            using BrowserlessUserAgent agent = host.CreateAgent();
            using HttpResponseMessage signedIn = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);

            // Act
            using HttpResponseMessage response =
                await agent.GetAsync(new Uri(OidcTestHost.AppBase, "protected/call-catalog"));
            string relayedStatus = await response.Content.ReadAsStringAsync();

            // Assert — two attempts through the intact chain, a different token on the second, success
            Assert.Multiple(() =>
            {
                Assert.That(relayedStatus, Is.EqualTo("OK"));
                Assert.That(downstream.RequestCount, Is.EqualTo(2));
                Assert.That(marker.InvocationCount, Is.EqualTo(2), "The consumer handler runs once per attempt.");
                Assert.That(
                    downstream.SeenBearerTokens[1],
                    Is.Not.EqualTo(downstream.SeenBearerTokens[0]),
                    "The renewal must attach a fresh token.");
            });
        }

        [Test]
        public async Task TicketWithoutStoredTokens_ThrowsNamingSaveTokens()
        {
            // Arrange — the configurator's final say turns SaveTokens off, so the ticket carries no
            // tokens (the spec's edge case)
            CapturingPrimaryHandler capturing = new();
            await using OidcTestHost host = await StartHostWithCatalogClientAsync(
                capturing,
                configure: static configurator => configurator.OpenIdConnect =
                    static oidc => oidc.SaveTokens = false,
                mapEndpoints: static app => app.MapGet(
                    "/protected/call-catalog-caught",
                    async (ICatalogClient catalog) =>
                    {
                        try
                        {
                            using HttpResponseMessage downstream =
                                await catalog.Client.GetAsync(new Uri("data", UriKind.Relative));

                            return Results.Text("sent:" + downstream.StatusCode);
                        }
                        catch (InvalidOperationException exception)
                        {
                            return Results.Text("failed:" + exception.Message, statusCode: 500);
                        }
                    }));
            using BrowserlessUserAgent agent = host.CreateAgent();
            using HttpResponseMessage signedIn = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);

            // Act
            using HttpResponseMessage response =
                await agent.GetAsync(new Uri(OidcTestHost.AppBase, "protected/call-catalog-caught"));
            string body = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(body, Does.StartWith("failed:"));
                Assert.That(body, Does.Contain("SaveTokens"));
                Assert.That(capturing.RequestCount, Is.Zero);
            });
        }

        [Test]
        public async Task ClientFlaggedForBothTokens_GetsBothHandlersUserFirst_AndSendsTheUsersToken()
        {
            // Arrange — both packages registered against the same real provider; one client, both flags
            CapturingPrimaryHandler capturing = new();
            HandlerChainCapture chainCapture = new("Catalog");
            Dictionary<string, string?> configuration = CatalogConfig(addUserAccessToken: true);
            configuration["Cloudstrap:HttpClients:Catalog:AddClientAccessToken"] = "true";
            configuration["Cloudstrap:ClientCredentials:TokenEndpoint"] =
                new Uri(OidcTestHost.IdpBase, "connect/token").AbsoluteUri;
            configuration["Cloudstrap:ClientCredentials:ClientId"] = "contoso-service";
            configuration["Cloudstrap:ClientCredentials:ClientSecret"] = "placeholder-not-a-real-secret";
            configuration["Cloudstrap:ClientCredentials:Scope"] = "catalog.read";

            await using OidcTestHost host = await OidcTestHost.StartAsync(
                configuration: configuration,
                configureIdentityProvider: static options => options.Clients.Add(
                    new Cloudstrap.TestIdentityProvider.TestIdentityProviderClient
                    {
                        ClientId = "contoso-service",
                        ClientSecret = "placeholder-not-a-real-secret",
                        Scopes = { "catalog.read" },
                        Audiences = { "contoso-api" },
                    }),
                afterRegistration: (builder, identityProvider) =>
                {
                    builder.Services.AddSingleton<Microsoft.Extensions.Http.IHttpMessageHandlerBuilderFilter>(
                        chainCapture);
                    builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                        .ConfigurePrimaryHttpMessageHandler(() => capturing);
                    builder.Services.AddCloudstrapClientCredentials(configurator => configurator.Backchannel =
                        http => http.ConfigurePrimaryHttpMessageHandler(identityProvider.CreateHandler));
                },
                mapEndpoints: static app => app.MapGet(
                    "/protected/call-catalog",
                    async (ICatalogClient catalog) =>
                    {
                        using HttpResponseMessage response =
                            await catalog.Client.GetAsync(new Uri("data", UriKind.Relative));

                        return Results.Text(await response.Content.ReadAsStringAsync());
                    }));

            using BrowserlessUserAgent agent = host.CreateAgent();
            using HttpResponseMessage signedIn = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);
            int tokenRequestsBeforeCall = host.IdentityProvider.TokenRequestCount;

            // Act
            using HttpResponseMessage response =
                await agent.GetAsync(new Uri(OidcTestHost.AppBase, "protected/call-catalog"));
            string relayedToken = await response.Content.ReadAsStringAsync();
            JsonWebToken jwt = new JsonWebTokenHandler().ReadJsonWebToken(relayedToken);

            List<string> handlerTypeNames = [.. chainCapture.HandlerTypes.Select(static type => type.Name)];
            int userHandlerIndex = handlerTypeNames.IndexOf("UserTokenHandler");
            int clientHandlerIndex = handlerTypeNames.IndexOf("ClientCredentialsTokenHandler");

            // Assert — both providers materialized their handler on the one client, user first, and
            // the token that reached the peer is the USER's (AC-CC13: user first now means the user's
            // token arrives). Per the non-clobber rule, no machine token is acquired for a request the
            // user handler already authenticated.
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(userHandlerIndex, Is.GreaterThanOrEqualTo(0), "The user handler must be in the chain.");
                Assert.That(
                    clientHandlerIndex,
                    Is.GreaterThanOrEqualTo(0),
                    "The client-credentials handler must be in the chain.");
                Assert.That(userHandlerIndex, Is.LessThan(clientHandlerIndex), "User first (AC-CC13).");
                Assert.That(jwt.Subject, Is.EqualTo(OidcTestHost.Username));
                Assert.That(jwt.GetClaim("client_id").Value, Is.EqualTo(OidcTestHost.ClientId));
                Assert.That(host.IdentityProvider.TokenRequestCount, Is.EqualTo(tokenRequestsBeforeCall));
            });
        }

        /// <summary>
        /// The standard <c>Cloudstrap:HttpClients:Catalog</c> section.
        /// </summary>
        private static Dictionary<string, string?> CatalogConfig(bool addUserAccessToken)
        {
            Dictionary<string, string?> configuration = OidcTestHost.DefaultConfig();
            configuration["Cloudstrap:HttpClients:Catalog:BaseAddress"] = _catalogBaseAddress;
            configuration["Cloudstrap:HttpClients:Catalog:AddUserAccessToken"] =
                addUserAccessToken ? "true" : "false";

            return configuration;
        }

        /// <summary>
        /// Starts the fixture with the flagged <c>Catalog</c> client registered through the shipped
        /// <c>AddCloudstrapHttpServiceClient</c> and a relay endpoint that returns the bearer token the
        /// downstream peer saw.
        /// </summary>
        private static Task<OidcTestHost> StartHostWithCatalogClientAsync(
            CapturingPrimaryHandler capturing,
            bool addUserAccessToken = true,
            Action<CloudstrapOpenIdConnectConfigurator>? configure = null,
            Action<Cloudstrap.TestIdentityProvider.TestIdentityProviderOptions>? configureIdentityProvider = null,
            Action<WebApplication>? mapEndpoints = null) =>
            OidcTestHost.StartAsync(
                configuration: CatalogConfig(addUserAccessToken),
                configure: configure,
                configureIdentityProvider: configureIdentityProvider,
                afterRegistration: (builder, _) =>
                    builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                        .ConfigurePrimaryHttpMessageHandler(() => capturing),
                mapEndpoints: app =>
                {
                    app.MapGet("/protected/call-catalog", async (ICatalogClient catalog) =>
                    {
                        using HttpResponseMessage response =
                            await catalog.Client.GetAsync(new Uri("data", UriKind.Relative));

                        return Results.Text(await response.Content.ReadAsStringAsync());
                    });
                    mapEndpoints?.Invoke(app);
                });
    }
}
