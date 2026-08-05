namespace Cloudstrap.Extensions
{
    using Cloudstrap.Core;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates the per-client settings bound from <c>Cloudstrap:HttpClients:{name}</c>. Rules are
    /// conditional on the client having been registered through Cloudstrap, so a hand-written validator is
    /// used rather than the attribute-driven source generator.
    /// </summary>
    internal sealed class HttpClientServiceOptionsValidator : IValidateOptions<HttpClientServiceOptions>
    {
        private readonly IConfiguration _configuration;
        private readonly HttpServiceClientRegistry _registry;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpClientServiceOptionsValidator"/> class.
        /// </summary>
        /// <param name="configuration">The configuration the section's existence is checked against.</param>
        /// <param name="registry">The names registered through the Cloudstrap entry point.</param>
        /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
        public HttpClientServiceOptionsValidator(IConfiguration configuration, HttpServiceClientRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(registry);

            _configuration = configuration;
            _registry = registry;
        }

        /// <summary>
        /// Validates one named client's settings, naming the offending configuration key on failure.
        /// </summary>
        /// <param name="name">The client name the options were bound for.</param>
        /// <param name="options">The bound settings.</param>
        /// <returns>
        /// The validation result — skipped for names this package did not register.
        /// </returns>
        public ValidateOptionsResult Validate(string? name, HttpClientServiceOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (string.IsNullOrEmpty(name) || !_registry.Contains(name))
            {
                return ValidateOptionsResult.Skip;
            }

            string sectionPath = $"{ServiceCollectionExtensions.HttpClientsSectionPath}:{name}";

            if (!_configuration.GetSection(sectionPath).Exists())
            {
                return ValidateOptionsResult.Fail(
                    $"Configuration section '{sectionPath}' is missing. Add it, or drop the "
                    + $"AddCloudstrapHttpServiceClient registration for client '{name}'.");
            }

            if (options.BaseAddress is null || !options.BaseAddress.IsAbsoluteUri)
            {
                return ValidateOptionsResult.Fail($"'{sectionPath}:BaseAddress' must be an absolute URI.");
            }

            return ValidateOptionsResult.Success;
        }
    }
}
