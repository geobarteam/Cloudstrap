namespace Cloudstrap.Hangfire
{
    /// <summary>
    /// Dashboard settings, bound from <c>Cloudstrap:Hangfire:Dashboard</c> and applied where the dashboard is
    /// mapped. The dashboard always requires an authenticated user.
    /// </summary>
    public sealed class HangfireDashboardOptions
    {
        /// <summary>
        /// Gets or sets the path the dashboard is served at, relative to the application path base.
        /// </summary>
        /// <value>A rooted path with no trailing slash. Defaults to <c>/hangfire</c>.</value>
        public string Path { get; set; } = "/hangfire";

        /// <summary>
        /// Gets or sets the role an authenticated user must be in to open the dashboard.
        /// </summary>
        /// <value>The role name, or <see langword="null"/> to admit any authenticated user.</value>
        public string? RequiredRole
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the name of an authorization policy that replaces the built-in one.
        /// </summary>
        /// <value>The policy name, or <see langword="null"/> to use the built-in authenticated (plus role) policy.</value>
        public string? AuthorizationPolicy
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the dashboard hides every action (trigger, delete, requeue).
        /// </summary>
        /// <value><see langword="true"/> for a read-only dashboard. Defaults to <see langword="false"/>.</value>
        public bool ReadOnly
        {
            get; set;
        }
    }
}
