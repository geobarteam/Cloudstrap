namespace Cloudstrap.Messaging.AzureBlob.Tests.Fixtures
{
    using Microsoft.EntityFrameworkCore;

    /// <summary>The one entity of the outbox-interplay fixtures.</summary>
    public sealed class Export
    {
        /// <summary>Gets or sets the export id.</summary>
        public Guid Id
        {
            get; set;
        }

        /// <summary>Gets or sets a short label — never the export content.</summary>
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>A minimal test <see cref="DbContext"/> over the <c>dbo.Exports</c> table.</summary>
    public sealed class ExportsDbContext(DbContextOptions<ExportsDbContext> options) : DbContext(options)
    {
        /// <summary>The SQL that creates the fixture table; EF's EnsureCreated is a no-op once Wolverine's tables exist.</summary>
        public const string CreateTableSql =
            "IF OBJECT_ID('dbo.Exports') IS NULL CREATE TABLE dbo.Exports (" +
            "Id uniqueidentifier NOT NULL PRIMARY KEY, Label nvarchar(200) NOT NULL)";

        /// <summary>Gets the exports.</summary>
        public DbSet<Export> Exports => Set<Export>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Export>().ToTable("Exports", "dbo").HasKey(export => export.Id);
        }
    }
}
