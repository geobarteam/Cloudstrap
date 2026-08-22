namespace Cloudstrap.Demo.Api.Controllers
{
    using Asp.Versioning;
    using Cloudstrap.Demo.Contracts;
    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// The downstream echo of the trusted-subsystem demo (deliverable #27): deliberately carries no
    /// <c>[Authorize]</c> attribute — the host-wide fallback policy
    /// (<c>RequireAuthenticatedEndpoints</c> at its hardened default) is what demands the token.
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/downstream")]
    public sealed class DownstreamController : ControllerBase
    {
        /// <summary>
        /// Echoes the validated caller identity plus this host's constant marker — the
        /// cross-process proof a same-shaped echo on the Bff cannot fake.
        /// </summary>
        /// <returns>The validated <c>sub</c>, <c>client_id</c> and <c>scope</c> claims and the host marker.</returns>
        [HttpGet("whoami")]
        public ActionResult<DownstreamWhoAmIDto> GetWhoAmI()
        {
            return Ok(new DownstreamWhoAmIDto(
                User.FindFirst("sub")?.Value ?? string.Empty,
                User.FindFirst("client_id")?.Value ?? string.Empty,
                User.FindFirst("scope")?.Value ?? string.Empty,
                "demo-api"));
        }
    }
}
