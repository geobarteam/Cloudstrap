namespace Cloudstrap.Messaging
{
    using Cloudstrap.Core;
    using Wolverine;

    /// <summary>
    /// The registration-time facts of the one messaging node a process hosts: its presence in the service
    /// collection is what makes a second <c>AddCloudstrapMessaging</c> call fail fast; it carries the eagerly
    /// bound options, the conventions, the consumer's configurator and the engine's options instance to the
    /// builder methods and the deferred bootstrap.
    /// </summary>
    internal sealed class MessagingRegistrationState
    {
        public MessagingRegistrationState(
            CloudstrapMessagingConfigurator configurator,
            CloudstrapMessagingOptions messaging,
            ApplicationOptions application,
            MessageConventions conventions,
            bool autoProvision)
        {
            Configurator = configurator;
            Messaging = messaging;
            Application = application;
            Conventions = conventions;
            AutoProvision = autoProvision;
        }

        /// <summary>
        /// Gets a value indicating whether the node provisions its own resources at startup: the explicit
        /// <c>Cloudstrap:Messaging:AutoProvision</c> value, else <see langword="true"/> in <c>Development</c> only.
        /// </summary>
        public bool AutoProvision
        {
            get;
        }

        /// <summary>Gets the code-level hooks the consumer supplied at registration.</summary>
        public CloudstrapMessagingConfigurator Configurator
        {
            get;
        }

        /// <summary>Gets the <c>Cloudstrap:Messaging</c> section as bound at the registration call.</summary>
        public CloudstrapMessagingOptions Messaging
        {
            get;
        }

        /// <summary>Gets the <c>Cloudstrap:Application</c> section as bound at the registration call.</summary>
        public ApplicationOptions Application
        {
            get;
        }

        /// <summary>Gets the effective message conventions (defaults plus the consumer's adjustments).</summary>
        public MessageConventions Conventions
        {
            get;
        }

        /// <summary>Gets the node's endpoint identity: the configured name or the workload name.</summary>
        public string EndpointName => Messaging.EndpointName ?? Application.WorkloadName;

        /// <summary>
        /// Gets the transport-level error queue name: the configured <c>DeadLetter:QueueName</c> or
        /// <c>{SystemName}-error</c>.
        /// </summary>
        public string DeadLetterQueueName => Messaging.DeadLetter.QueueName ?? $"{Application.SystemName}-error";

        /// <summary>
        /// Gets or sets the engine's options instance, captured when the engine is registered so later
        /// builder calls — which may register services — can still shape it before the host is built.
        /// </summary>
        public WolverineOptions? Wolverine
        {
            get; set;
        }
    }
}
