namespace Cloudstrap.Messaging
{
    /// <summary>
    /// Derives SQL schema names from workload names: lowercase, every non-alphanumeric character replaced by
    /// <c>_</c>, so <c>contoso-orders-worker</c> becomes <c>contoso_orders_worker</c>.
    /// </summary>
    internal static class SchemaNames
    {
        /// <summary>
        /// Sanitizes a workload name into a single SQL schema identifier.
        /// </summary>
        /// <param name="workloadName">The workload name.</param>
        /// <returns>The sanitized schema name.</returns>
        public static string Sanitize(string workloadName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(workloadName);

            char[] characters = new char[workloadName.Length];
            for (int i = 0; i < workloadName.Length; i++)
            {
                char c = workloadName[i];
                characters[i] = char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_';
            }

            return new string(characters);
        }
    }
}
