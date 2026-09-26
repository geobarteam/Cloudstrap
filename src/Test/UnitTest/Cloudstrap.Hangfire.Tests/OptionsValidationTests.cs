namespace Cloudstrap.Hangfire.Tests
{
    using Cloudstrap.Core;
    using Cloudstrap.Hangfire.Tests.Infrastructure;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;

    /// <summary>
    /// The <c>Cloudstrap:Hangfire</c> section (spec Public API Sketch): its defaults, and the validation rules that
    /// fail at the registration call naming the exact key, never echoing a value.
    /// </summary>
    [TestFixture]
    public sealed class OptionsValidationTests
    {
        [Test]
        public void Options_Defaults_MatchTheSketch()
        {
            // Arrange & Act
            HangfireOptions options = new();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(HangfireOptions.SectionName, Is.EqualTo("Cloudstrap:Hangfire"));
                Assert.That(options.Storage.ConnectionStringName, Is.EqualTo("DefaultConnection"));
                Assert.That(options.Storage.SchemaName, Is.Null);
                Assert.That(options.Storage.PrepareSchema, Is.Null);
                Assert.That(options.Server.WorkerCount, Is.Null);
                Assert.That(options.Server.Queues, Is.Null);
                Assert.That(options.Dashboard.Path, Is.EqualTo("/hangfire"));
                Assert.That(options.Dashboard.RequiredRole, Is.Null);
                Assert.That(options.Dashboard.AuthorizationPolicy, Is.Null);
                Assert.That(options.Dashboard.ReadOnly, Is.False);
                Assert.That(options.Jobs, Is.Empty);
            });
        }

        [Test]
        public void Options_WorkerCountZero_FailsNamingTheKey()
        {
            // Act
            ConfigurationValidationException failure = Register(("Cloudstrap:Hangfire:Server:WorkerCount", "0"));

            // Assert
            Assert.That(failure.Failures, Has.Some.Contains("Cloudstrap:Hangfire:Server:WorkerCount"));
        }

        [Test]
        public void Options_EmptyQueueEntry_FailsNamingTheKey()
        {
            // Act
            ConfigurationValidationException failure = Register(
                ("Cloudstrap:Hangfire:Server:Queues:0", "critical"),
                ("Cloudstrap:Hangfire:Server:Queues:1", " "));

            // Assert
            Assert.That(failure.Failures, Has.Some.Contains("'Cloudstrap:Hangfire:Server:Queues'"));
        }

        [TestCase("hangfire")]
        [TestCase("/hangfire/")]
        public void Options_DashboardPathNotRootedOrTrailingSlash_FailsNamingTheKey(string path)
        {
            // Act
            ConfigurationValidationException failure = Register(("Cloudstrap:Hangfire:Dashboard:Path", path));

            // Assert
            Assert.That(failure.Failures, Has.Some.Contains("'Cloudstrap:Hangfire:Dashboard:Path'"));
        }

        [Test]
        public void Options_EmptyConnectionStringNameOrSchemaName_FailNamingTheKeys()
        {
            // Act
            ConfigurationValidationException failure = Register(
                ("Cloudstrap:Hangfire:Storage:ConnectionStringName", " "),
                ("Cloudstrap:Hangfire:Storage:SchemaName", " "));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(failure.Failures, Has.Some.Contains("'Cloudstrap:Hangfire:Storage:ConnectionStringName'"));
                Assert.That(failure.Failures, Has.Some.Contains("'Cloudstrap:Hangfire:Storage:SchemaName'"));
            });
        }

        [Test]
        public void Options_JobsLookup_IsCaseInsensitive()
        {
            // Arrange
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder(With(("Cloudstrap:Hangfire:Jobs:nightlycleanuptask:Cron", "0 4 * * *")));

            // Act
            builder.AddCloudstrapHangfire();
            HangfireOptions options = ((HangfireRegistrationState)builder.Services
                .Single(descriptor => descriptor.ServiceType == typeof(HangfireRegistrationState))
                .ImplementationInstance!).Options;

            // Assert
            Assert.That(options.Jobs["NightlyCleanupTask"].Cron, Is.EqualTo("0 4 * * *"));
        }

        [Test]
        public void Options_Failures_NeverEchoTheValue()
        {
            // Act
            ConfigurationValidationException failure = Register(
                ("Cloudstrap:Hangfire:Dashboard:Path", "secret-looking-path/"),
                ("Cloudstrap:Hangfire:Server:WorkerCount", "-42"));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(failure.Message, Does.Not.Contain("secret-looking-path"));
                Assert.That(failure.Failures, Has.None.Contains("secret-looking-path"));
                Assert.That(failure.Failures, Has.None.Contains("-42"));
            });
        }

        private static ConfigurationValidationException Register(params (string Key, string Value)[] overrides)
        {
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder(With(overrides));
            return Assert.Throws<ConfigurationValidationException>(() => builder.AddCloudstrapHangfire())!;
        }

        private static Dictionary<string, string?> With(params (string Key, string Value)[] overrides)
        {
            Dictionary<string, string?> settings = HangfireTestHost.ValidSettings();
            foreach ((string key, string value) in overrides)
            {
                settings[key] = value;
            }

            return settings;
        }
    }
}
