namespace Cloudstrap.WebApi.Tests.Infrastructure
{
    using System.Security.Claims;
    using System.Text;
    using Microsoft.AspNetCore.Authentication.JwtBearer;
    using Microsoft.IdentityModel.JsonWebTokens;
    using Microsoft.IdentityModel.Protocols.OpenIdConnect;
    using Microsoft.IdentityModel.Tokens;

    /// <summary>
    /// Issues locally signed test tokens and configures the bearer handler to validate them without ever
    /// contacting the configured authority. No live identity provider takes part in this suite.
    /// </summary>
    internal static class TestTokens
    {
        /// <summary>The issuer the fixture tokens claim, and the one the handler is told to trust.</summary>
        public const string Issuer = "https://idp.contoso.example/";

        /// <summary>The audience the fixture tokens carry, matching <c>Cloudstrap:JwtBearer:Audience</c>.</summary>
        public const string Audience = "contoso-catalog-api";

        /// <summary>A second issuer, used to prove the configure hook can widen issuer validation.</summary>
        public const string LegacyIssuer = "https://legacy-idp.contoso.example/";

        private static readonly SymmetricSecurityKey _signingKey = new(
            Encoding.UTF8.GetBytes("contoso-catalog-test-signing-key-of-sufficient-length"));

        /// <summary>
        /// Issues a signed token.
        /// </summary>
        /// <param name="audience">The audience claim. Defaults to <see cref="Audience"/>.</param>
        /// <param name="issuer">The issuer claim. Defaults to <see cref="Issuer"/>.</param>
        /// <param name="expiresIn">
        /// How long from now the token expires. A negative value issues an already-expired token.
        /// </param>
        /// <param name="subject">The subject claim.</param>
        /// <returns>The encoded token.</returns>
        public static string Issue(
            string? audience = null,
            string? issuer = null,
            TimeSpan? expiresIn = null,
            string subject = "contoso-user")
        {
            TimeSpan lifetime = expiresIn ?? TimeSpan.FromMinutes(10);
            DateTime now = DateTime.UtcNow;
            DateTime expires = now.Add(lifetime);

            SecurityTokenDescriptor descriptor = new()
            {
                Issuer = issuer ?? Issuer,
                Audience = audience ?? Audience,
                IssuedAt = expires < now ? expires.AddMinutes(-10) : now,
                NotBefore = expires < now ? expires.AddMinutes(-10) : now,
                Expires = expires,
                SigningCredentials = new SigningCredentials(
                    _signingKey,
                    SecurityAlgorithms.HmacSha256),
                Claims = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["sub"] = subject,
                },
            };

            return new JsonWebTokenHandler().CreateToken(descriptor);
        }

        /// <summary>
        /// Builds the bearer authorization header value for a token.
        /// </summary>
        /// <param name="token">The encoded token.</param>
        /// <returns>The header value.</returns>
        public static string BearerHeader(string token)
        {
            return $"Bearer {token}";
        }

        /// <summary>
        /// Configures the handler to validate the fixture's signing key against a pre-seeded discovery
        /// document, so the configured authority is never contacted over the network.
        /// </summary>
        /// <param name="extraIssuers">Additional issuers to accept, if any.</param>
        /// <returns>The hook to pass to <c>AddCloudstrapJwtBearer</c>.</returns>
        public static Action<JwtBearerOptions> Validation(params string[] extraIssuers)
        {
            return options =>
            {
                OpenIdConnectConfiguration configuration = new() { Issuer = Issuer };
                configuration.SigningKeys.Add(_signingKey);

                options.Configuration = configuration;
                options.TokenValidationParameters.IssuerSigningKey = _signingKey;
                options.TokenValidationParameters.ValidIssuers = [Issuer, .. extraIssuers];
            };
        }
    }
}
