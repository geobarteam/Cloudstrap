namespace Cloudstrap.Messaging
{
    /// <summary>
    /// Azure Service Bus transport settings, bound from <c>Cloudstrap:Messaging:AzureServiceBus</c>.
    /// Exactly one of the two members must resolve when the transport is
    /// <see cref="MessagingTransport.AzureServiceBus"/>; there is deliberately no tenant, client id, secret
    /// or key setting — credentials come from <c>DefaultAzureCredential</c>.
    /// </summary>
    public sealed class AzureServiceBusOptions
    {
        /// <summary>
        /// Gets or sets the namespace host name (for example <c>contoso.servicebus.windows.net</c>), authenticated
        /// with <c>DefaultAzureCredential</c> — environment variables, workload identity or managed identity,
        /// identical across App Service and AKS.
        /// </summary>
        /// <value>The fully qualified namespace, or <see langword="null"/> to fall back to <see cref="ConnectionStringName"/>.</value>
        public string? FullyQualifiedNamespace
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the name of the <c>ConnectionStrings:</c> entry to connect with — the fallback for local
        /// emulators when <see cref="FullyQualifiedNamespace"/> is not set. The value is a name, never the
        /// connection string itself.
        /// </summary>
        /// <value>The connection string name, or <see langword="null"/> when the namespace is used.</value>
        public string? ConnectionStringName
        {
            get; set;
        }
    }
}
