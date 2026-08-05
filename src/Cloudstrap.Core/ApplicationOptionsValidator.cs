namespace Cloudstrap.Core
{
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates the data-annotation rules declared on <see cref="ApplicationOptions"/>.
    /// The rule checks are emitted by the options source generator, so validation is reflection-free.
    /// </summary>
    [OptionsValidator]
    internal sealed partial class ApplicationOptionsValidator : IValidateOptions<ApplicationOptions>
    {
    }
}
