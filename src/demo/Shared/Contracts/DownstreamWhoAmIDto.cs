namespace Cloudstrap.Demo.Contracts
{
    /// <summary>
    /// The Api demo host's identity echo: the claims it validated plus its constant host marker —
    /// the proof that a call crossed the process boundary (deliverable #27).
    /// </summary>
    /// <param name="Subject">The validated <c>sub</c> claim.</param>
    /// <param name="ClientId">The validated <c>client_id</c> claim.</param>
    /// <param name="Scope">The validated <c>scope</c> claim.</param>
    /// <param name="Host">The constant <c>demo-api</c> marker stamped by the downstream host.</param>
    public sealed record DownstreamWhoAmIDto(string Subject, string ClientId, string Scope, string Host);
}
