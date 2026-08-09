namespace Cloudstrap.Authentication.OpenIdConnect
{
    using System.ComponentModel.DataAnnotations;

    /// <summary>
    /// Settings for interactive OpenID Connect login, bound from the flat
    /// <c>Cloudstrap:OpenIdConnect</c> configuration section.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This section owns its <em>own</em> client registration: <see cref="ClientId"/> is required here
    /// and is never read from <c>Cloudstrap:ClientCredentials</c> — an interactive web client and a
    /// machine client are different principals at the identity provider (decision D-3).
    /// </para>
    /// <para>
    /// The client secret is a configuration value like any other: supply it through Azure Key Vault, an
    /// environment variable or user-secrets — never <c>appsettings.json</c>. It may be omitted entirely
    /// for public clients or for secret-free client authentication configured through
    /// <see cref="CloudstrapOpenIdConnectConfigurator.OpenIdConnect"/>.
    /// </para>
    /// </remarks>
    public sealed class CloudstrapOpenIdConnectOptions
    {
        /// <summary>
        /// The configuration section the options bind from.
        /// </summary>
        public const string SectionName = "Cloudstrap:OpenIdConnect";

        /// <summary>
        /// Gets or sets the identity provider users sign in at. The stock handler discovers the
        /// authorization, token, userinfo and end-session endpoints from its
        /// <c>/.well-known/openid-configuration</c> document.
        /// </summary>
        /// <value>The absolute authority URL. Required.</value>
        [Required(
            AllowEmptyStrings = false,
            ErrorMessage = "'Cloudstrap:OpenIdConnect:Authority' is required — the absolute URL of the"
                + " identity provider users sign in at.")]
        public string Authority { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the client identifier of this application's <em>own</em> interactive client at
        /// the identity provider.
        /// </summary>
        /// <value>The client identifier. Required — never read from another section (D-3).</value>
        [Required(
            AllowEmptyStrings = false,
            ErrorMessage = "'Cloudstrap:OpenIdConnect:ClientId' is required — this package never reads"
                + " the client identifier from 'Cloudstrap:ClientCredentials'.")]
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the client secret presented during the code exchange.
        /// </summary>
        /// <value>
        /// The secret, or <see langword="null"/> — the default — for public clients or secret-free
        /// client authentication reached through
        /// <see cref="CloudstrapOpenIdConnectConfigurator.OpenIdConnect"/> (D-3).
        /// </value>
        public string? ClientSecret
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the space-delimited scopes requested at login.
        /// </summary>
        /// <value>
        /// The scope string. Defaults to <c>openid profile offline_access</c> — <c>offline_access</c>
        /// requests the refresh token transparent renewal needs (D-3).
        /// </value>
        /// <remarks>
        /// Deliberately one string rather than a bound collection: setting it <em>replaces</em> the
        /// default entirely, so a default scope can be removed and no stock default is silently
        /// appended.
        /// </remarks>
        public string Scope { get; set; } = "openid profile offline_access";

        /// <summary>
        /// Gets or sets a value indicating whether inbound claim names are translated to the
        /// framework's legacy URI-style names.
        /// </summary>
        /// <value>
        /// <see langword="true"/> to remap, <see langword="false"/> — the default — to keep the names
        /// the token actually used, so <c>sub</c> stays <c>sub</c>.
        /// </value>
        public bool MapInboundClaims
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the identity provider's metadata must be fetched
        /// over HTTPS.
        /// </summary>
        /// <value>
        /// <see langword="true"/> to require HTTPS, <see langword="false"/> to allow plain HTTP, or
        /// <see langword="null"/> — the default — to require it everywhere <em>except</em>
        /// <c>Development</c>, where a local identity provider on HTTP is normal.
        /// </value>
        public bool? RequireHttpsMetadata
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the local path the identity provider posts the authorization response back to.
        /// </summary>
        /// <value>The callback path. Defaults to the stock <c>/signin-oidc</c>.</value>
        public string CallbackPath { get; set; } = "/signin-oidc";

        /// <summary>
        /// Gets or sets the local path the identity provider returns to after RP-initiated sign-out.
        /// </summary>
        /// <value>The callback path. Defaults to the stock <c>/signout-callback-oidc</c>.</value>
        public string SignedOutCallbackPath { get; set; } = "/signout-callback-oidc";

        /// <summary>
        /// Gets or sets the path of the opt-in login endpoint mapped by
        /// <c>MapCloudstrapAuthenticationEndpoints</c>.
        /// </summary>
        /// <value>The login path. Defaults to <c>/account/login</c> (D-5).</value>
        public string LoginPath { get; set; } = "/account/login";

        /// <summary>
        /// Gets or sets the path of the opt-in logout endpoint mapped by
        /// <c>MapCloudstrapAuthenticationEndpoints</c>.
        /// </summary>
        /// <value>The logout path. Defaults to <c>/account/logout</c> (D-5).</value>
        public string LogoutPath { get; set; } = "/account/logout";

        /// <summary>
        /// Gets or sets a value indicating whether registering interactive login also requires every
        /// endpoint to be authenticated.
        /// </summary>
        /// <value>
        /// <see langword="true"/> — the default — to install a require-authenticated fallback
        /// authorization policy, so an endpoint is protected unless it opts out. <see langword="false"/>
        /// leaves authorization entirely to the application's own attributes and policies (D-6).
        /// </value>
        /// <remarks>
        /// Secure by default with two documented opt-outs: <c>[AllowAnonymous]</c> for one endpoint,
        /// this flag for the whole application — the same posture as
        /// <c>Cloudstrap:JwtBearer:RequireAuthenticatedEndpoints</c>.
        /// </remarks>
        public bool RequireAuthenticatedEndpoints { get; set; } = true;

        /// <summary>
        /// Gets the session cookie settings.
        /// </summary>
        /// <value>The cookie settings, bound from <c>Cloudstrap:OpenIdConnect:Cookie</c> (D-1).</value>
        public CloudstrapAuthenticationCookieOptions Cookie { get; } = new();
    }
}
