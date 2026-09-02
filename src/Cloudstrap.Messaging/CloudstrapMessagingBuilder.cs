namespace Cloudstrap.Messaging
{
    using Microsoft.Extensions.Hosting;

    /// <summary>
    /// The builder <see cref="HostApplicationBuilderExtensions.AddCloudstrapMessaging"/> returns: the seam on
    /// which durability providers and the transactional EF Core integration are chosen.
    /// </summary>
    /// <remarks>
    /// This type is public and sealed on purpose: a future durability provider (PostgreSQL, for example) arrives
    /// as an extension method on this builder from its own leaf package — additively, with no signature change
    /// here. Builder calls compose regardless of order; every choice is applied when the host starts.
    /// </remarks>
    public sealed class CloudstrapMessagingBuilder
    {
        internal CloudstrapMessagingBuilder(IHostApplicationBuilder hostBuilder, MessagingRegistrationState state)
        {
            HostBuilder = hostBuilder;
            State = state;
        }

        /// <summary>
        /// Gets the host application builder the messaging node was registered on.
        /// </summary>
        /// <value>The host application builder.</value>
        public IHostApplicationBuilder HostBuilder
        {
            get;
        }

        internal MessagingRegistrationState State
        {
            get;
        }
    }
}
