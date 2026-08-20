namespace Cloudstrap.Demo.Contracts
{
    /// <summary>
    /// The result of an outbound hop made through a Cloudstrap-registered typed HTTP client: the
    /// correlation identifier the peer saw on the incoming request.
    /// </summary>
    /// <param name="PeerCorrelationId">The correlation identifier reported by the peer.</param>
    public sealed record OutboundCallDto(string PeerCorrelationId);
}
