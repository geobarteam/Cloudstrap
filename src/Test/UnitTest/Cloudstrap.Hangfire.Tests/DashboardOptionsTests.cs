namespace Cloudstrap.Hangfire.Tests
{
    using global::Hangfire;
    using global::Hangfire.Dashboard;
    using NUnit.Framework;

    /// <summary>
    /// The composed <see cref="DashboardOptions"/> (D-1, AC-HF12): the Cloudstrap authenticated-only filter is
    /// always first and cannot be removed, consumer filters are additive, read-only follows configuration, and
    /// the consumer's delegate otherwise has the final say.
    /// </summary>
    [TestFixture]
    public sealed class DashboardOptionsTests
    {
        [Test]
        public void Composer_CloudstrapFilterIsFirst_EvenWhenConfigureClearsOrReplacesAuthorization()
        {
            // Act
            DashboardOptions cleared = DashboardOptionsComposer.Create(readOnly: false, options => options.Authorization = []);
            DashboardOptions replaced = DashboardOptionsComposer.Create(readOnly: false, options => options.Authorization = [new AllowAll()]);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(cleared.Authorization.First(), Is.InstanceOf<AuthenticatedDashboardAuthorizationFilter>());
                Assert.That(cleared.Authorization.Count(), Is.EqualTo(1));
                Assert.That(replaced.Authorization.First(), Is.InstanceOf<AuthenticatedDashboardAuthorizationFilter>());
            });
        }

        [Test]
        public void Composer_ConsumerFilters_AreKeptAfterTheFloor_AllMustPass()
        {
            // Arrange
            AllowAll consumer = new();

            // Act
            DashboardOptions options = DashboardOptionsComposer.Create(readOnly: false, dashboard => dashboard.Authorization = [consumer]);

            // Assert
            Assert.That(options.Authorization, Is.EqualTo(new IDashboardAuthorizationFilter[] { options.Authorization.First(), consumer }));
        }

        [Test]
        public void Composer_DefaultHasOnlyTheFloor_NeverHangfiresLocalRequestsOnlyFilter()
        {
            // Act
            DashboardOptions options = DashboardOptionsComposer.Create(readOnly: false, configure: null);

            // Assert
            Assert.That(options.Authorization.Select(filter => filter.GetType()), Is.EqualTo(new[] { typeof(AuthenticatedDashboardAuthorizationFilter) }));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Composer_ReadOnly_FollowsConfiguration(bool readOnly)
        {
            // Act
            DashboardOptions options = DashboardOptionsComposer.Create(readOnly, configure: null);

            // Assert
            Assert.That(options.IsReadOnlyFunc(null!), Is.EqualTo(readOnly));
        }

        [Test]
        public void Composer_ConfigureRunsLast_ForOtherOptions()
        {
            // Act
            DashboardOptions options = DashboardOptionsComposer.Create(readOnly: true, dashboard =>
            {
                dashboard.DashboardTitle = "Contoso jobs";
                dashboard.IsReadOnlyFunc = _ => false;
            });

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(options.DashboardTitle, Is.EqualTo("Contoso jobs"));
                Assert.That(options.IsReadOnlyFunc(null!), Is.False);
            });
        }

        [Test]
        public void Composer_NeverSetsIgnoreAntiforgeryToken()
        {
            // Act
            DashboardOptions options = DashboardOptionsComposer.Create(readOnly: false, configure: null);

            // Assert
            Assert.That(options.IgnoreAntiforgeryToken, Is.False);
        }

        private sealed class AllowAll : IDashboardAuthorizationFilter
        {
            public bool Authorize(DashboardContext context)
            {
                return true;
            }
        }
    }
}
