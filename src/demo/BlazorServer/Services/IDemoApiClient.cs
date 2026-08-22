namespace Cloudstrap.Demo.BlazorServer.Services
{
    using Cloudstrap.Demo.Contracts;

    /// <summary>
    /// The typed client into the Api demo host: registered through
    /// <c>AddCloudstrapHttpServiceClient</c> and flagged <c>AddUserAccessToken</c> in configuration
    /// — the signed-in user's token is what reaches the peer, no token code in this app.
    /// </summary>
    public interface IDemoApiClient
    {
        /// <summary>
        /// Calls the Api demo host's JWT-protected echo as the signed-in user and returns the
        /// identity that host validated, including its constant <c>demo-api</c> marker.
        /// </summary>
        /// <param name="cancellationToken">Cancels the outbound request.</param>
        /// <returns>The validated caller identity the downstream host echoed.</returns>
        Task<DownstreamWhoAmIDto> GetWhoAmIAsync(CancellationToken cancellationToken);
    }
}
