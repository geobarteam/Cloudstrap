namespace Cloudstrap.Authentication.ClientCredentials
{
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates the data-annotation rules declared on <see cref="CloudstrapClientCredentialsOptions"/>.
    /// The rule checks are emitted by the options source generator, so validation is reflection-free.
    /// Every failure message names the offending <c>Cloudstrap:ClientCredentials:*</c> key and never
    /// echoes a configured value (AC-CC5).
    /// </summary>
    [OptionsValidator]
    internal sealed partial class CloudstrapClientCredentialsOptionsValidator
        : IValidateOptions<CloudstrapClientCredentialsOptions>
    {
    }

    /// <summary>
    /// Validates the parse-shaped rule the data annotations cannot express: the token endpoint must be an
    /// absolute URL — there is no authority-plus-path convention.
    /// </summary>
    internal sealed class TokenEndpointShapeValidator : IValidateOptions<CloudstrapClientCredentialsOptions>
    {
        /// <inheritdoc/>
        public ValidateOptionsResult Validate(string? name, CloudstrapClientCredentialsOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (options.TokenEndpoint is { IsAbsoluteUri: false })
            {
                return ValidateOptionsResult.Fail(
                    "'Cloudstrap:ClientCredentials:TokenEndpoint' must be an absolute URL — copy the"
                    + " token endpoint from the identity provider's discovery document.");
            }

            return ValidateOptionsResult.Success;
        }
    }
}
