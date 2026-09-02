namespace Cloudstrap.Messaging.Tests.Infrastructure
{
    using Microsoft.Data.SqlClient;

    /// <summary>
    /// The SQL Server the durability tests run against (spec D-3): the <c>CLOUDSTRAP_TEST_SQL</c> environment
    /// variable when set, else SQL Server LocalDB. The database is dropped and recreated per fixture so every
    /// run starts from nothing; Wolverine's auto-provisioning creates the schemas and tables under test.
    /// </summary>
    internal static class SqlServerTestDatabase
    {
        /// <summary>The environment variable overriding the LocalDB default.</summary>
        public const string EnvironmentVariable = "CLOUDSTRAP_TEST_SQL";

        private const string _localDb = @"Server=(localdb)\MSSQLLocalDB;Database=CloudstrapMessagingTests;Integrated Security=true;TrustServerCertificate=true;";

        /// <summary>Gets the connection string of the test database.</summary>
        public static string ConnectionString
        {
            get;
        } =
            Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } configured ? configured : _localDb;

        /// <summary>Drops the test database if it exists and creates it empty.</summary>
        public static async Task ResetAsync()
        {
            SqlConnectionStringBuilder builder = new(ConnectionString);
            string database = builder.InitialCatalog;
            builder.InitialCatalog = "master";

            SqlConnection.ClearAllPools();
            using SqlConnection connection = new(builder.ConnectionString);
            await connection.OpenAsync();
            using SqlCommand command = connection.CreateCommand();
            command.CommandText =
                $"IF DB_ID(N'{database}') IS NOT NULL BEGIN " +
                $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END; " +
                $"CREATE DATABASE [{database}];";
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>Returns whether a table exists in the given schema.</summary>
        public static async Task<bool> TableExistsAsync(string schema, string table)
        {
            object? result = await ScalarAsync(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table",
                ("@schema", schema),
                ("@table", table));
            return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) > 0;
        }

        /// <summary>Runs a scalar query against the test database.</summary>
        public static async Task<object?> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
        {
            using SqlConnection connection = new(ConnectionString);
            await connection.OpenAsync();
            using SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            foreach ((string name, object value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return await command.ExecuteScalarAsync();
        }

        /// <summary>Polls a scalar query until it yields a non-null value or the deadline passes.</summary>
        public static async Task<object?> WaitForScalarAsync(string sql, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                object? value = await ScalarAsync(sql);
                if (value is not null && value is not DBNull)
                {
                    return value;
                }

                await Task.Delay(250);
            }

            return null;
        }
    }
}
