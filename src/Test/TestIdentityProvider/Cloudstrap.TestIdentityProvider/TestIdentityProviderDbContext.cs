namespace Cloudstrap.TestIdentityProvider
{
    using Microsoft.EntityFrameworkCore;

    /// <summary>
    /// The Entity Framework Core context backing OpenIddict's stores. The model is registered by
    /// <c>UseOpenIddict()</c> on the context options; the store itself is a per-instance in-memory SQLite
    /// database held open for the instance's lifetime.
    /// </summary>
    internal sealed class TestIdentityProviderDbContext : DbContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TestIdentityProviderDbContext"/> class.
        /// </summary>
        /// <param name="options">The context options, carrying the SQLite connection and the OpenIddict model.</param>
        public TestIdentityProviderDbContext(DbContextOptions<TestIdentityProviderDbContext> options)
            : base(options)
        {
        }
    }
}
