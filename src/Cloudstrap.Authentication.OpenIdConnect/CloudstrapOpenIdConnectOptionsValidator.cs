namespace Cloudstrap.Authentication.OpenIdConnect
{
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates the data-annotation rules declared on <see cref="CloudstrapOpenIdConnectOptions"/>.
    /// The rule checks are emitted by the options source generator, so validation is reflection-free.
    /// Every failure message names the offending <c>Cloudstrap:OpenIdConnect:*</c> key and never echoes
    /// a configured value (AC-OIDC6, AC-OIDC7).
    /// </summary>
    [OptionsValidator]
    internal sealed partial class CloudstrapOpenIdConnectOptionsValidator
        : IValidateOptions<CloudstrapOpenIdConnectOptions>
    {
    }

    /// <summary>
    /// Validates the parse-shaped rules the data annotations cannot express: the authority must be an
    /// absolute URL, and the cookie lifetime must be positive.
    /// </summary>
    internal sealed class OpenIdConnectShapeValidator : IValidateOptions<CloudstrapOpenIdConnectOptions>
    {
        /// <inheritdoc/>
        public ValidateOptionsResult Validate(string? name, CloudstrapOpenIdConnectOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            List<string> failures = [];

            if (!string.IsNullOrEmpty(options.Authority)
                && !Uri.TryCreate(options.Authority, UriKind.Absolute, out _))
            {
                failures.Add(
                    "'Cloudstrap:OpenIdConnect:Authority' must be an absolute URL — the identity"
                    + " provider's base address, for example 'https://idp.example.com/'.");
            }

            if (options.Cookie.Lifetime <= TimeSpan.Zero)
            {
                failures.Add(
                    "'Cloudstrap:OpenIdConnect:Cookie:Lifetime' must be greater than zero.");
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }
    }
}
