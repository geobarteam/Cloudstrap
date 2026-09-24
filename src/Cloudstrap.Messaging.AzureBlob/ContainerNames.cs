namespace Cloudstrap.Messaging.AzureBlob
{
    using System.Text.RegularExpressions;

    /// <summary>
    /// The claim-check container naming convention and Azure's container naming rules.
    /// </summary>
    internal static partial class ContainerNames
    {
        /// <summary>The suffix appended to the lower-cased system name for the default container.</summary>
        public const string DefaultSuffix = "-claimcheck";

        /// <summary>
        /// Computes the default container name for a system: <c>{systemName}-claimcheck</c>, lower-cased.
        /// </summary>
        /// <param name="systemName">The system name from <c>Cloudstrap:Application:SystemName</c>.</param>
        /// <returns>The default container name.</returns>
        public static string Default(string systemName)
        {
            ArgumentNullException.ThrowIfNull(systemName);

            return $"{systemName}{DefaultSuffix}".ToLowerInvariant();
        }

        /// <summary>
        /// Returns whether a name satisfies Azure's blob container naming rules: 3–63 characters of
        /// lower-case letters, digits and hyphens, starting and ending alphanumeric, no consecutive hyphens.
        /// </summary>
        /// <param name="name">The candidate name.</param>
        /// <returns><see langword="true"/> when the name is valid.</returns>
        public static bool IsValid(string? name)
        {
            return name is not null && ContainerNamePattern().IsMatch(name);
        }

        [GeneratedRegex("^(?!.*--)[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$", RegexOptions.CultureInvariant)]
        private static partial Regex ContainerNamePattern();
    }
}
