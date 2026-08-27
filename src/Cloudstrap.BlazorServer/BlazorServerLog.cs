namespace Cloudstrap.BlazorServer
{
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The log messages this package writes. Source-generated, so logging costs nothing when the level is
    /// disabled and the message template is checked at compile time.
    /// </summary>
    internal static partial class BlazorServerLog
    {
        /// <summary>
        /// Records the composition decisions the pipeline call made, once, when the pipeline is built —
        /// the one place an operator can read which interactivity the application runs with and whether
        /// authentication middleware was placed.
        /// </summary>
        /// <param name="logger">The logger to write to.</param>
        /// <param name="interactivity">The interactivity decided at registration time.</param>
        /// <param name="authenticationPlaced">
        /// Whether an authentication scheme was registered and the middleware pair was placed.
        /// </param>
        [LoggerMessage(
            EventId = 1000,
            Level = LogLevel.Debug,
            Message = "Cloudstrap Blazor Server pipeline built: interactivity {Interactivity}, "
                + "authentication middleware placed: {AuthenticationPlaced}.")]
        public static partial void PipelineBuilt(
            ILogger logger,
            BlazorInteractivity interactivity,
            bool authenticationPlaced);
    }
}
