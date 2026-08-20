namespace Cloudstrap.WasmTestProject.Contracts
{
    /// <summary>
    /// The versioned status payload, served side by side on two API versions so the per-version OpenAPI
    /// documents have something to differ about.
    /// </summary>
    /// <param name="ApiVersion">The API version that produced the payload.</param>
    /// <param name="WorkloadName">The workload name resolved from <c>Cloudstrap:Application</c>.</param>
    public sealed record StatusDto(string ApiVersion, string WorkloadName);
}
