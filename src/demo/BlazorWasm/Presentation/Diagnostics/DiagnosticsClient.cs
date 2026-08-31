namespace Cloudstrap.Demo.BlazorWasm.Presentation.Diagnostics
{
    using System.Net.Http.Json;
    using Cloudstrap.Demo.Contracts;

    /// <summary>
    /// The diagnostics API as a plain typed client (deliverable #13 demo): registered in one line by
    /// <c>AddCloudstrapWasmHttpClient&lt;DiagnosticsClient&gt;</c>, so it rides the same cookie+XSRF
    /// pipeline as the Refit client — the package's second registration flavor, demonstrated. The
    /// name ends in <c>Client</c> on purpose: it sits outside the #11 convention scan.
    /// </summary>
    public sealed class DiagnosticsClient
    {
        private readonly HttpClient _http;

        public DiagnosticsClient(HttpClient http)
        {
            ArgumentNullException.ThrowIfNull(http);
            _http = http;
        }

        public Task<DiagnosticsDto?> GetOptionsAsync(CancellationToken cancellationToken = default) =>
            _http.GetFromJsonAsync<DiagnosticsDto>("api/diagnostics/options", cancellationToken);
    }
}
