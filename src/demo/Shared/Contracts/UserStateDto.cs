namespace Cloudstrap.WasmTestProject.Contracts
{
    /// <summary>
    /// The caller's authentication state, reported by the anonymous <c>state</c> endpoint (SUT demo
    /// code — replaced by deliverable #13's BFF user-info contract).
    /// </summary>
    /// <param name="SignedIn">Whether the caller has a signed-in cookie session.</param>
    /// <param name="Name">The signed-in user's display name, or empty when signed out.</param>
    public sealed record UserStateDto(bool SignedIn, string Name);
}
