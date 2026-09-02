namespace Cloudstrap.Messaging
{
    /// <summary>
    /// The transport a Cloudstrap messaging node moves messages over. Selected by
    /// <c>Cloudstrap:Messaging:Transport</c>; the node's code never changes when the value does.
    /// </summary>
    public enum MessagingTransport
    {
        /// <summary>
        /// In-process queues — zero infrastructure, works on a fresh clone. The default.
        /// </summary>
        Local = 0,

        /// <summary>
        /// Azure Service Bus: queues per workload, a topic per event type, subscriptions per consuming
        /// workload; authenticated with <c>DefaultAzureCredential</c> against
        /// <see cref="AzureServiceBusOptions.FullyQualifiedNamespace"/>.
        /// </summary>
        AzureServiceBus = 1,

        /// <summary>
        /// SQL Server queue tables on the connection string named by
        /// <see cref="SqlTransportOptions.ConnectionStringName"/> — queues only, no topics.
        /// </summary>
        SqlServer = 2,
    }
}
