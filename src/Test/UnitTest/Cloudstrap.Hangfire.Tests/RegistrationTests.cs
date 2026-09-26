namespace Cloudstrap.Hangfire.Tests
{
    using System.Diagnostics;
    using Cloudstrap.Hangfire.Tests.Fakes;
    using Cloudstrap.Hangfire.Tests.Infrastructure;
    using global::Hangfire;
    using global::Hangfire.SqlServer;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;
    using OpenTelemetry;
    using OpenTelemetry.Metrics;
    using OpenTelemetry.Trace;

    /// <summary>
    /// One-call registration (AC-HF1, AC-HF9 descriptors, AC-HF17 effective value, AC-HF19): the storage, the
    /// dispatcher and the scheduler are registered without opening a connection, the host role decides whether
    /// scheduling and processing are wired, and a second call fails fast.
    /// </summary>
    [TestFixture]
    public sealed class RegistrationTests
    {
        private static readonly string[] _configuredQueues = ["critical", "default"];

        [Test]
        public void AddCloudstrapHangfire_OnNullBuilder_ThrowsArgumentNullException()
        {
            // Arrange
            IHostApplicationBuilder builder = null!;

            // Act & Assert
            Assert.That(() => builder.AddCloudstrapHangfire(), Throws.ArgumentNullException);
        }

        [Test]
        public void AddCloudstrapHangfire_CalledTwice_ThrowsNamingTheDuplicateRegistration()
        {
            // Arrange
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder();
            builder.AddCloudstrapHangfire();

            // Act & Assert
            Assert.That(
                () => builder.AddCloudstrapHangfire(),
                Throws.InvalidOperationException.With.Message.Contains(nameof(HostApplicationBuilderExtensions.AddCloudstrapHangfire)));
        }

        [Test]
        public void AddCloudstrapHangfire_UnresolvableConnectionStringName_FailsAtTheCallNamingTheKey_NeverTheValue()
        {
            // Arrange — the default name does not resolve; a different connection string does exist.
            Dictionary<string, string?> settings = HangfireTestHost.ValidSettings();
            settings.Remove("ConnectionStrings:DefaultConnection");
            settings["ConnectionStrings:Other"] = HangfireTestHost.UnreachableConnectionString;
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder(settings);

            // Act
            InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() => builder.AddCloudstrapHangfire());

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(failure!.Message, Does.Contain("'ConnectionStrings:DefaultConnection'"));
                Assert.That(failure.Message, Does.Contain("'Cloudstrap:Hangfire:Storage:ConnectionStringName'"));
                Assert.That(failure.Message, Does.Not.Contain("fixture-password"));
                Assert.That(failure.Message, Does.Not.Contain("unreachable.invalid"));
            });
        }

        [Test]
        public void AddCloudstrapHangfire_ConfiguredConnectionStringName_IsTheOneResolved()
        {
            // Arrange
            Dictionary<string, string?> settings = HangfireTestHost.ValidSettings();
            settings.Remove("ConnectionStrings:DefaultConnection");
            settings["ConnectionStrings:Jobs"] = "Server=unreachable.invalid;Database=jobs_catalog;Integrated Security=true;Connect Timeout=1;";
            settings["Cloudstrap:Hangfire:Storage:ConnectionStringName"] = "Jobs";
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder(settings);

            // Act — the SqlServer hatch turns off the schema probe Hangfire runs when the storage is resolved.
            builder.AddCloudstrapHangfire(hangfire => hangfire.SqlServer = sql => sql.TryAutoDetectSchemaDependentOptions = false);
            using ServiceProvider provider = builder.Services.BuildServiceProvider();
            JobStorage storage = provider.GetRequiredService<JobStorage>();

            // Assert — SqlServerStorage describes itself by server and database.
            Assert.Multiple(() =>
            {
                Assert.That(storage, Is.InstanceOf<SqlServerStorage>());
                Assert.That(storage.ToString(), Does.Contain("jobs_catalog"));
            });
        }

        [Test]
        public void AddCloudstrapHangfire_CustomStorageHatch_SkipsSqlServerAndTheConnectionStringRequirement()
        {
            // Arrange — no connection string at all.
            Dictionary<string, string?> settings = HangfireTestHost.ValidSettings();
            settings.Remove("ConnectionStrings:DefaultConnection");
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder(settings);
            FakeJobStorage fake = new();

            // Act
            builder.AddCloudstrapHangfire(hangfire => hangfire.Storage = configuration => configuration.UseStorage(fake));
            using ServiceProvider provider = builder.Services.BuildServiceProvider();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<JobStorage>(), Is.SameAs(fake));
                Assert.That(RegistrationState(builder).CustomStorage, Is.True);
            });
        }

        [Test]
        public void AddCloudstrapHangfire_Default_RegistersStorageClientManagerRunnerAndScheduler_WithoutOpeningAConnection()
        {
            // Arrange
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder();

            // Act — registration only: the storage is built lazily when the host resolves it, so nothing here
            // touches the unreachable server.
            builder.AddCloudstrapHangfire();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(builder.Services.Any(d => d.ServiceType == typeof(JobStorage)), Is.True);
                Assert.That(builder.Services.Any(d => d.ServiceType == typeof(IBackgroundJobClient)), Is.True);
                Assert.That(builder.Services.Any(d => d.ServiceType == typeof(IRecurringJobManager)), Is.True);
                Assert.That(Lifetime(builder, typeof(RecurringTaskRunner)), Is.EqualTo(ServiceLifetime.Scoped));
                Assert.That(Lifetime(builder, typeof(RecurringJobsScheduler)), Is.EqualTo(ServiceLifetime.Scoped));
            });
        }

        [Test]
        public void AddCloudstrapHangfire_RunServerTrue_RegistersTheSchedulingServiceBeforeTheHangfireServer()
        {
            // Arrange
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder();
            int before = HostedServices(builder).Count;

            // Act
            builder.AddCloudstrapHangfire();

            // Assert — hosted services start in registration order: scheduling completes before the server dequeues.
            List<ServiceDescriptor> added = [.. HostedServices(builder).Skip(before)];
            Assert.Multiple(() =>
            {
                Assert.That(added, Has.Count.EqualTo(2));
                Assert.That(added[0].ImplementationType, Is.EqualTo(typeof(RecurringTaskSchedulingService)));
                Assert.That(added[1].ImplementationType, Is.Not.EqualTo(typeof(RecurringTaskSchedulingService)));
            });
        }

        [Test]
        public void AddCloudstrapHangfire_RunServerFalse_RegistersNoHostedServiceFromThisPackage()
        {
            // Arrange
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder();
            ServiceDescriptor[] before = [.. HostedServices(builder)];

            // Act
            builder.AddCloudstrapHangfire(hangfire => hangfire.RunServer = false);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(HostedServices(builder), Is.EquivalentTo(before));
                Assert.That(RegistrationState(builder).RunServer, Is.False);
            });
        }

        [Test]
        public void StorageOptions_AreHangfiresRecommendedSqlServerSettings()
        {
            // Arrange
            HangfireStorageOptions configured = new();
            string defaultSchema = new SqlServerStorageOptions().SchemaName;

            // Act
            SqlServerStorageOptions options = SqlServerStorageOptionsFactory.Create(configured, prepareSchema: false, hatch: null);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(options.CommandBatchMaxTimeout, Is.EqualTo(TimeSpan.FromMinutes(5)));
                Assert.That(options.SlidingInvisibilityTimeout, Is.EqualTo(TimeSpan.FromMinutes(5)));
                Assert.That(options.QueuePollInterval, Is.EqualTo(TimeSpan.Zero));
                Assert.That(options.UseRecommendedIsolationLevel, Is.True);
                Assert.That(options.DisableGlobalLocks, Is.True);
                Assert.That(options.PrepareSchemaIfNecessary, Is.False);
                Assert.That(options.SchemaName, Is.EqualTo(defaultSchema));
                Assert.That(options.SqlClientFactory, Is.SameAs(Microsoft.Data.SqlClient.SqlClientFactory.Instance));
            });
        }

        [Test]
        public void StorageOptions_SchemaNameOverride_AndTheSqlServerHatch_RunLast()
        {
            // Arrange
            HangfireStorageOptions configured = new() { SchemaName = "jobs" };
            string? schemaSeenByHatch = null;

            // Act
            SqlServerStorageOptions options = SqlServerStorageOptionsFactory.Create(
                configured,
                prepareSchema: true,
                hatch: sql =>
                {
                    schemaSeenByHatch = sql.SchemaName;
                    sql.QueuePollInterval = TimeSpan.FromSeconds(15);
                });

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(schemaSeenByHatch, Is.EqualTo("jobs"));
                Assert.That(options.SchemaName, Is.EqualTo("jobs"));
                Assert.That(options.PrepareSchemaIfNecessary, Is.True);
                Assert.That(options.QueuePollInterval, Is.EqualTo(TimeSpan.FromSeconds(15)));
            });
        }

        [Test]
        public void PrepareSchema_UnsetInDevelopment_IsTrue()
        {
            // Arrange
            Dictionary<string, string?> settings = HangfireTestHost.ValidSettings();
            settings.Remove("Cloudstrap:Hangfire:Storage:PrepareSchema");
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder(settings, Environments.Development);

            // Act
            builder.AddCloudstrapHangfire();

            // Assert
            Assert.That(RegistrationState(builder).PrepareSchema, Is.True);
        }

        [Test]
        public void PrepareSchema_UnsetInProduction_IsFalse()
        {
            // Arrange
            Dictionary<string, string?> settings = HangfireTestHost.ValidSettings();
            settings.Remove("Cloudstrap:Hangfire:Storage:PrepareSchema");
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder(settings, Environments.Production);

            // Act
            builder.AddCloudstrapHangfire();

            // Assert
            Assert.That(RegistrationState(builder).PrepareSchema, Is.False);
        }

        [TestCase("Development", "false", false)]
        [TestCase("Production", "true", true)]
        public void PrepareSchema_ExplicitValue_WinsInEitherEnvironment(string environment, string configured, bool expected)
        {
            // Arrange
            Dictionary<string, string?> settings = HangfireTestHost.ValidSettings();
            settings["Cloudstrap:Hangfire:Storage:PrepareSchema"] = configured;
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder(settings, environment);

            // Act
            builder.AddCloudstrapHangfire();

            // Assert
            Assert.That(RegistrationState(builder).PrepareSchema, Is.EqualTo(expected));
        }

        [Test]
        public void ServerOptions_WorkerCountAndQueues_AppliedWhenSet_ServerHatchRunsLast()
        {
            // Arrange
            HangfireServerOptions configured = new() { WorkerCount = 3, Queues = ["critical", "default"] };
            BackgroundJobServerOptions untouched = new();
            BackgroundJobServerOptions server = new();
            int? workerCountSeenByHatch = null;

            // Act
            ServerOptionsComposer.Apply(untouched, new HangfireServerOptions(), hatch: null);
            ServerOptionsComposer.Apply(server, configured, hatch: options =>
            {
                workerCountSeenByHatch = options.WorkerCount;
                options.ServerName = "fixture-server";
            });

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(untouched.WorkerCount, Is.EqualTo(new BackgroundJobServerOptions().WorkerCount));
                Assert.That(untouched.Queues, Is.EqualTo(new BackgroundJobServerOptions().Queues));
                Assert.That(server.WorkerCount, Is.EqualTo(3));
                Assert.That(server.Queues, Is.EqualTo(_configuredQueues));
                Assert.That(workerCountSeenByHatch, Is.EqualTo(3));
                Assert.That(server.ServerName, Is.EqualTo("fixture-server"));
            });
        }

        [Test]
        public void AddCloudstrapHangfire_ContributesTheSpanSource_RegistersNoTracerProviderAndNoExporter()
        {
            // Arrange
            HostApplicationBuilder bare = HangfireTestHost.CreateBuilder();
            HostApplicationBuilder owned = HangfireTestHost.CreateBuilder();
            List<Activity> exported = [];
            owned.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddInMemoryExporter(exported));

            // Act
            bare.AddCloudstrapHangfire();
            owned.AddCloudstrapHangfire();
            using ServiceProvider provider = owned.Services.BuildServiceProvider();
            _ = provider.GetRequiredService<TracerProvider>();
            using ActivitySource source = new(CloudstrapHangfireActivitySources.RecurringTask);
            using (Activity? activity = source.StartActivity("probe"))
            {
                Assert.That(activity, Is.Not.Null, "The host's pipeline does not listen to the contributed source.");
            }

            // Assert — the tripwire on the pipeline-less host: nothing of the package's own.
            string[] descriptors = [.. bare.Services.Select(descriptor =>
                $"{descriptor.ServiceType.FullName}|{descriptor.ImplementationType?.FullName}|{descriptor.ImplementationInstance?.GetType().FullName}")];
            Assert.Multiple(() =>
            {
                Assert.That(CloudstrapHangfireActivitySources.RecurringTask, Is.EqualTo("Cloudstrap.Hangfire"));
                Assert.That(exported.Select(activity => activity.Source.Name), Does.Contain("Cloudstrap.Hangfire"));
                Assert.That(descriptors, Has.None.Contains("Exporter"));
                Assert.That(bare.Services.Any(d => d.ServiceType == typeof(TracerProvider)), Is.False);
                Assert.That(bare.Services.Any(d => d.ServiceType == typeof(MeterProvider)), Is.False);
            });
        }

        private static HangfireRegistrationState RegistrationState(HostApplicationBuilder builder)
        {
            return (HangfireRegistrationState)builder.Services
                .Single(descriptor => descriptor.ServiceType == typeof(HangfireRegistrationState))
                .ImplementationInstance!;
        }

        private static ServiceLifetime? Lifetime(HostApplicationBuilder builder, Type serviceType)
        {
            return builder.Services.SingleOrDefault(descriptor => descriptor.ServiceType == serviceType)?.Lifetime;
        }

        private static List<ServiceDescriptor> HostedServices(HostApplicationBuilder builder)
        {
            return [.. builder.Services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService))];
        }
    }
}
