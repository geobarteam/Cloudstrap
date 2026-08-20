namespace Cloudstrap.Demo.BlazorWasm.Bff.Controllers
{
    using Cloudstrap.Core;
    using Cloudstrap.Demo.BlazorWasm.Bff.Services;
    using Cloudstrap.Demo.Contracts;
    using Cloudstrap.Observability.Correlation;
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
        private readonly ISelfApiClient _selfApiClient;

        public DiagnosticsController(
            IOptions<ApplicationOptions> application,
            IOptions<OpenTelemetryOptions> openTelemetry,
            IOptions<CorrelationOptions> correlation,
            ICorrelationContextAccessor correlationAccessor,
            ISelfApiClient selfApiClient)
        {
            ArgumentNullException.ThrowIfNull(application);
            ArgumentNullException.ThrowIfNull(openTelemetry);
            ArgumentNullException.ThrowIfNull(correlation);
            ArgumentNullException.ThrowIfNull(correlationAccessor);
            ArgumentNullException.ThrowIfNull(selfApiClient);

            _application = application.Value;
            _openTelemetry = openTelemetry.Value;
            _correlation = correlation.Value;
            _correlationAccessor = correlationAccessor;
            _selfApiClient = selfApiClient;
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

        /// <summary>
        /// Makes one outbound hop through the Cloudstrap-registered typed client and reports the
        /// correlation identifier the peer saw — which is this request's, propagated by the handler
        /// Cloudstrap put in that client's pipeline.
        /// </summary>
        /// <param name="cancellationToken">Cancels the outbound request.</param>
        /// <returns>The peer-reported correlation identifier.</returns>
        [HttpGet("outbound")]
        public async Task<ActionResult<OutboundCallDto>> GetOutbound(CancellationToken cancellationToken)
        {
            string peerCorrelationId = await _selfApiClient.GetPeerCorrelationIdAsync(cancellationToken);

            return new OutboundCallDto(peerCorrelationId);
        }
    }
}
