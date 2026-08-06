namespace Cloudstrap.TestIdentityProvider
{
    using System.Security.Claims;
    using Microsoft.AspNetCore;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Options;
    using OpenIddict.Abstractions;
    using OpenIddict.Server.AspNetCore;
    using static OpenIddict.Abstractions.OpenIddictConstants;

    /// <summary>
    /// Maps the test identity provider's token endpoint. Discovery and JWKS are served by OpenIddict
    /// itself at their standard well-known paths; only the token endpoint passes through to this handler.
    /// </summary>
    public static class EndpointRouteBuilderExtensions
    {
        /// <summary>
        /// Maps the token endpoint (<c>/connect/token</c>) of the test identity provider.
        /// </summary>
        /// <param name="endpoints">The endpoint route builder to map into.</param>
        /// <returns>The endpoint route builder, so further mappings can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
        public static IEndpointRouteBuilder MapCloudstrapTestIdentityProvider(this IEndpointRouteBuilder endpoints)
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            endpoints.MapPost("/connect/token", HandleTokenRequest);

            return endpoints;
        }

        /// <summary>
        /// Issues the client-credentials token. OpenIddict has already authenticated the client — a
        /// wrong or unknown credential never reaches this handler — so all that remains is shaping the
        /// principal the token is generated from.
        /// </summary>
        /// <param name="context">The HTTP context of the validated token request.</param>
        /// <returns>The sign-in result OpenIddict turns into a token response.</returns>
        private static IResult HandleTokenRequest(HttpContext context)
        {
            OpenIddictRequest request = context.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("The request is not handled by the OpenIddict server.");

            if (!request.IsClientCredentialsGrantType())
            {
                throw new NotSupportedException(
                    "The client-credentials grant is the only grant type this test identity provider supports.");
            }

            TestIdentityProviderOptions options = context.RequestServices
                .GetRequiredService<IOptions<TestIdentityProviderOptions>>().Value;
            TestIdentityProviderClient client = options.Clients.First(candidate =>
                string.Equals(candidate.ClientId, request.ClientId, StringComparison.Ordinal));

            ClaimsIdentity identity = new(
                authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                nameType: Claims.Name,
                roleType: Claims.Role);
            identity.SetClaim(Claims.Subject, request.ClientId);
            identity.SetScopes(request.GetScopes());
            identity.SetAudiences([.. client.Audiences]);

            // Every token this provider issues is a client-credentials access token, so all three
            // configured claim sets apply to it. Multi-valued claims become JSON arrays.
            AddConfiguredClaims(identity, client.TokenClaims.Common);
            AddConfiguredClaims(identity, client.TokenClaims.AccessToken);
            AddConfiguredClaims(identity, client.TokenClaims.ClientCredentialsToken);

            identity.SetDestinations(static _ => [Destinations.AccessToken]);

            return Results.SignIn(
                new ClaimsPrincipal(identity),
                properties: null,
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        /// <summary>
        /// Stamps one configured claim set onto the identity: single values as plain claims,
        /// multi-valued claims as JSON arrays.
        /// </summary>
        /// <param name="identity">The identity the token is generated from.</param>
        /// <param name="claims">Claim values by claim type.</param>
        private static void AddConfiguredClaims(ClaimsIdentity identity, IDictionary<string, IList<string>> claims)
        {
            foreach ((string type, IList<string> values) in claims)
            {
                if (values.Count == 1)
                {
                    identity.SetClaim(type, values[0]);
                }
                else
                {
                    identity.SetClaims(type, [.. values]);
                }
            }
        }
    }
}
