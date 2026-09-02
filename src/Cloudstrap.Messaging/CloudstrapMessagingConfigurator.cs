namespace Cloudstrap.Messaging
{
    using Wolverine;

    /// <summary>
    /// Code-level hooks over the messaging node's configuration-driven defaults, supplied to
    /// <see cref="HostApplicationBuilderExtensions.AddCloudstrapMessaging"/>. The hooks run in a fixed
    /// order: the Cloudstrap defaults, then <see cref="Conventions"/>, then <see cref="Wolverine"/> last.
    /// </summary>
    public sealed class CloudstrapMessagingConfigurator
    {
        /// <summary>
        /// Gets or sets the delegate that replaces or extends the message classification and routing rules.
        /// It receives the default <see cref="MessageConventions"/> after configuration has been bound, so a
        /// rule can wrap the default or discard it.
        /// </summary>
        /// <value>The delegate, or <see langword="null"/> to keep the suffix conventions and the <c>Destinations</c> map.</value>
        public Action<MessageConventions>? Conventions
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the delegate with final say over the engine: it runs <em>last</em>, after every
        /// Cloudstrap default (identity, transport, conventions, retries, dead-lettering, durability), so
        /// anything it sets on the <see cref="WolverineOptions"/> — serializers, discovery, listeners,
        /// endpoints, policies — wins.
        /// </summary>
        /// <value>The delegate, or <see langword="null"/> to keep the defaults.</value>
        public Action<WolverineOptions>? Wolverine
        {
            get; set;
        }
    }
}
