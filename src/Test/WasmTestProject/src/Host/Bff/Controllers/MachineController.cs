namespace Cloudstrap.WasmTestProject.Host.Bff.Controllers
{
    using System.Security.Claims;
    using Asp.Versioning;
    using Cloudstrap.WasmTestProject.Contracts;
    using Cloudstrap.WasmTestProject.Host.Bff.Services;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// The deliverable #9 demonstration pair: <c>status</c> is the one endpoint of this otherwise
    /// anonymous SUT that requires a validated bearer token (#5's <c>AddCloudstrapJwtBearer</c>
    /// validating against the test identity provider), and <c>call</c> is the anonymous relay that
    /// drives the flagged <c>SelfApi</c> client into it — the in-app machine-to-machine round trip
    /// the E2E tests assert.
    /// </summary>
    /// <param name="selfApiClient">The flagged typed client that carries the machine token.</param>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/machine")]
    public sealed class MachineController(ISelfApiClient selfApiClient) : ControllerBase
    {
        /// <summary>
        /// Echoes the validated machine caller's claims. Reachable only with a bearer token the test
        /// identity provider issued — the <c>[Authorize]</c> here is the single opt-in next to
        /// <c>RequireAuthenticatedEndpoints=false</c>.
        /// </summary>
        /// <returns>The caller's identity claims.</returns>
        [HttpGet("status")]
        [Authorize]
        public ActionResult<MachineStatusDto> GetStatus()
        {
            return Ok(new MachineStatusDto(
                User.FindFirstValue("client_id") ?? string.Empty,
                User.FindFirstValue("iss") ?? string.Empty,
                User.FindFirstValue("scope") ?? string.Empty));
        }

        /// <summary>
        /// Invokes the protected endpoint through the flagged typed client and relays what it said —
        /// one anonymous GET that proves acquisition (#9) and validation (#5) live in the running app.
        /// </summary>
        /// <param name="cancellationToken">Cancels the outbound request.</param>
        /// <returns>The relayed machine status.</returns>
        [HttpGet("call")]
        public async Task<ActionResult<MachineStatusDto>> GetCall(CancellationToken cancellationToken)
        {
            return Ok(await selfApiClient.GetMachineStatusAsync(cancellationToken));
        }
    }
}
