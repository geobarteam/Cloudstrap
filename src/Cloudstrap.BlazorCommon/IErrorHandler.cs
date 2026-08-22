namespace Cloudstrap.BlazorCommon
{
    /// <summary>
    /// Consumer-implemented feedback contract: ViewModels route failures here instead of throwing
    /// into the render loop, and the application decides how errors surface (snackbar, dialog,
    /// logger, …).
    /// </summary>
    /// <remarks>
    /// Cloudstrap ships no implementation and registers nothing for this interface — implement it
    /// in the application and register it at any lifetime, for example
    /// <c>services.AddScoped&lt;IErrorHandler, SnackbarErrorHandler&gt;()</c>.
    /// </remarks>
    public interface IErrorHandler
    {
        /// <summary>
        /// Handles an exception a ViewModel or service caught on behalf of the user.
        /// </summary>
        /// <param name="exception">The caught exception.</param>
        void HandleError(Exception exception);

        /// <summary>
        /// Shows an error message to the user.
        /// </summary>
        /// <param name="message">The message to display.</param>
        void ShowError(string message);
    }
}
