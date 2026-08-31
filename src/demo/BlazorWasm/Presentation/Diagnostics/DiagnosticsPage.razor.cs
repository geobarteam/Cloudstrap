namespace Cloudstrap.Demo.BlazorWasm.Presentation.Diagnostics
{
    using Cloudstrap.Demo.Contracts;
    using Microsoft.AspNetCore.Components;

    /// <summary>
    /// Shows the server-bound Cloudstrap options fetched from <c>api/diagnostics/options</c>
    /// (deliverable #1 demo) — through the #13 typed client, so the fetch rides the package pipeline.
    /// </summary>
    public partial class DiagnosticsPage
    {
        [Inject]
        public DiagnosticsClient Client { get; set; } = null!;

        protected DiagnosticsDto? Diagnostics
        {
            get; private set;
        }

        protected override async Task OnInitializedAsync()
        {
            Diagnostics = await Client.GetOptionsAsync();
        }
    }
}
