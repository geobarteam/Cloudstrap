namespace Cloudstrap.WasmTestProject.Host.Bff.Controllers
{
    using Cloudstrap.Core;
    using Cloudstrap.Observability.Correlation;
    using Cloudstrap.WasmTestProject.Contracts;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Exposes the server-bound Cloudstrap options (safe subset only) and the ambient
    /// correlation identifier for the diagnostics page and the E2E tests.
    /// </summary>
    [ApiController]
    [Route("api/diagnostics")]
    public sealed class DiagnosticsController : ControllerBase
    {
        private readonly ApplicationOptions _application;
        private readonly OpenTelemetryOptions _openTelemetry;
        private readonly CorrelationOptions _correlation;
        private readonly ICorrelationContextAccessor _correlationAccessor;

        public DiagnosticsController(
            IOptions<ApplicationOptions> application,
            IOptions<OpenTelemetryOptions> openTelemetry,
            IOptions<CorrelationOptions> correlation,
            ICorrelationContextAccessor correlationAccessor)
        {
            ArgumentNullException.ThrowIfNull(application);
            ArgumentNullException.ThrowIfNull(openTelemetry);
            ArgumentNullException.ThrowIfNull(correlation);
            ArgumentNullException.ThrowIfNull(correlationAccessor);

            _application = application.Value;
            _openTelemetry = openTelemetry.Value;
            _correlation = correlation.Value;
            _correlationAccessor = correlationAccessor;
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

        [HttpGet("correlation")]
        public ActionResult<CorrelationDto> GetCorrelation()
        {
            return new CorrelationDto(_correlationAccessor.CorrelationId ?? string.Empty);
        }
    }
}
