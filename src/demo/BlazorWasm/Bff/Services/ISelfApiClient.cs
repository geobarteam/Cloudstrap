namespace Cloudstrap.Demo.BlazorWasm.Bff.Services
{
    using Cloudstrap.Demo.Contracts;

    /// <summary>
    /// A typed HTTP client registered through <c>AddCloudstrapHttpServiceClient</c>. It calls back into this
    /// same application, which makes a genuine outbound hop observable without a second service.
    /// </summary>
    public interface ISelfApiClient
    {
        /// <summary>
        /// Calls the diagnostics correlation endpoint and returns the correlation identifier the peer saw.
        /// </summary>
        /// <param name="cancellationToken">Cancels the outbound request.</param>
        /// <returns>The peer-reported correlation identifier.</returns>
        Task<string> GetPeerCorrelationIdAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Calls the JWT-protected machine endpoint — this client is flagged
        /// <c>AddClientAccessToken</c>, so the request transparently carries a bearer token issued by
        /// the test identity provider (deliverable #9 demo).
        /// </summary>
        /// <param name="cancellationToken">Cancels the outbound request.</param>
        /// <returns>The validated caller identity the protected endpoint echoed.</returns>
        Task<MachineStatusDto> GetMachineStatusAsync(CancellationToken cancellationToken);
    }
}
