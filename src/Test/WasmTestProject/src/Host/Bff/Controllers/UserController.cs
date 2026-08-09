namespace Cloudstrap.WasmTestProject.Host.Bff.Controllers
{
    using System.Security.Claims;
    using Asp.Versioning;
    using Cloudstrap.WasmTestProject.Contracts;
    using Cloudstrap.WasmTestProject.Host.Bff.Services;
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
        /// Echoes the signed-in user's identity from the cookie principal. (SUT application code for
        /// the demo — the BFF user-info contract is deliverable #13.)
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
        /// Invokes the protected machine endpoint through the user-flagged typed client and relays
        /// what it validated — the in-app round trip proving the user's token reached the peer.
        /// </summary>
        /// <param name="cancellationToken">Cancels the outbound request.</param>
        /// <returns>The relayed caller identity.</returns>
        [HttpGet("call")]
        [Authorize]
        public async Task<ActionResult<MachineStatusDto>> GetCall(CancellationToken cancellationToken)
        {
            return Ok(await userApiClient.GetMachineStatusAsync(cancellationToken));
        }
    }
}
