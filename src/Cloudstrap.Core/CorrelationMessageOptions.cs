namespace Cloudstrap.Core
{
    /// <summary>
    /// Correlation settings for message handlers, bound from the
    /// <c>Cloudstrap:Correlation:Message</c> configuration section.
    /// </summary>
    public sealed class CorrelationMessageOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether every message handler must carry a correlation identifier.
        /// The handlers listed in <see cref="ExcludeMessageHandlers"/> are exempt.
        /// </summary>
        /// <value><see langword="true"/> when correlation is mandatory. Defaults to <see langword="false"/>.</value>
        public bool RequireForAllMessageHandlers
        {
            get; set;
        }

        /// <summary>
        /// Gets the message handlers exempt from the correlation requirement, by full type name.
        /// </summary>
        /// <value>The exempt handler type names. Empty by default.</value>
        public List<string> ExcludeMessageHandlers { get; } = [];
    }
}
