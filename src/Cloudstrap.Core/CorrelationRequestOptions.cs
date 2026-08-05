namespace Cloudstrap.Core
{
    /// <summary>
    /// Correlation settings for inbound HTTP requests, bound from the
    /// <c>Cloudstrap:Correlation:Request</c> configuration section.
    /// </summary>
    public sealed class CorrelationRequestOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether every endpoint must carry a correlation identifier.
        /// The endpoints listed in <see cref="HealthEndpoints"/> and <see cref="ExcludeEndpoints"/> are exempt.
        /// </summary>
        /// <value><see langword="true"/> when correlation is mandatory. Defaults to <see langword="false"/>.</value>
        public bool RequireForAllEndpoints
        {
            get; set;
        }

        /// <summary>
        /// Gets the probe endpoints that are always exempt from the correlation requirement. Values configured
        /// under <c>Cloudstrap:Correlation:Request:HealthEndpoints</c> are appended to these defaults rather
        /// than replacing them, which is how the configuration binder populates pre-initialized collections.
        /// </summary>
        /// <value>The exempt probe paths. Defaults to <c>/healthz</c> and <c>/ready</c>.</value>
        public List<string> HealthEndpoints { get; } = ["/healthz", "/ready"];

        /// <summary>
        /// Gets the additional endpoints exempt from the correlation requirement.
        /// </summary>
        /// <value>The exempt paths. Empty by default.</value>
        public List<string> ExcludeEndpoints { get; } = [];

        /// <summary>
        /// Gets or sets a value indicating whether the correlation identifier is echoed back to the caller in
        /// a response header of the same name.
        /// </summary>
        /// <value><see langword="true"/> to echo it. Defaults to <see langword="true"/>.</value>
        /// <remarks>
        /// This is how a caller who sent no identifier learns the generated one, which is what makes it
        /// quotable in a support request. A value the application set itself is never overwritten.
        /// </remarks>
        public bool EchoInResponse { get; set; } = true;
    }
}
