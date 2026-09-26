namespace Cloudstrap.Hangfire
{
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates <see cref="HangfireOptions"/>. Every failure names the offending configuration key and never echoes a value.
    /// </summary>
    internal sealed class HangfireOptionsValidator : IValidateOptions<HangfireOptions>
    {
        private const string _storage = $"{HangfireOptions.SectionName}:Storage";
        private const string _server = $"{HangfireOptions.SectionName}:Server";
        private const string _dashboard = $"{HangfireOptions.SectionName}:Dashboard";

        /// <summary>
        /// Validates the supplied options, reporting every failure rather than stopping at the first.
        /// </summary>
        /// <param name="name">The options instance name, unused: the rules do not vary per name.</param>
        /// <param name="options">The options to validate.</param>
        /// <returns>The validation result, carrying one failure per broken rule.</returns>
        public ValidateOptionsResult Validate(string? name, HangfireOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            List<string> failures = [];

            if (string.IsNullOrWhiteSpace(options.Storage.ConnectionStringName))
            {
                failures.Add($"'{_storage}:ConnectionStringName' must name a 'ConnectionStrings:' entry.");
            }

            if (options.Storage.SchemaName is not null && string.IsNullOrWhiteSpace(options.Storage.SchemaName))
            {
                failures.Add($"'{_storage}:SchemaName' must be omitted or name a schema.");
            }

            if (options.Server.WorkerCount is < 1)
            {
                failures.Add($"'{_server}:WorkerCount' must be omitted or at least 1.");
            }

            if (options.Server.Queues is { } queues && queues.Any(string.IsNullOrWhiteSpace))
            {
                failures.Add($"'{_server}:Queues' must not contain an empty queue name.");
            }

            string path = options.Dashboard.Path;
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/') || (path.Length > 1 && path.EndsWith('/')))
            {
                failures.Add($"'{_dashboard}:Path' must be a rooted path without a trailing slash, such as '/hangfire'.");
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }
    }
}
