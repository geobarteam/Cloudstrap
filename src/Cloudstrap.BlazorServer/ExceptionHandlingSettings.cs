namespace Cloudstrap.BlazorServer
{
    /// <summary>
    /// Error-handling settings, bound from <c>Cloudstrap:BlazorServer:ExceptionHandling</c>.
    /// </summary>
    /// <remarks>
    /// Outside the developer page, unhandled exceptions re-execute the consumer's own error path at
    /// <c>Cloudstrap:Application:ExceptionHandlerPath</c> (default <c>/error</c>), with a fresh dependency
    /// injection scope for the re-execution — the razor-components idiom. This package ships no negotiated
    /// JSON error contract: a Blazor Server application serves people, not API clients, and the JSON half
    /// is <c>Cloudstrap.Mvc</c>/<c>Cloudstrap.WebApi</c> territory.
    /// </remarks>
    public sealed class ExceptionHandlingSettings
    {
        /// <summary>
        /// Gets or sets a value indicating whether the framework's developer exception page handles
        /// unhandled exceptions instead of the re-executed error path.
        /// </summary>
        /// <value>
        /// Unset (<see langword="null"/>) means <c>Development</c> only. Set it to
        /// <see langword="false"/> to keep the hardened error path even in <c>Development</c>.
        /// </value>
        public bool? UseDeveloperExceptionPage
        {
            get; set;
        }
    }
}
