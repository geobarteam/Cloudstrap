namespace Cloudstrap.Authentication.OpenIdConnect
{
    using Duende.AccessTokenManagement.OpenIdConnect;
    using Microsoft.AspNetCore.Authentication.Cookies;
    using Microsoft.AspNetCore.Authentication.OpenIdConnect;

    /// <summary>
    /// Code-level hooks for the parts of the integration that configuration values cannot express.
    /// Every convention has an override: these delegates run after Cloudstrap's own wiring, so they
    /// always have the final say over it.
    /// </summary>
    public sealed class CloudstrapOpenIdConnectConfigurator
    {
        /// <summary>
        /// Gets or sets the hook shaping the stock <see cref="OpenIdConnectOptions"/> this package
        /// configures — for advanced protocol settings, events, claim actions, a custom backchannel, or
        /// secret-free client authentication.
        /// </summary>
        /// <value>The hook, or <see langword="null"/> for Cloudstrap's configuration alone.</value>
        public Action<OpenIdConnectOptions>? OpenIdConnect
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the hook shaping the stock <see cref="CookieAuthenticationOptions"/> this
        /// package configures. This is the <em>only</em> way to reach the hardened cookie constants —
        /// <c>HttpOnly</c>, <c>SecurePolicy</c> and <c>SameSite</c> — deliberately code, not
        /// configuration (D-1).
        /// </summary>
        /// <value>The hook, or <see langword="null"/> for the hardened defaults.</value>
        public Action<CookieAuthenticationOptions>? Cookie
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the hook shaping Duende's <see cref="UserTokenManagementOptions"/> — the
        /// refresh-before-expiry buffer and related knobs.
        /// </summary>
        /// <value>The hook, or <see langword="null"/> for Duende's defaults.</value>
        public Action<UserTokenManagementOptions>? TokenManagement
        {
            get; set;
        }
    }
}
