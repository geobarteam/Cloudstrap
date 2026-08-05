namespace Cloudstrap.WasmTestProject.Host.Bff.Services
{
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
    }
}
