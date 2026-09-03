namespace Cloudstrap.Demo.Worker.Data
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Infrastructure;
    using Microsoft.EntityFrameworkCore.Storage;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>The consumer's own persistence model (deliverable #14) over the shared <c>demo.Orders</c> table.</summary>
    public sealed class Order
    {
        /// <summary>Gets or sets the order id.</summary>
        public Guid Id
        {
            get; set;
        }

        /// <summary>Gets or sets the description the Api stored — read here, never logged.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Gets or sets the status this host advances to <c>Processed</c>.</summary>
        public string Status { get; set; } = "Placed";

        /// <summary>Gets or sets the correlation id observed while processing.</summary>
        public string? ProcessedCorrelationId
        {
            get; set;
        }
    }

    /// <summary>
    /// The Worker demo host's <see cref="DbContext"/>, registered through
    /// <c>AddCloudstrapTransactionalMessaging&lt;WorkerDbContext&gt;</c>: a handler taking it commits its
    /// entity changes with the message's inbox record in one transaction (AC-MSG7 live). Producer and
    /// consumer own separate models over one table; the contracts project knows nothing of EF.
    /// </summary>
    public sealed class WorkerDbContext(DbContextOptions<WorkerDbContext> options) : DbContext(options)
    {
        /// <summary>Gets the orders.</summary>
        public DbSet<Order> Orders => Set<Order>();

        /// <summary>
        /// Demo-only bootstrap: creates the LocalDB database and the <c>demo.Orders</c> table when missing,
        /// before the messaging node starts — either demo host may start first. Production databases come
        /// from IaC and migrations. Plain SQL, because EF's <c>EnsureCreated</c> is a no-op once Wolverine's
        /// own tables exist.
        /// </summary>
        /// <param name="services">The host's service provider.</param>
        public static void EnsureCreated(IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);

            using IServiceScope scope = services.CreateScope();
            WorkerDbContext db = scope.ServiceProvider.GetRequiredService<WorkerDbContext>();
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
