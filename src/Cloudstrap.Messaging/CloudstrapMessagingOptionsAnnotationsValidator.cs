namespace Cloudstrap.Messaging
{
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates the data-annotation rules declared on <see cref="CloudstrapMessagingOptions"/>.
    /// The rule checks are emitted by the options source generator, so validation is reflection-free.
    /// </summary>
    [OptionsValidator]
    internal sealed partial class CloudstrapMessagingOptionsAnnotationsValidator : IValidateOptions<CloudstrapMessagingOptions>
    {
    }
}
