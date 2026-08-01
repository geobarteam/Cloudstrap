namespace Cloudstrap.WasmTestProject.Host.Bff.Controllers
{
    using Cloudstrap.Core;
    using Cloudstrap.WasmTestProject.Contracts;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Exposes the server-bound Cloudstrap options (safe subset only) for the diagnostics page.
    /// </summary>
    [ApiController]
    [Route("api/diagnostics")]
    public sealed class DiagnosticsController : ControllerBase
    {
        private readonly ApplicationOptions _application;
        private readonly OpenTelemetryOptions _openTelemetry;
        private readonly CorrelationOptions _correlation;

        public DiagnosticsController(
            IOptions<ApplicationOptions> application,
            IOptions<OpenTelemetryOptions> openTelemetry,
            IOptions<CorrelationOptions> correlation)
        {
            ArgumentNullException.ThrowIfNull(application);
            ArgumentNullException.ThrowIfNull(openTelemetry);
            ArgumentNullException.ThrowIfNull(correlation);

            _application = application.Value;
            _openTelemetry = openTelemetry.Value;
            _correlation = correlation.Value;
        }

        [HttpGet("options")]
        public ActionResult<DiagnosticsDto> GetOptions()
        {
            return new DiagnosticsDto(
                _application.SystemName,
                _application.SubsystemName,
                _application.SubsystemType,
                _application.WorkloadName,
                _application.EnvironmentTier,
                _openTelemetry.Mode.ToString(),
                _correlation.HeaderName);
        }
    }
}
