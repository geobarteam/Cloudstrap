namespace Cloudstrap.Observability
{
    using Cloudstrap.Core;
    using Microsoft.Extensions.Hosting;
    using OpenTelemetry.Resources;

    /// <summary>
    /// The Cloudstrap resource identity: standard semantic-convention attributes plus the
    /// <c>cloudstrap.*</c> vocabulary shared by every package in the suite.
    /// </summary>
    internal static class CloudstrapResourceAttributes
    {
        internal const string SystemName = "cloudstrap.system.name";
        internal const string SubsystemName = "cloudstrap.subsystem.name";
        internal const string SubsystemType = "cloudstrap.subsystem.type";
        internal const string EnvironmentTier = "cloudstrap.environment.tier";
        internal const string DeploymentEnvironment = "deployment.environment.name";
        internal const string HostName = "host.name";

        /// <summary>
        /// Applies the full owner-mode resource identity: <c>service.name</c> from the workload name, the
        /// deployment environment and host name, and the <c>cloudstrap.*</c> attributes.
        /// </summary>
        /// <param name="resourceBuilder">The resource builder to apply the identity to.</param>
        /// <param name="application">The application identity settings.</param>
        /// <param name="environment">The host environment the deployment environment name is read from.</param>
        public static void ApplyOwnerIdentity(
            ResourceBuilder resourceBuilder,
            ApplicationOptions application,
            IHostEnvironment environment)
        {
            resourceBuilder.AddService(application.WorkloadName);
            resourceBuilder.AddAttributes(
            [
                new(DeploymentEnvironment, environment.EnvironmentName),
                new(HostName, Environment.MachineName),
            ]);
            ApplyCloudstrapAttributes(resourceBuilder, application);
        }

        /// <summary>
        /// Applies only the <c>cloudstrap.*</c> attributes — <c>cloudstrap.environment.tier</c> when
        /// configured — leaving <c>service.name</c> and the environment identity to whoever owns the pipeline.
        /// </summary>
        /// <param name="resourceBuilder">The resource builder to apply the attributes to.</param>
        /// <param name="application">The application identity settings.</param>
        public static void ApplyCloudstrapAttributes(
            ResourceBuilder resourceBuilder,
            ApplicationOptions application)
        {
            List<KeyValuePair<string, object>> attributes =
            [
                new(SystemName, application.SystemName),
                new(SubsystemName, application.SubsystemName),
                new(SubsystemType, application.SubsystemType),
            ];

            if (!string.IsNullOrWhiteSpace(application.EnvironmentTier))
            {
                attributes.Add(new(EnvironmentTier, application.EnvironmentTier));
            }

            resourceBuilder.AddAttributes(attributes);
        }
    }
}
