namespace Cloudstrap.Demo.Api.Data
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Infrastructure;
    using Microsoft.EntityFrameworkCore.Storage;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>The producer's own persistence model (deliverable #14): one entity over the shared <c>demo.Orders</c> table.</summary>
    public sealed class Order
    {
        /// <summary>Gets or sets the order id.</summary>
        public Guid Id
        {
            get; set;
        }

        /// <summary>Gets or sets the free-form description — stored here, never carried by the command.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Gets or sets the status: <c>Placed</c> here, <c>Processed</c> by the Worker.</summary>
        public string Status { get; set; } = "Placed";

        /// <summary>Gets or sets the correlation id the Worker observed while processing.</summary>
        public string? ProcessedCorrelationId
        {
            get; set;
        }
    }

    /// <summary>
    /// The Api demo host's <see cref="DbContext"/>, registered through
    /// <c>AddCloudstrapTransactionalMessaging&lt;DemoDbContext&gt;</c>: the order row and the outgoing
    /// <c>PlaceOrderCommand</c> commit in one transaction (AC-MSG8 live). The producer and the consumer each
    /// own their model over the same table; no EF type ever touches the contracts project.
    /// </summary>
    public sealed class DemoDbContext(DbContextOptions<DemoDbContext> options) : DbContext(options)
    {
        /// <summary>Gets the orders.</summary>
        public DbSet<Order> Orders => Set<Order>();

        /// <summary>
        /// Demo-only bootstrap: creates the LocalDB database and the <c>demo.Orders</c> table when missing,
        /// before the messaging node starts. Production databases come from IaC and migrations. The table is
        /// created with plain SQL because EF's <c>EnsureCreated</c> is a no-op once Wolverine's own tables exist.
        /// </summary>
        /// <param name="services">The host's service provider.</param>
        public static void EnsureCreated(IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);

            using IServiceScope scope = services.CreateScope();
            DemoDbContext db = scope.ServiceProvider.GetRequiredService<DemoDbContext>();
            IRelationalDatabaseCreator creator = db.Database.GetService<IRelationalDatabaseCreator>();
            if (!creator.Exists())
            {
                creator.Create();
            }

            db.Database.ExecuteSqlRaw(
                "IF SCHEMA_ID('demo') IS NULL EXEC('CREATE SCHEMA demo'); " +
                "IF OBJECT_ID('demo.Orders') IS NULL CREATE TABLE demo.Orders (" +
                "Id uniqueidentifier NOT NULL PRIMARY KEY, Description nvarchar(200) NOT NULL, " +
                "Status nvarchar(50) NOT NULL, ProcessedCorrelationId nvarchar(200) NULL);");
        }

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().ToTable("Orders", "demo").HasKey(order => order.Id);
        }
    }
}
