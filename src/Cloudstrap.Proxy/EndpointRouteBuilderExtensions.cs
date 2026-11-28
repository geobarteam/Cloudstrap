namespace Cloudstrap.Proxy
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Routing;

    /// <summary>
    /// Maps the trusted-subsystem proxy's routes into the endpoint pipeline.
    /// </summary>
    public static class EndpointRouteBuilderExtensions
    {
        /// <summary>
        /// Maps the YARP routes registered by <see cref="HostApplicationBuilderExtensions.AddCloudstrapProxy"/>.
        /// </summary>
        /// <param name="endpoints">The endpoint route builder, typically the <c>WebApplication</c>.</param>
        /// <returns>
        /// YARP's own <see cref="ReverseProxyConventionBuilder"/>, so endpoint conventions chain on it.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// Call it after <c>UseAuthentication</c> and <c>UseAuthorization</c>, so route authorization policies apply
        /// before a request is forwarded.
        /// </remarks>
        public static ReverseProxyConventionBuilder MapCloudstrapProxy(this IEndpointRouteBuilder endpoints)
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            return endpoints.MapReverseProxy();
        }
    }
}
