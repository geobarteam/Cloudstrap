namespace Cloudstrap.Demo.BlazorWasm.Presentation.Diagnostics
{
    using System.Net.Http.Json;
    using Cloudstrap.Demo.Contracts;
    using Microsoft.AspNetCore.Components;

    /// <summary>
    /// Shows the server-bound Cloudstrap options fetched from <c>api/diagnostics/options</c>
    /// (deliverable #1 demo).
    /// </summary>
    public partial class DiagnosticsPage
    {
        [Inject]
        public HttpClient Http { get; set; } = null!;

        protected DiagnosticsDto? Diagnostics
        {
            get; private set;
        }

        protected override async Task OnInitializedAsync()
        {
            Diagnostics = await Http.GetFromJsonAsync<DiagnosticsDto>("api/diagnostics/options");
        }
    }
}
