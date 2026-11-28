namespace Cloudstrap.Proxy.Tests.Infrastructure
{
    using System.Security.Claims;
    using System.Text.Encodings.Web;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// The front door's test sign-in (mechanic (h)): a request carrying <c>Authorization: Test &lt;name&gt;</c> is a
    /// signed-in user called <c>name</c>. The caller's credential is therefore an <c>Authorization</c> header, so its
    /// removal before forwarding is observable on the same request. The base handler's challenge is 401 and its
    /// forbid is 403, like a bearer scheme.
    /// </summary>
    internal sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        /// <summary>The scheme name, also the <c>Authorization</c> header's scheme token.</summary>
        public const string SchemeName = "Test";

        /// <inheritdoc />
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string authorization = Request.Headers.Authorization.ToString();
            const string prefix = SchemeName + " ";
            if (!authorization.StartsWith(prefix, StringComparison.Ordinal) || authorization.Length == prefix.Length)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            string name = authorization[prefix.Length..].Trim();
            ClaimsPrincipal principal = new(new ClaimsIdentity([new Claim("name", name), new Claim("sub", name)], SchemeName, "name", "role"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
