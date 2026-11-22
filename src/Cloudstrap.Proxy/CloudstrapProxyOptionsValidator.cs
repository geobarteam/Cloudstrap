namespace Cloudstrap.Proxy
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates the data-annotation rules declared on <see cref="CloudstrapProxyOptions"/>.
    /// The rule checks are emitted by the options source generator, so validation is reflection-free.
    /// Every failure message names the offending <c>Cloudstrap:Proxy:*</c> key (AC-PX10).
    /// </summary>
    [OptionsValidator]
    internal sealed partial class CloudstrapProxyOptionsValidator : IValidateOptions<CloudstrapProxyOptions>
    {
    }

    /// <summary>
    /// Validates the rule the data annotations cannot express: the YARP configuration section named by
    /// <see cref="CloudstrapProxyOptions.ConfigurationSection"/> exists, so a typo in the section name fails startup
    /// instead of silently proxying nothing.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    internal sealed class ProxyConfigurationSectionValidator(IConfiguration configuration)
        : IValidateOptions<CloudstrapProxyOptions>
    {
        /// <inheritdoc/>
        public ValidateOptionsResult Validate(string? name, CloudstrapProxyOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            // An empty name is the Required rule's failure; reporting it twice adds nothing.
            if (string.IsNullOrWhiteSpace(options.ConfigurationSection)
                || configuration.GetSection(options.ConfigurationSection).Exists())
            {
                return ValidateOptionsResult.Success;
            }

            return ValidateOptionsResult.Fail(
                $"'{CloudstrapProxyOptions.SectionName}:ConfigurationSection' names the configuration section"
                + $" '{options.ConfigurationSection}', which does not exist. Add the YARP routes and clusters under"
                + $" '{options.ConfigurationSection}', or point the key at the section that holds them.");
        }
    }
}
