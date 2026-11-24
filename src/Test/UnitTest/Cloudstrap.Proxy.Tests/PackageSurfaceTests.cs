namespace Cloudstrap.Proxy.Tests
{
    using System.Reflection;
    using Cloudstrap.Proxy.Tests.Infrastructure;
    using NUnit.Framework;

    /// <summary>
    /// Permanent tripwires on the shipped surface of <c>Cloudstrap.Proxy</c> (AC-PX11, AC-PX13): exactly the public
    /// types and members approved at Gate 3, every public class sealed or static, the metadata keys and token mode
    /// values pinned as one-way doors, and a package README that states the trusted-subsystem rules.
    /// </summary>
    [TestFixture]
    public sealed class PackageSurfaceTests
    {
        private const BindingFlags _declaredPublic = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private static readonly Assembly _proxyAssembly = typeof(CloudstrapProxyOptions).Assembly;

        private static readonly string[] _approvedPublicTypeNames =
        [
            "CloudstrapProxyOptions",
            "EndpointRouteBuilderExtensions",
            "HostApplicationBuilderExtensions",
            "ProxyMetadataKeys",
            "ProxyTokenMode",
        ];

        /// <summary>
        /// Every public member, per type, as <c>Type.Member</c>. <c>CloudstrapProxyOptions.SectionName</c> follows the
        /// suite-wide options convention (every Cloudstrap options class exposes it).
        /// </summary>
        private static readonly string[] _approvedPublicMembers =
        [
            "CloudstrapProxyOptions..ctor",
            "CloudstrapProxyOptions.ConfigurationSection",
            "CloudstrapProxyOptions.DefaultTokenMode",
            "CloudstrapProxyOptions.SectionName",
            "CloudstrapProxyOptions.StripInboundCredentials",
            "EndpointRouteBuilderExtensions.MapCloudstrapProxy",
            "HostApplicationBuilderExtensions.AddCloudstrapProxy",
            "ProxyMetadataKeys.AllowAnonymousWithToken",
            "ProxyMetadataKeys.ForwardInboundCredentials",
            "ProxyMetadataKeys.TokenMode",
            "ProxyTokenMode.Client",
            "ProxyTokenMode.None",
            "ProxyTokenMode.User",
        ];

        private static readonly string[] _planReadmeTerms =
        [
            "Cookie", "Authorization", "AllowAnonymousWithToken", "ForwardInboundCredentials", "TokenMode",
            "Cloudstrap.Authentication.ClientCredentials", "IDestinationResolver",
        ];

        [Test]
        public void PublicTypes_AreExactlyTheIntendedSurface()
        {
            // Act
            Type[] publicTypes = _proxyAssembly.GetExportedTypes();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(publicTypes.Select(type => type.Name), Is.EquivalentTo(_approvedPublicTypeNames));
                Assert.That(publicTypes.Where(type => type.Namespace != "Cloudstrap.Proxy"), Is.Empty);
                Assert.That(publicTypes.Where(type => type.IsInterface), Is.Empty);
            });
        }

        [Test]
        public void PublicMembers_AreExactlyTheApprovedMembers()
        {
            // Act: properties are listed once (not their accessors); enum members exclude the runtime's value__ field
            string[] members = [.. _proxyAssembly.GetExportedTypes()
                .SelectMany(type => type.GetMembers(_declaredPublic)
                    .Where(member => member is not MethodInfo { IsSpecialName: true })
                    .Where(member => member.Name != "value__")
                    .Select(member => $"{type.Name}.{member.Name}"))
                .Distinct(StringComparer.Ordinal)];

            // Assert
            Assert.That(members, Is.EquivalentTo(_approvedPublicMembers));
        }

        [Test]
        public void PublicClasses_AreSealedOrStatic()
        {
            // Act: a static class is abstract and sealed in metadata
            string[] unsealed = [.. _proxyAssembly.GetExportedTypes()
                .Where(type => type.IsClass && !type.IsSealed)
                .Select(type => type.Name)];

            // Assert
            Assert.That(unsealed, Is.Empty);
        }

        [Test]
        public void ProxyMetadataKeys_HaveTheSpecifiedValues()
        {
            // Assert: configuration files in the field use these strings; changing one is a breaking change
            Assert.Multiple(() =>
            {
                Assert.That(ProxyMetadataKeys.TokenMode, Is.EqualTo("Cloudstrap.TokenMode"));
                Assert.That(ProxyMetadataKeys.ForwardInboundCredentials, Is.EqualTo("Cloudstrap.ForwardInboundCredentials"));
                Assert.That(ProxyMetadataKeys.AllowAnonymousWithToken, Is.EqualTo("Cloudstrap.AllowAnonymousWithToken"));
                Assert.That(CloudstrapProxyOptions.SectionName, Is.EqualTo("Cloudstrap:Proxy"));
            });
        }

        [Test]
        public void ProxyTokenMode_HasTheSpecifiedNamesAndValues()
        {
            // Act
            Dictionary<string, int> modes = Enum.GetValues<ProxyTokenMode>().ToDictionary(mode => mode.ToString(), mode => (int)mode);

            // Assert
            Assert.That(modes, Is.EqualTo(new Dictionary<string, int> { ["None"] = 0, ["Client"] = 1, ["User"] = 2 }));
        }

        [Test]
        public void PackageReadme_CoversTheTrustedSubsystemRules()
        {
            // Arrange
            string readmePath = Path.Combine(RepositoryPaths.Root, "src", "Cloudstrap.Proxy", "README.md");

            // Act
            string readme = File.Exists(readmePath) ? File.ReadAllText(readmePath) : string.Empty;

            // Assert
            Assert.Multiple(() =>
            {
                // the plan's required terms
                foreach (string term in _planReadmeTerms)
                {
                    Assert.That(readme, Does.Contain(term), term);
                }

                // the trusted-subsystem pattern, the signed-in default and the unsafe opt-outs
                Assert.That(readme, Does.Contain("## The trusted-subsystem pattern"));
                Assert.That(readme, Does.Contain("signed-in user"));
                Assert.That(readme, Does.Contain("StripInboundCredentials"));
                Assert.That(readme, Does.Contain("unsafe"));

                // token modes, the packages they need and their per-cluster parameters
                Assert.That(readme, Does.Contain("`Client`").And.Contain("`User`").And.Contain("`None`"));
                Assert.That(readme, Does.Contain("Cloudstrap.Authentication.OpenIdConnect"));
                Assert.That(readme, Does.Contain("TokenRequestParameters"));

                // fail-closed startup and 502 on a token failure
                Assert.That(readme, Does.Contain("## Fail-closed startup rules"));
                Assert.That(readme, Does.Contain("502"));

                // one owner for destination resolution, upgrades, the factory takeover, and D-13
                Assert.That(readme, Does.Contain("Microsoft.Extensions.ServiceDiscovery.Yarp"));
                Assert.That(readme, Does.Contain("WebSocket").And.Contain("Server-Sent Events"));
                Assert.That(readme, Does.Contain("IForwarderHttpClientFactory"));
                Assert.That(readme, Does.Contain("## TLS to the destination"));
                Assert.That(readme, Does.Contain("no custom certificate validator"));

                Assert.That(readme, Does.Not.Contain("skeleton"), "the Step 1 placeholder is still there");
            });
        }
    }
}
