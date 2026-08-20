namespace Cloudstrap.WasmTestProject.Host.Bff.Services
{
    using Cloudstrap.WasmTestProject.Contracts;

    /// <summary>
    /// The deliverable #10 typed client: registered through <c>AddCloudstrapHttpServiceClient</c> and
    /// flagged <em>both</em> <c>AddUserAccessToken</c> and <c>AddClientAccessToken</c> in
    /// configuration — the signed-in user's token is the one that reaches the peer (AC-CC13 live).
    /// </summary>
    public interface IUserApiClient
    {
        /// <summary>
        /// Calls the JWT-protected machine endpoint as the signed-in user and returns the identity the
        /// peer validated.
        /// </summary>
        /// <param name="cancellationToken">Cancels the outbound request.</param>
        /// <returns>The validated caller identity the protected endpoint echoed.</returns>
        Task<MachineStatusDto> GetMachineStatusAsync(CancellationToken cancellationToken);
    }
}
