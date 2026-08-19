namespace Cloudstrap.Mvc
{
    using Microsoft.AspNetCore.Http;

    /// <summary>
    /// Session-state settings, bound from <c>Cloudstrap:Mvc:Session</c>. The defaults are the hardened
    /// posture; every one of them is overridable here, and the <c>CloudstrapMvcConfigurator.Session</c>
    /// hook runs after all of them for anything this type does not model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hardening is applied entirely as startup-time options on stock
    /// <c>Microsoft.AspNetCore.Session</c> — this package ships no session middleware, store or
    /// cookie-protection code, and the cookie stays compatible with the stock one (same DataProtection
    /// purpose). <c>HttpOnly = true</c> and <c>SameSite = Lax</c> are the framework's own defaults,
    /// asserted by tests rather than modeled here.
    /// </para>
    /// <para>
    /// The cookie path follows <c>Cloudstrap:Application:PathBase</c> when one is configured, and is
    /// <c>/</c> otherwise.
    /// </para>
    /// <para>
    /// With <see cref="CookieSecurePolicy.Always"/>, a browser will not return the cookie over plain
    /// HTTP on a non-loopback origin: develop over HTTPS, or override to
    /// <see cref="Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest"/> explicitly for local
    /// HTTP — the default is never silently downgraded.
    /// </para>
    /// </remarks>
    public sealed class SessionSettings
    {
        /// <summary>
        /// Gets or sets a value indicating whether session state is registered and wired at all.
        /// </summary>
        /// <value>
        /// <see langword="true"/> by default. When <see langword="false"/>, no session services, cache
        /// fallback or middleware are added, and <c>HttpContext.Session</c> surfaces the framework's own
        /// <see cref="InvalidOperationException"/>.
        /// </value>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the session cookie name.
        /// </summary>
        /// <value>Defaults to <c>.Cloudstrap.Session</c>.</value>
        public string CookieName { get; set; } = ".Cloudstrap.Session";

        /// <summary>
        /// Gets or sets the cookie secure policy.
        /// </summary>
        /// <value>
        /// Defaults to <see cref="Microsoft.AspNetCore.Http.CookieSecurePolicy.Always"/>: the cookie is
        /// always marked <c>Secure</c>. See the type remarks for the plain-HTTP consequence.
        /// </value>
        public CookieSecurePolicy CookieSecurePolicy { get; set; } = CookieSecurePolicy.Always;

        /// <summary>
        /// Gets or sets the idle timeout, in minutes, after which an untouched session is abandoned.
        /// </summary>
        /// <value>Defaults to 20 — the stock value, made visible and configurable.</value>
        public int IdleTimeoutMinutes { get; set; } = 20;

        /// <summary>
        /// Gets or sets a value indicating whether the session cookie is essential and therefore exempt
        /// from cookie-consent gating.
        /// </summary>
        /// <value>
        /// Defaults to <see langword="false"/>: with a consent feature active (such as
        /// <c>Cloudstrap.CookieConsent</c>), the session cookie is withheld until the visitor consents.
        /// Set it to <see langword="true"/> only when session state is genuinely essential to the site's
        /// basic function.
        /// </value>
        public bool IsEssential
        {
            get; set;
        }
    }
}
