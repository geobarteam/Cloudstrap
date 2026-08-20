namespace Cloudstrap.Demo.BlazorWasm.Bff.Controllers
{
    using Asp.Versioning;
    using Cloudstrap.Core;
    using Cloudstrap.Demo.Contracts;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Version 2.0 of the status API. Its existence is what makes the host publish a second OpenAPI document
    /// (deliverable #5 demo).
    /// </summary>
    /// <param name="application">The bound application identity.</param>
    [ApiController]
    [ApiVersion("2.0")]
    [Route("api/v{version:apiVersion}/status")]
    public sealed class StatusV2Controller(IOptions<ApplicationOptions> application) : ControllerBase
    {
        /// <summary>Serves the version 2.0 status.</summary>
        /// <returns>The status payload.</returns>
        [HttpGet]
        public ActionResult<StatusDto> Get()
        {
            return Ok(new StatusDto("2.0", application.Value.WorkloadName));
        }
    }
}
