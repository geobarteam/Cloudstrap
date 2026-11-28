namespace Cloudstrap.Demo.Contracts
{
    /// <summary>
    /// What the Api demo host received on an inbound request — the observation point of the
    /// trusted-subsystem proxy demo (deliverable #17): whether the caller's cookie crossed the hop and
    /// which credential scheme arrived instead.
    /// </summary>
    /// <param name="CookiePresent">Whether the request carried a <c>Cookie</c> header.</param>
    /// <param name="AuthorizationScheme">The scheme of the <c>Authorization</c> header, or <see langword="null"/> when absent.</param>
    /// <param name="Host">The constant <c>demo-api</c> marker stamped by the downstream host.</param>
    public sealed record DownstreamInboundDto(bool CookiePresent, string? AuthorizationScheme, string Host);
}
