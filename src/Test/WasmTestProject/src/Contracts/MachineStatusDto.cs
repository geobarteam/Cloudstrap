namespace Cloudstrap.WasmTestProject.Contracts
{
    /// <summary>
    /// The validated caller's identity, echoed from the claims of the bearer token the protected
    /// endpoint accepted (deliverable #9 demo; <paramref name="Subject"/> added by #10 so a user token
    /// is distinguishable from a machine token).
    /// </summary>
    /// <param name="ClientId">The caller's <c>client_id</c> claim.</param>
    /// <param name="Issuer">The token's <c>iss</c> claim — the test identity provider's address.</param>
    /// <param name="Scope">The caller's <c>scope</c> claim.</param>
    /// <param name="Subject">The caller's <c>sub</c> claim — who the caller is.</param>
    public sealed record MachineStatusDto(string ClientId, string Issuer, string Scope, string Subject = "");
}
