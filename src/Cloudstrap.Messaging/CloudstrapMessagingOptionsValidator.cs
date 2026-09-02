namespace Cloudstrap.Messaging
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates <see cref="CloudstrapMessagingOptions"/>: the data-annotation rules first, then the
    /// transport-conditional rules that need the host's <see cref="IConfiguration"/> (connection strings that
    /// must resolve by name). Every failure names the offending configuration key and never echoes a value.
    /// </summary>
    internal sealed class CloudstrapMessagingOptionsValidator : IValidateOptions<CloudstrapMessagingOptions>
    {
        private readonly CloudstrapMessagingOptionsAnnotationsValidator _annotations = new();
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="CloudstrapMessagingOptionsValidator"/> class.
        /// </summary>
        /// <param name="configuration">The host configuration, consulted for the <c>ConnectionStrings:</c> section.</param>
        public CloudstrapMessagingOptionsValidator(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            _configuration = configuration;
        }

        /// <summary>
        /// Validates the supplied messaging options, reporting every failure rather than stopping at the first.
        /// </summary>
        /// <param name="name">The options instance name, unused: the rules do not vary per name.</param>
        /// <param name="options">The options to validate.</param>
        /// <returns>The validation result, carrying one failure per broken rule.</returns>
        public ValidateOptionsResult Validate(string? name, CloudstrapMessagingOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            List<string> failures = [];

            ValidateOptionsResult annotations = _annotations.Validate(name, options);
            if (annotations.Failed && annotations.Failures is not null)
            {
                foreach (string failure in annotations.Failures)
                {
                    failures.Add($"{CloudstrapMessagingOptions.SectionName}: {failure}");
                }
            }

            if (options.Transport == MessagingTransport.AzureServiceBus)
            {
                ValidateAzureServiceBus(options.AzureServiceBus, failures);
            }

            if (options.Transport == MessagingTransport.SqlServer)
            {
                ValidateSqlServer(options.SqlTransport, failures);
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }

        private void ValidateSqlServer(SqlTransportOptions sqlTransport, List<string> failures)
        {
            const string key = $"{CloudstrapMessagingOptions.SectionName}:SqlTransport:ConnectionStringName";

            if (string.IsNullOrWhiteSpace(sqlTransport.ConnectionStringName)
                || string.IsNullOrWhiteSpace(_configuration.GetConnectionString(sqlTransport.ConnectionStringName)))
            {
                failures.Add(
                    $"'{key}' names a connection string that does not resolve: add a " +
                    $"'ConnectionStrings:{sqlTransport.ConnectionStringName}' entry to the configuration.");
            }
        }

        private void ValidateAzureServiceBus(AzureServiceBusOptions serviceBus, List<string> failures)
        {
            const string section = $"{CloudstrapMessagingOptions.SectionName}:AzureServiceBus";

            if (!string.IsNullOrWhiteSpace(serviceBus.FullyQualifiedNamespace))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(serviceBus.ConnectionStringName))
            {
                failures.Add(
                    $"'{section}:FullyQualifiedNamespace' is required when '{CloudstrapMessagingOptions.SectionName}:Transport' " +
                    $"is {nameof(MessagingTransport.AzureServiceBus)}, unless '{section}:ConnectionStringName' names a " +
                    "'ConnectionStrings:' entry (the local-emulator fallback).");
                return;
            }

            if (string.IsNullOrWhiteSpace(_configuration.GetConnectionString(serviceBus.ConnectionStringName)))
            {
                failures.Add(
                    $"'{section}:ConnectionStringName' names a connection string that does not resolve: add a " +
                    $"'ConnectionStrings:{serviceBus.ConnectionStringName}' entry to the configuration.");
            }
        }
    }
}
