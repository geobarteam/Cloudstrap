namespace Cloudstrap.Demo.Contracts
{
    /// <summary>
    /// The ambient correlation identifier of the current request, as established by the
    /// Cloudstrap correlation middleware.
    /// </summary>
    public sealed record CorrelationDto(string CorrelationId);
}
