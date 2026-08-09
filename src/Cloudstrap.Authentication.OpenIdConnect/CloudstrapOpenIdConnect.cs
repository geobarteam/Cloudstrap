namespace Cloudstrap.Authentication.OpenIdConnect
{
    using Microsoft.AspNetCore.Authentication.Cookies;
    using Microsoft.AspNetCore.Authentication.OpenIdConnect;

    /// <summary>
    /// Well-known names of the interactive login integration.
    /// </summary>
    /// <remarks>
    /// Both schemes deliberately keep their <em>stock</em> names, so unqualified
    /// <c>Challenge()</c>/<c>SignOut()</c> calls and third-party libraries that assume the framework
    /// defaults keep working.
    /// </remarks>
    public static class CloudstrapOpenIdConnect
    {
        /// <summary>
        /// The cookie scheme holding the interactive session — the application's default scheme.
        /// </summary>
        public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;

        /// <summary>
        /// The OpenID Connect scheme challenges go to — the application's default challenge scheme.
        /// </summary>
        public const string ChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    }
}
