namespace Cloudstrap.Extensions
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates <see cref="StorageOptions"/>. The rule is an either/or across two settings — one of which
    /// may come from the standard <c>ConnectionStrings:</c> section — so validation is written out rather
    /// than generated from attributes.
    /// </summary>
    internal sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
    {
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageOptionsValidator"/> class.
        /// </summary>
        /// <param name="configuration">
        /// The configuration consulted for the standard <c>ConnectionStrings:CloudstrapStorage</c> entry.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        public StorageOptionsValidator(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            _configuration = configuration;
        }

        /// <summary>
        /// Validates the supplied settings, naming the offending configuration key on failure.
        /// </summary>
        /// <param name="name">The options instance name, unused: these settings are never named.</param>
        /// <param name="options">The settings to validate.</param>
        /// <returns>The validation result.</returns>
        public ValidateOptionsResult Validate(string? name, StorageOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (BlobStorageRegistration.ResolveConnectionString(options, _configuration) is not null)
            {
                return ValidateOptionsResult.Success;
            }

            if (options.BlobServiceUri is null || !options.BlobServiceUri.IsAbsoluteUri)
            {
                return ValidateOptionsResult.Fail(
                    $"'{StorageOptions.SectionName}:BlobServiceUri' must be an absolute URI, unless "
                    + $"'{StorageOptions.SectionName}:ConnectionString' or "
                    + $"'ConnectionStrings:{StorageOptions.ConnectionStringName}' supplies a connection string.");
            }

            return ValidateOptionsResult.Success;
        }
    }
}
