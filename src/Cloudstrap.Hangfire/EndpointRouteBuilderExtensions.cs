namespace Cloudstrap.Hangfire
{
    using global::Hangfire;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Maps the Hangfire dashboard behind authentication.
    /// </summary>
    public static class EndpointRouteBuilderExtensions
    {
        /// <summary>
        /// Maps the Hangfire dashboard at <c>Cloudstrap:Hangfire:Dashboard:Path</c> (default <c>/hangfire</c>),
        /// served only to authenticated users: the endpoint requires <c>Dashboard:AuthorizationPolicy</c> when set,
        /// otherwise an authenticated user in <c>Dashboard:RequiredRole</c> when set, otherwise any authenticated
        /// user. A Cloudstrap filter inside the dashboard enforces "authenticated" as well, so bypassing endpoint
        /// authorization never exposes it.
        /// </summary>
        /// <param name="endpoints">The endpoint route builder of a host that called <c>AddCloudstrapHangfire</c>.</param>
        /// <param name="configure">
        /// Optional delegate over the Hangfire <see cref="DashboardOptions"/> (title, extra authorization filters,
        /// app path…). It runs after the Cloudstrap defaults; filters it adds are additive and cannot remove the
        /// Cloudstrap floor.
        /// </param>
        /// <returns>The endpoint convention builder, to add further conventions (tighten, never loosen).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// No authentication scheme is registered, or <c>AddCloudstrapHangfire</c> was not called.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Call it from a Cloudstrap composite's <c>ConfigureEndpoints</c> hook, or on a <c>WebApplication</c> after
        /// <c>UseAuthentication</c> and <c>UseAuthorization</c>. A dashboard below "authenticated" is deliberately
        /// not offered: use the Hangfire <c>MapHangfireDashboard</c> directly for that.
        /// </para>
        /// <para>
        /// Dashboard actions (trigger, delete, requeue) are protected with the host's antiforgery tokens; on a host
        /// whose antiforgery cookie is secure-only, serve the dashboard over HTTPS. Proxy path prefixes
        /// (<c>X-Forwarded-Prefix</c>) are not handled here.
        /// </para>
        /// </remarks>
        public static IEndpointConventionBuilder MapCloudstrapHangfireDashboard(
            this IEndpointRouteBuilder endpoints,
            Action<DashboardOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            IServiceProvider services = endpoints.ServiceProvider;
            HangfireRegistrationState state = services.GetService<HangfireRegistrationState>()
                ?? throw new InvalidOperationException(
                    $"Call AddCloudstrapHangfire on the host builder before {nameof(MapCloudstrapHangfireDashboard)}.");

            bool hasScheme = services.GetService<IOptions<AuthenticationOptions>>()?.Value.SchemeMap.Count > 0;
            if (!hasScheme)
            {
                throw new InvalidOperationException(
                    "The Hangfire dashboard is only served to authenticated users: register an authentication scheme " +
                    "(for example AddCloudstrapOpenIdConnect, or the JWT bearer of AddCloudstrapWebApi) before " +
                    $"calling {nameof(MapCloudstrapHangfireDashboard)}.");
            }

            HangfireDashboardOptions dashboard = services.GetRequiredService<IOptions<HangfireOptions>>().Value.Dashboard;
            DashboardOptions options = DashboardOptionsComposer.Create(dashboard.ReadOnly, configure);

            IEndpointConventionBuilder mapped = endpoints.MapHangfireDashboard(dashboard.Path, options);
            if (!string.IsNullOrWhiteSpace(dashboard.AuthorizationPolicy))
            {
                mapped.RequireAuthorization(dashboard.AuthorizationPolicy);
            }
            else
            {
                AuthorizationPolicyBuilder policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser();
                if (!string.IsNullOrWhiteSpace(dashboard.RequiredRole))
                {
                    policy.RequireRole(dashboard.RequiredRole);
                }

                mapped.RequireAuthorization(policy.Build());
            }

            state.DashboardPath = dashboard.Path;

            // A processing host states the dashboard path in its scheduling summary; a storage-only host has no
            // scheduling service, so its one summary line is written here.
            if (!state.RunServer)
            {
                ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(HangfireStartupSummaryLogger.Category);
                HangfireStartupSummaryLogger.LogPosture(logger, state, state.RegisteredTaskCount);
            }

            return mapped;
        }
    }
}
