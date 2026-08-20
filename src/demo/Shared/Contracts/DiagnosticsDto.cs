namespace Cloudstrap.WasmTestProject.Contracts
{
    /// <summary>
    /// Safe subset of the server-bound Cloudstrap options shown on the diagnostics page —
    /// never expose secrets or export headers here.
    /// </summary>
    public sealed record DiagnosticsDto(
        string SystemName,
        string SubsystemName,
        string SubsystemType,
        string WorkloadName,
        string? EnvironmentTier,
        string OpenTelemetryMode,
        string CorrelationHeaderName);
}
