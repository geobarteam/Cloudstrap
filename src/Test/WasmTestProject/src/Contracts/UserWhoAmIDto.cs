namespace Cloudstrap.WasmTestProject.Contracts
{
    /// <summary>
    /// The signed-in user's identity, echoed from the cookie principal (deliverable #10 demo — SUT
    /// application code; the BFF user-info <em>contract</em> arrives with deliverable #13).
    /// </summary>
    /// <param name="Sub">The user's <c>sub</c> claim.</param>
    /// <param name="Name">The user's <c>name</c> claim.</param>
    public sealed record UserWhoAmIDto(string Sub, string Name);
}
