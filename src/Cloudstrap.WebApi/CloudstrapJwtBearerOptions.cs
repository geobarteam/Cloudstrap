namespace Cloudstrap.WebApi
{
    using System.ComponentModel.DataAnnotations;

    /// <summary>
    /// Inbound JWT bearer validation settings, bound from the <c>Cloudstrap:JwtBearer</c> configuration
    /// section.
    /// </summary>
    /// <remarks>
    /// These settings govern how tokens this API <em>receives</em> are validated. Acquiring tokens to call
    /// other services is a separate concern and a separate package.
    /// </remarks>
    public sealed class CloudstrapJwtBearerOptions
    {
        /// <summary>
        /// The configuration section these options are bound from.
        /// </summary>
        public const string SectionName = "Cloudstrap:JwtBearer";

        /// <summary>
        /// Gets or sets the identity provider that issued the tokens this API accepts.
        /// </summary>
        /// <value>The authority URL. Required.</value>
        [Required(
            AllowEmptyStrings = false,
            ErrorMessage = "'Cloudstrap:JwtBearer:Authority' is required when JWT bearer authentication is added.")]
        public string Authority { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the audience a token must name to be accepted — this API's own identifier at the
        /// identity provider.
        /// </summary>
        /// <value>The expected audience. Required.</value>
        /// <remarks>
        /// Audience validation is deliberately not switchable. A token minted for another service in the same
        /// realm must not open this one, and an API that accepts any audience is exactly that mistake.
        /// </remarks>
        [Required(
            AllowEmptyStrings = false,
            ErrorMessage = "'Cloudstrap:JwtBearer:Audience' is required when JWT bearer authentication is added.")]
        public string Audience { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether the identity provider's metadata must be fetched over
        /// HTTPS.
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
        /// Gets or sets the tolerance applied to a token's expiry and not-before times, in seconds.
        /// </summary>
        /// <value>
        /// The clock skew in seconds. Defaults to 60 — considerably tighter than the framework's 300, which
        /// dates from an era of poorly synchronised servers and today mostly widens the window in which a
        /// revoked or expired token still works.
        /// </value>
        public int ClockSkewSeconds { get; set; } = 60;

        /// <summary>
        /// Gets or sets a value indicating whether inbound claim names are translated to the framework's
        /// legacy URI-style names.
        /// </summary>
        /// <value>
        /// <see langword="true"/> to remap, <see langword="false"/> — the default — to keep the names the
        /// token actually used, so <c>sub</c> stays <c>sub</c>.
        /// </value>
        public bool MapInboundClaims
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether registering bearer authentication also requires every
        /// endpoint to be authenticated.
        /// </summary>
        /// <value>
        /// <see langword="true"/> — the default — to install a require-authenticated fallback authorization
        /// policy, so an endpoint is protected unless it opts out. <see langword="false"/> leaves
        /// authorization entirely to the application's own attributes and policies.
        /// </value>
        /// <remarks>
        /// Secure by default with two documented opt-outs: <c>[AllowAnonymous]</c> for one endpoint, this
        /// flag for the whole application. Health probes and the API documentation endpoints are mapped
        /// anonymously by Cloudstrap and stay reachable either way.
        /// </remarks>
        public bool RequireAuthenticatedEndpoints { get; set; } = true;
    }
}
