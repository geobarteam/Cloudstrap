namespace Cloudstrap.Observability.Correlation
{
    /// <summary>
    /// Generates a correlation identifier when an inbound request or message carries none. Register an
    /// implementation before calling <c>AddCloudstrapCorrelation</c> to override the default trace-id-based
    /// generation.
    /// </summary>
    public interface ICorrelationSource
    {
        /// <summary>
        /// Generates a new correlation identifier.
        /// </summary>
        /// <returns>The generated identifier.</returns>
        string GenerateCorrelation();
    }
}
