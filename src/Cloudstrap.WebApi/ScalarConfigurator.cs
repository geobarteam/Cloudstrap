namespace Cloudstrap.WebApi
{
    using Scalar.AspNetCore;

    /// <summary>
    /// Carries the reference-UI hook from the service registration, where the consumer supplies it, to the
    /// pipeline, where the UI is mapped.
    /// </summary>
    /// <param name="configure">The consumer hook, or <see langword="null"/> when none was supplied.</param>
    internal sealed class ScalarConfigurator(Action<ScalarOptions>? configure)
    {
        /// <summary>
        /// Gets the hook applied to the reference UI after the Cloudstrap defaults.
        /// </summary>
        /// <value>The hook, or <see langword="null"/> when none was supplied.</value>
        public Action<ScalarOptions>? Configure { get; } = configure;
    }
}
