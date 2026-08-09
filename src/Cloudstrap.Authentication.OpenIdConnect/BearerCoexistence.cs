namespace Cloudstrap.Authentication.OpenIdConnect
{
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// The AC-OIDC9 coexistence rule in one place: a request carrying an
    /// <c>Authorization: Bearer …</c> header is authenticated and challenged through the JWT bearer
    /// scheme — 401, never a login redirect — while every other request uses the cookie/OpenID Connect
    /// path.
    /// </summary>
    /// <remarks>
    /// The bearer scheme is looked up by its well-known name through
    /// <see cref="IAuthenticationSchemeProvider"/>, so this package takes no
    /// <c>Microsoft.AspNetCore.Authentication.JwtBearer</c> reference and the forwarding is inert when
    /// <c>Cloudstrap.WebApi</c>'s <c>AddCloudstrapJwtBearer</c> is not registered. The selector can
    /// only ever choose between the scheme the request's header names and the default path — it never
    /// authenticates a browser request as a bearer caller or vice versa.
    /// </remarks>
    internal static class BearerCoexistence
    {
        /// <summary>
        /// The well-known name of the JWT bearer scheme (<c>JwtBearerDefaults.AuthenticationScheme</c>),
        /// spelled here so no JwtBearer package reference is taken.
        /// </summary>
        internal const string BearerSchemeName = "Bearer";

        /// <summary>
        /// The forward selector installed on the cookie (default) and OpenID Connect (challenge)
        /// schemes.
        /// </summary>
        /// <param name="context">The current HTTP context.</param>
        /// <returns>
        /// The bearer scheme name when the request carries a bearer header <em>and</em> the scheme is
        /// registered; otherwise <see langword="null"/> — the scheme's own behavior.
        /// </returns>
        internal static string? SelectForwardScheme(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            string authorization = context.Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // The scheme provider's in-memory lookup completes synchronously; the selector contract
            // is synchronous.
            AuthenticationScheme? bearerScheme = context.RequestServices
                .GetRequiredService<IAuthenticationSchemeProvider>()
                .GetSchemeAsync(BearerSchemeName)
                .GetAwaiter()
                .GetResult();

            return bearerScheme is null ? null : BearerSchemeName;
        }
    }
}
