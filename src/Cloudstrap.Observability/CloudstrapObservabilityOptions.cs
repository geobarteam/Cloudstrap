namespace Cloudstrap.Observability
{
    using Serilog;

    /// <summary>
    /// Code-level options for <see cref="HostApplicationBuilderExtensions.UseCloudstrapObservability"/>.
    /// Everything driven by settings lives under the <c>Cloudstrap:</c> configuration section; these options
    /// carry only what cannot be expressed in configuration, such as callbacks.
    /// </summary>
    public sealed class CloudstrapObservabilityOptions
    {
        /// <summary>
        /// Gets or sets a callback that runs over the Serilog configuration after Cloudstrap has applied its
        /// own shape, giving the consumer the final say — add sinks, change levels, or override anything
        /// Cloudstrap configured.
        /// </summary>
        /// <value>The Serilog configuration callback, or <see langword="null"/> when not used.</value>
        public Action<LoggerConfiguration>? ConfigureSerilog
        {
            get; set;
        }
    }
}
