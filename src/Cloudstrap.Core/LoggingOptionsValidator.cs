namespace Cloudstrap.Core
{
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates the conditional rules of <see cref="LoggingOptions"/> that data annotations cannot express.
    /// </summary>
    internal sealed class LoggingOptionsValidator : IValidateOptions<LoggingOptions>
    {
        /// <summary>
        /// Validates the supplied logging options.
        /// </summary>
        /// <param name="name">The options instance name, unused: the rules do not vary per name.</param>
        /// <param name="options">The options to validate.</param>
        /// <returns>The validation result.</returns>
        public ValidateOptionsResult Validate(string? name, LoggingOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (options.File.Enabled && string.IsNullOrWhiteSpace(options.File.Path))
            {
                return ValidateOptionsResult.Fail(
                    "File:Path is required when File:Enabled is true.");
            }

            return ValidateOptionsResult.Success;
        }
    }
}
