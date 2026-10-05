namespace Cloudstrap.TestInfrastructure.Tests
{
    using Microsoft.Data.SqlClient;
    using NUnit.Framework;

    /// <summary>
    /// #8 D-4 / DD-4: the shared SQL test database composes a per-suite connection string — LocalDB by default, the
    /// <c>CLOUDSTRAP_TEST_SQL</c> override's server, authentication and options otherwise, always on the suite's own
    /// catalog — and refuses unsafe catalog names before any SQL runs. No test here reads or mutates the environment
    /// variable and none needs SQL Server: CI sets the override while this suite runs.
    /// </summary>
    [TestFixture]
    public sealed class SqlServerTestDatabaseTests
    {
        private const string _ciOverride = "Server=localhost,1433;User Id=sa;Password=placeholder-not-a-real-password;TrustServerCertificate=true;";

        [Test]
        public void ComposeConnectionString_OverrideUnset_IsLocalDbIntegratedTrustedOnTheSuiteCatalog()
        {
            // Act
            SqlConnectionStringBuilder builder = new(SqlServerTestDatabase.ComposeConnectionString(null, "X"));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(builder.DataSource, Is.EqualTo(@"(localdb)\MSSQLLocalDB"));
                Assert.That(builder.IntegratedSecurity, Is.True);
                Assert.That(builder.TrustServerCertificate, Is.True);
                Assert.That(builder.InitialCatalog, Is.EqualTo("X"));
            });
        }

        [Test]
        public void ComposeConnectionString_OverrideSet_KeepsServerAuthenticationAndOptions()
        {
            // Act
            SqlConnectionStringBuilder builder = new(SqlServerTestDatabase.ComposeConnectionString(_ciOverride, "X"));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(builder.DataSource, Is.EqualTo("localhost,1433"));
                Assert.That(builder.UserID, Is.EqualTo("sa"));
                Assert.That(builder.Password, Is.EqualTo("placeholder-not-a-real-password"));
                Assert.That(builder.TrustServerCertificate, Is.True);
                Assert.That(builder.InitialCatalog, Is.EqualTo("X"));
            });
        }

        [TestCase("Database=CloudstrapCi;")]
        [TestCase("Initial Catalog=CloudstrapCi;")]
        [TestCase("Database=master;")]
        public void ComposeConnectionString_OverrideNamesACatalog_TheSuiteCatalogReplacesIt(string catalogClause)
        {
            // Act
            SqlConnectionStringBuilder builder = new(SqlServerTestDatabase.ComposeConnectionString(_ciOverride + catalogClause, "X"));

            // Assert
            Assert.That(builder.InitialCatalog, Is.EqualTo("X"));
        }

        [Test]
        public void ComposeConnectionString_OverrideWithoutACatalog_TheSuiteCatalogIsAdded()
        {
            // Act
            string composed = SqlServerTestDatabase.ComposeConnectionString(_ciOverride, "CloudstrapHangfireTests");

            // Assert
            Assert.That(new SqlConnectionStringBuilder(composed).InitialCatalog, Is.EqualTo("CloudstrapHangfireTests"));
        }

        [Test]
        public void ComposeConnectionString_EntraIdAuthenticationOverride_IsPassedThroughUntouched()
        {
            // Arrange
            const string azureSql = "Server=tcp:contoso.database.windows.net,1433;Authentication=Active Directory Default;Encrypt=true;";

            // Act
            SqlConnectionStringBuilder builder = new(SqlServerTestDatabase.ComposeConnectionString(azureSql, "X"));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(builder.Authentication, Is.EqualTo(SqlAuthenticationMethod.ActiveDirectoryDefault));
                Assert.That(builder.DataSource, Is.EqualTo("tcp:contoso.database.windows.net,1433"));
                Assert.That(builder.InitialCatalog, Is.EqualTo("X"));
            });
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        public void ComposeOverride_OverrideUnsetOrWhiteSpace_ReturnsNull(string? overrideConnectionString)
        {
            // Act
            string? composed = SqlServerTestDatabase.ComposeOverride(overrideConnectionString, "CloudstrapDemo");

            // Assert
            Assert.That(composed, Is.Null);
        }

        [Test]
        public void ComposeOverride_OverrideSet_ReturnsTheOverrideOnTheGivenCatalog()
        {
            // Act
            string? composed = SqlServerTestDatabase.ComposeOverride(_ciOverride + "Database=CloudstrapCi;", "CloudstrapDemo");

            // Assert
            SqlConnectionStringBuilder builder = new(composed);
            Assert.Multiple(() =>
            {
                Assert.That(builder.DataSource, Is.EqualTo("localhost,1433"));
                Assert.That(builder.InitialCatalog, Is.EqualTo("CloudstrapDemo"));
            });
        }

        [TestCase("")]
        [TestCase("  ")]
        [TestCase("master")]
        [TestCase("MASTER")]
        [TestCase("model")]
        [TestCase("msdb")]
        [TestCase("TempDb")]
        [TestCase("1Catalog")]
        [TestCase("_Catalog")]
        [TestCase("Cloudstrap-Tests")]
        [TestCase("Cloudstrap Tests")]
        [TestCase("x];DROP DATABASE y;--")]
        public void Constructor_InvalidCatalog_ThrowsArgumentExceptionNamingTheParameter(string catalog)
        {
            // Act & Assert
            ArgumentException? exception = Assert.Throws<ArgumentException>(() => _ = new SqlServerTestDatabase(catalog));
            Assert.That(exception!.ParamName, Is.EqualTo("catalog"));
        }

        [Test]
        public void Constructor_NullCatalog_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _ = new SqlServerTestDatabase(null!));
        }

        [Test]
        public void Constructor_ValidCatalog_ExposesTheCatalog_AndAConnectionStringTargetingIt()
        {
            // Act — CI-safe in either mode: the catalog is always the suite's own.
            SqlServerTestDatabase database = new("CloudstrapExampleTests");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(database.Catalog, Is.EqualTo("CloudstrapExampleTests"));
                Assert.That(new SqlConnectionStringBuilder(database.ConnectionString).InitialCatalog, Is.EqualTo("CloudstrapExampleTests"));
            });
        }

        [TestCase("master")]
        [TestCase("Cloudstrap-Demo")]
        public void OverrideConnectionStringFor_InvalidCatalog_Throws(string catalog)
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => SqlServerTestDatabase.OverrideConnectionStringFor(catalog));
        }

        [Test]
        public void EnvironmentVariable_IsCloudstrapTestSql()
        {
            // Act & Assert
            Assert.That(SqlServerTestDatabase.EnvironmentVariable, Is.EqualTo("CLOUDSTRAP_TEST_SQL"));
        }
    }
}
