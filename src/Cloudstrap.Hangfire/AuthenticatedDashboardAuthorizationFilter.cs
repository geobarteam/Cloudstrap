namespace Cloudstrap.Hangfire
{
    using global::Hangfire.Dashboard;

    /// <summary>
    /// The defense-in-depth floor inside Hangfire's own filter list: a dashboard request is served only to an
    /// authenticated user, even when a consumer bypasses endpoint authorization (for example with
    /// <c>AllowAnonymous</c>). Always first; a consumer's dashboard delegate cannot remove it.
    /// </summary>
    internal sealed class AuthenticatedDashboardAuthorizationFilter : IDashboardAuthorizationFilter
    {
        /// <inheritdoc />
        public bool Authorize(DashboardContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            return context.GetHttpContext().User.Identity?.IsAuthenticated == true;
        }
    }
}
