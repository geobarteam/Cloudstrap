namespace Cloudstrap.WebApi
{
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates the data-annotation rules declared on <see cref="CloudstrapJwtBearerOptions"/>.
    /// The rule checks are emitted by the options source generator, so validation is reflection-free.
    /// </summary>
    [OptionsValidator]
    internal sealed partial class CloudstrapJwtBearerOptionsValidator
        : IValidateOptions<CloudstrapJwtBearerOptions>
    {
    }
}
