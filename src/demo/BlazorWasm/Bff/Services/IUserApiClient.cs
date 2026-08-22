namespace Cloudstrap.Demo.BlazorWasm.Bff.Services
{
    using Cloudstrap.Demo.Contracts;

    /// <summary>
    /// The deliverable #10 typed client: registered through <c>AddCloudstrapHttpServiceClient</c> and
    /// flagged <em>both</em> <c>AddUserAccessToken</c> and <c>AddClientAccessToken</c> in
    /// configuration — the signed-in user's token is the one that reaches the peer (AC-CC13 live).
    /// Since deliverable #27 the peer is the separate Api demo host on 5330, so the call is a real
    /// cross-process trusted-subsystem hop.
    /// </summary>
    public interface IUserApiClient
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
