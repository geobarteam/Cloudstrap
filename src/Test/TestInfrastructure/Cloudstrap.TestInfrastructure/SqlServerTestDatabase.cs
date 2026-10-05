namespace Cloudstrap.TestInfrastructure
{
    using System.Globalization;
    using System.Text.RegularExpressions;
    using Microsoft.Data.SqlClient;

    /// <summary>
    /// A SQL Server database owned by one test suite: SQL Server LocalDB by default, or the server, authentication and
    /// options of the <c>CLOUDSTRAP_TEST_SQL</c> environment variable when it is set — always on the suite's own
    /// catalog, whatever catalog the override names. The connecting identity needs <c>CREATE DATABASE</c>
    /// permission, because <see cref="ResetAsync"/> drops and recreates the catalog. Fixtures sharing one catalog must
    /// not run in parallel. An Azure SQL override such as <c>Authentication=Active Directory Default</c> passes through
    /// untouched.
    /// </summary>
    public sealed partial class SqlServerTestDatabase
    {
        /// <summary>The environment variable supplying server, authentication and options in place of LocalDB.</summary>
        public const string EnvironmentVariable = "CLOUDSTRAP_TEST_SQL";

        private const string _localDb = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true;";

        private static readonly string[] _systemDatabases = ["master", "model", "msdb", "tempdb"];

        /// <summary>Initializes a new instance of the <see cref="SqlServerTestDatabase"/> class.</summary>
        /// <param name="catalog">The suite's own catalog: a letter followed by letters, digits or underscores; never a system database.</param>
        /// <exception cref="ArgumentNullException"><paramref name="catalog"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="catalog"/> is empty, a system database or not a safe identifier.</exception>
        public SqlServerTestDatabase(string catalog)
        {
            Validate(catalog);
            Catalog = catalog;
            ConnectionString = ComposeConnectionString(Environment.GetEnvironmentVariable(EnvironmentVariable), catalog);
        }

        /// <summary>Gets the suite's catalog.</summary>
        public string Catalog
        {
            get;
        }

        /// <summary>Gets the connection string of the suite's catalog.</summary>
        public string ConnectionString
        {
            get;
        }

        /// <summary>
        /// Returns the <c>CLOUDSTRAP_TEST_SQL</c> override retargeted to <paramref name="catalog"/>, or
        /// <see langword="null"/> when the variable is unset — the entry point for suites that forward the override to
        /// a host they launch rather than connect themselves.
        /// </summary>
        /// <param name="catalog">The catalog the forwarded connection string targets.</param>
        /// <returns>The retargeted override, or <see langword="null"/> when no override is set.</returns>
        /// <exception cref="ArgumentException"><paramref name="catalog"/> is not a valid catalog name.</exception>
        public static string? OverrideConnectionStringFor(string catalog)
        {
            Validate(catalog);
            return ComposeOverride(Environment.GetEnvironmentVariable(EnvironmentVariable), catalog);
        }

        /// <summary>Drops the catalog if it exists and creates it empty.</summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task completing when the catalog is recreated.</returns>
        public async Task ResetAsync(CancellationToken cancellationToken = default)
        {
            SqlConnectionStringBuilder master = new(ConnectionString) { InitialCatalog = "master" };

            // Only this catalog's pooled connections: other suites' pools in the process are left alone.
            using (SqlConnection own = new(ConnectionString))
            {
                SqlConnection.ClearPool(own);
            }

            using SqlConnection connection = new(master.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            using SqlCommand command = connection.CreateCommand();

            // Catalog is validated as [A-Za-z][A-Za-z0-9_]* and bracket-quoted: nothing else is ever interpolated.
            command.CommandText =
                $"IF DB_ID(N'{Catalog}') IS NOT NULL BEGIN " +
                $"ALTER DATABASE [{Catalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Catalog}]; END; " +
                $"CREATE DATABASE [{Catalog}];";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        /// <summary>Returns whether a table exists in the given schema.</summary>
        /// <param name="schema">The schema name.</param>
        /// <param name="table">The table name.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns><see langword="true"/> when the table exists.</returns>
        public async Task<bool> TableExistsAsync(string schema, string table, CancellationToken cancellationToken = default)
        {
            object? result = await ScalarAsync(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table",
                [("@schema", schema), ("@table", table)],
                cancellationToken);
            return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
        }

        /// <summary>Runs a scalar query against the catalog.</summary>
        /// <param name="sql">The query text.</param>
        /// <param name="parameters">The query parameters, if any.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The first column of the first row, or <see langword="null"/>.</returns>
        public async Task<object?> ScalarAsync(
            string sql,
            IReadOnlyList<(string Name, object Value)>? parameters = null,
            CancellationToken cancellationToken = default)
        {
            using SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync(cancellationToken);
            using SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            foreach ((string name, object value) in parameters ?? [])
            {
                command.Parameters.AddWithValue(name, value);
            }

            return await command.ExecuteScalarAsync(cancellationToken);
        }

        /// <summary>Polls a scalar query until it yields a non-null value or the deadline passes.</summary>
        /// <param name="sql">The query text.</param>
        /// <param name="timeout">How long to keep polling.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The first non-null value, or <see langword="null"/> when the deadline passes.</returns>
        public async Task<object?> WaitForScalarAsync(string sql, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                object? value = await ScalarAsync(sql, cancellationToken: cancellationToken);
                if (value is not null && value is not DBNull)
                {
                    return value;
                }

                await Task.Delay(250, cancellationToken);
            }

            return null;
        }

        /// <summary>Composes the suite's connection string: the override (or LocalDB) with the catalog always replaced.</summary>
        internal static string ComposeConnectionString(string? overrideConnectionString, string catalog)
        {
            SqlConnectionStringBuilder builder = new(string.IsNullOrWhiteSpace(overrideConnectionString) ? _localDb : overrideConnectionString)
            {
                InitialCatalog = catalog,
            };
            return builder.ConnectionString;
        }

        /// <summary>Composes the forwarded override: <see langword="null"/> when unset, else retargeted to the catalog.</summary>
        internal static string? ComposeOverride(string? overrideConnectionString, string catalog)
        {
            return string.IsNullOrWhiteSpace(overrideConnectionString)
                ? null
                : ComposeConnectionString(overrideConnectionString, catalog);
        }

        private static void Validate(string catalog)
        {
            ArgumentNullException.ThrowIfNull(catalog);
            if (string.IsNullOrWhiteSpace(catalog))
            {
                throw new ArgumentException("The catalog name must not be empty.", nameof(catalog));
            }

            if (_systemDatabases.Contains(catalog, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"'{catalog}' is a system database; a test suite never resets it.", nameof(catalog));
            }

            if (!SafeIdentifier().IsMatch(catalog))
            {
                throw new ArgumentException(
                    $"'{catalog}' is not a safe catalog name: a letter followed by letters, digits or underscores.",
                    nameof(catalog));
            }
        }

        [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
        private static partial Regex SafeIdentifier();
    }
}
