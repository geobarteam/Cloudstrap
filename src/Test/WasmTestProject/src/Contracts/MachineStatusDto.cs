namespace Cloudstrap.WasmTestProject.Contracts
{
    /// <summary>
    /// The validated machine caller's identity, echoed from the claims of the bearer token the
    /// protected endpoint accepted (deliverable #9 demo).
    /// </summary>
    /// <param name="ClientId">The caller's <c>client_id</c> claim.</param>
    /// <param name="Issuer">The token's <c>iss</c> claim — the test identity provider's address.</param>
    /// <param name="Scope">The caller's <c>scope</c> claim.</param>
    public sealed record MachineStatusDto(string ClientId, string Issuer, string Scope);
}
