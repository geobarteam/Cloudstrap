namespace Cloudstrap.Demo.BlazorWasm.Bff.Controllers
{
    using System.Security.Claims;
    using Asp.Versioning;
    using Cloudstrap.Demo.BlazorWasm.Bff.Services;
    using Cloudstrap.Demo.Contracts;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// The deliverable #10 demonstration pair: <c>whoami</c> echoes the cookie principal a real
    /// browser sign-in established, and <c>call</c> drives the user-flagged <see cref="IUserApiClient"/>
    /// into the JWT-protected machine endpoint — proving the <em>user's</em> token was the one on the
    /// wire. Both are <c>[Authorize]</c> on the default (cookie) scheme, so an anonymous browser is
    /// challenged to the identity provider.
    /// </summary>
    /// <param name="userApiClient">The both-flagged typed client that carries the user's token.</param>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/user")]
    public sealed class UserController(IUserApiClient userApiClient) : ControllerBase
    {
        /// <summary>
        /// Reports whether the caller has a signed-in cookie session — anonymous by design, always
        /// 200, so pages can probe auth state without any console-visible 401 noise. (SUT application
        /// code pinned by its own E2E test; the shipped BFF user-info contract is
        /// <c>MapCloudstrapBffUserEndpoint</c>'s <c>/bff/user</c>, which the WASM client consumes.)
        /// </summary>
        /// <returns>The caller's auth state and display name.</returns>
        [HttpGet("state")]
        public ActionResult<UserStateDto> GetState()
        {
            return Ok(new UserStateDto(
                User.Identity?.IsAuthenticated == true,
                User.Identity?.Name ?? string.Empty));
        }

        /// <summary>
        /// Echoes the signed-in user's identity from the cookie principal. (SUT application code for
        /// the demo; the shipped BFF user-info contract is <c>MapCloudstrapBffUserEndpoint</c>'s
        /// <c>/bff/user</c>.)
        /// </summary>
        /// <returns>The signed-in user's <c>sub</c> and <c>name</c>.</returns>
        [HttpGet("whoami")]
        [Authorize]
        public ActionResult<UserWhoAmIDto> GetWhoAmI()
        {
            return Ok(new UserWhoAmIDto(
                User.FindFirstValue("sub") ?? string.Empty,
                User.Identity?.Name ?? string.Empty));
        }

        /// <summary>
        /// Invokes the Api demo host's protected echo through the user-flagged typed client and
        /// relays what that host validated — since deliverable #27 a real cross-process round trip
        /// proving the user's token reached a separate peer (its <c>demo-api</c> marker cannot be
        /// faked by a same-shaped echo on this host).
        /// </summary>
        /// <param name="cancellationToken">Cancels the outbound request.</param>
        /// <returns>The relayed caller identity, including the downstream host marker.</returns>
        [HttpGet("call")]
        [Authorize]
        public async Task<ActionResult<DownstreamWhoAmIDto>> GetCall(CancellationToken cancellationToken)
        {
            return Ok(await userApiClient.GetWhoAmIAsync(cancellationToken));
        }
    }
}
