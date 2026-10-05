namespace Cloudstrap.Hangfire
{
    using global::Hangfire;
    using global::Hangfire.Dashboard;

    /// <summary>
    /// Composes the Hangfire dashboard options: the read-only posture from configuration, then the consumer's
    /// delegate, then the authorization floor — <see cref="AuthenticatedDashboardAuthorizationFilter"/> first,
    /// the consumer's filters after it. Tighten, never loosen.
    /// </summary>
    internal static class DashboardOptionsComposer
    {
        /// <summary>Creates the dashboard options.</summary>
        /// <param name="readOnly">The <c>Cloudstrap:Hangfire:Dashboard:ReadOnly</c> value.</param>
        /// <param name="configure">The consumer's delegate, if any; it runs after the Cloudstrap defaults.</param>
        /// <returns>The composed options.</returns>
        public static DashboardOptions Create(bool readOnly, Action<DashboardOptions>? configure)
        {
            // The Hangfire default filter list (localhost only) is replaced, never extended: the floor below is the
            // Cloudstrap posture, and the endpoint's authorization policy does the rest.
            DashboardOptions options = new()
            {
                Authorization = [],
                IsReadOnlyFunc = _ => readOnly,
            };

            configure?.Invoke(options);

            IEnumerable<IDashboardAuthorizationFilter> consumerFilters = (options.Authorization ?? [])
                .Where(filter => filter is not AuthenticatedDashboardAuthorizationFilter);
            options.Authorization = [new AuthenticatedDashboardAuthorizationFilter(), .. consumerFilters];
            return options;
        }
    }
}
