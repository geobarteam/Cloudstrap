namespace Cloudstrap.Demo.BlazorWasm.Bff.Controllers
{
    using Asp.Versioning;
    using Cloudstrap.Core;
    using Cloudstrap.Demo.Contracts;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Version 1.0 of the status API, plus the failing action the hardened error-response E2E test drives
    /// (deliverable #5 demo).
    /// </summary>
    /// <param name="application">The bound application identity.</param>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/status")]
    public sealed class StatusController(IOptions<ApplicationOptions> application) : ControllerBase
    {
        /// <summary>Serves the version 1.0 status.</summary>
        /// <returns>The status payload.</returns>
        [HttpGet]
        public ActionResult<StatusDto> Get()
        {
            return Ok(new StatusDto("1.0", application.Value.WorkloadName));
        }

        /// <summary>Fails with a nested exception chain, so the error contract is observable end to end.</summary>
        /// <returns>Never returns.</returns>
        [HttpGet("boom")]
        public ActionResult<StatusDto> Boom()
        {
            throw new InvalidOperationException(
                $"status projection failed for {application.Value.WorkloadName}",
                new InvalidDataException("the status store returned an unusable row"));
        }
    }
}
