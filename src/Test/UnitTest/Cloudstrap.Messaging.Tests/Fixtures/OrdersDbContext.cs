namespace Cloudstrap.Messaging.Tests.Fixtures
{
    using Microsoft.EntityFrameworkCore;

    /// <summary>The one entity of the transactional-messaging fixtures.</summary>
    public sealed class Order
    {
        /// <summary>Gets or sets the order id.</summary>
        public Guid Id
        {
            get; set;
        }

        /// <summary>Gets or sets a free-form description.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Gets or sets the order status.</summary>
        public string Status { get; set; } = "Placed";
    }

    /// <summary>A minimal test <see cref="DbContext"/> over the <c>dbo.Orders</c> table.</summary>
    public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
    {
        /// <summary>The SQL that creates the fixture table; EF's EnsureCreated is a no-op once Wolverine's tables exist.</summary>
        public const string CreateTableSql =
            "IF OBJECT_ID('dbo.Orders') IS NULL CREATE TABLE dbo.Orders (" +
            "Id uniqueidentifier NOT NULL PRIMARY KEY, Description nvarchar(200) NOT NULL, Status nvarchar(50) NOT NULL)";

        /// <summary>Gets the orders.</summary>
        public DbSet<Order> Orders => Set<Order>();

        /// <inheritdoc />
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().ToTable("Orders", "dbo").HasKey(order => order.Id);
        }
    }
}
