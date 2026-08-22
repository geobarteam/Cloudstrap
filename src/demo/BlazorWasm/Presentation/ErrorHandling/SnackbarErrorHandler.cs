namespace Cloudstrap.Demo.BlazorWasm.Presentation.ErrorHandling
{
    using Cloudstrap.BlazorCommon;
    using MudBlazor;

    /// <summary>
    /// The consumer-owned <see cref="IErrorHandler"/> implementation over MudBlazor's snackbar —
    /// Cloudstrap ships no implementation and no registration (AC-BC5), so the demo registers this
    /// explicitly in the Client's <c>Program.cs</c>. The name deliberately ends in <c>Handler</c>:
    /// it sits outside the convention scan.
    /// </summary>
    public sealed class SnackbarErrorHandler : IErrorHandler
    {
        private readonly ISnackbar _snackbar;

        public SnackbarErrorHandler(ISnackbar snackbar)
        {
            ArgumentNullException.ThrowIfNull(snackbar);

            _snackbar = snackbar;
        }

        public void HandleError(Exception exception) =>
            _snackbar.Add("Something went wrong. Please try again.", Severity.Error);

        public void ShowError(string message) => _snackbar.Add(message, Severity.Error);
    }
}
