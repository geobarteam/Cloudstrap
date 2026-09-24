namespace Cloudstrap.Messaging.AzureBlob
{
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Writes the one startup log line stating the claim-check posture in force: the container, the offload
    /// threshold and where the client came from. Account URIs and connection strings never appear.
    /// </summary>
    internal static partial class ClaimCheckPostureLogger
    {
        /// <summary>The logger category the posture line is written under.</summary>
        public const string Category = "Cloudstrap.Messaging.AzureBlob";

        /// <summary>
        /// Logs the posture line.
        /// </summary>
        /// <param name="logger">The logger (category <see cref="Category"/>).</param>
        /// <param name="resolution">The client resolution.</param>
        /// <param name="thresholdBytes">The offload threshold in bytes.</param>
        public static void LogPosture(ILogger logger, ClaimCheckClientResolution resolution, long thresholdBytes)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(resolution);

            string note = resolution.Source == ClaimCheckClientSource.Code
                ? "; ContainerName setting ignored (the client names its container)"
                : string.Empty;
            LogPosture(logger, resolution.Container.Name, thresholdBytes, resolution.SourceLabel, note);
        }

        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "Cloudstrap claim check: container '{Container}', offload bodies larger than {ThresholdBytes} bytes, " +
                      "client from {ClientSource}{Note}")]
        private static partial void LogPosture(ILogger logger, string container, long thresholdBytes, string clientSource, string note);
    }
}
