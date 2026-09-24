namespace Cloudstrap.Messaging.AzureBlob.Tests
{
    using System.Reflection;
    using System.Text.Json;
    using Cloudstrap.Messaging.AzureBlob.Tests.Fixtures;
    using Cloudstrap.Messaging.AzureBlob.Tests.Fixtures.Contracts;
    using Cloudstrap.Messaging.AzureBlob.Tests.Infrastructure;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;
    using OpenTelemetry.Metrics;
    using OpenTelemetry.Trace;

    /// <summary>
    /// The claim check across a real transport hop (AC-M4, AC-CK1–AC-CK3, AC-CK5, AC-CK6) on a real SQL Server
    /// (spec D-3: LocalDB by default, <c>CLOUDSTRAP_TEST_SQL</c> overrides) with a no-network in-memory blob
    /// account: a large body leaves the message as one blob plus Wolverine's reference header and comes back
    /// whole; a small one travels untouched; retries re-read the payload with one side effect and the blob
    /// retained; correlation and telemetry flow exactly as before; contracts and handlers reference no leaf,
    /// claim-check or Azure type.
    /// </summary>
    [TestFixture]
    public sealed class ClaimCheckRoundTripTests
    {
        private const long _defaultThreshold = 204_800;
        private const string _claimCheckHeaderPrefix = "claim-check.";
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(45);

        [OneTimeSetUp]
        public Task ResetDatabase()
        {
            return SqlServerTestDatabase.ResetAsync();
        }

        [Test]
        public async Task AboveThreshold_ExactlyOneBlobIsWritten_TheEnvelopeCarriesTheBodyReference_AndTheHandlerSeesTheWholeMessage()
        {
            // Arrange — the default threshold; a 300 000-character body.
            InMemoryBlobContainerClient blobs = new();
            await using TwoNodeHarness harness = await TwoNodeHarness.StartAsync(blobs);
            ExportReadyCommand command = new(Guid.NewGuid(), new string('x', 300_000));

            // Act
            await harness.SendAsync(command);
            ExportObservation observed = (ExportObservation)await harness.ListenerRecorder.WaitForNextAsync(_timeout);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(blobs.Uploads, Is.EqualTo(1), "exactly one blob");
                Assert.That(blobs.Payloads.Single(), Has.Length.GreaterThan(_defaultThreshold), "the whole body was stored");
                Assert.That(observed.Message, Is.EqualTo(command), "the handler sees the whole message");
                Assert.That(observed.Headers.Keys, Has.Some.StartsWith(_claimCheckHeaderPrefix), "the reference travelled on the envelope");
                Assert.That(harness.Sender.Services.GetRequiredService<InvocationRecorder>().Received, Is.Empty, "the sender never handled it");
            });
        }

        [Test]
        public async Task BelowThreshold_NoBlobIsWritten_AndNoReferenceHeaderTravels()
        {
            // Arrange — a 4 KiB body under the default threshold.
            InMemoryBlobContainerClient blobs = new();
            await using TwoNodeHarness harness = await TwoNodeHarness.StartAsync(blobs);
            ExportReadyCommand command = new(Guid.NewGuid(), new string('y', 4096));

            // Act
            await harness.SendAsync(command);
            ExportObservation observed = (ExportObservation)await harness.ListenerRecorder.WaitForNextAsync(_timeout);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(blobs.Uploads, Is.Zero);
                Assert.That(observed.Headers.Keys, Has.None.StartsWith(_claimCheckHeaderPrefix));
                Assert.That(observed.Message, Is.EqualTo(command));
            });
        }

        [Test]
        public async Task AtThreshold_ExactlyTheSerializedSize_NoBlobIsWritten()
        {
            // Arrange — the threshold set to the body's exact serialized size: "strictly larger" offloads, equal does not.
            SmallCommand command = new(Guid.NewGuid());
            long size = JsonSerializer.SerializeToUtf8Bytes(command).LongLength;
            InMemoryBlobContainerClient blobs = new();
            await using TwoNodeHarness harness = await TwoNodeHarness.StartAsync(blobs, offloadThresholdBytes: size);

            // Act
            await harness.SendAsync(command);
            ExportObservation observed = (ExportObservation)await harness.ListenerRecorder.WaitForNextAsync(_timeout);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(blobs.Uploads, Is.Zero);
                Assert.That(observed.Headers.Keys, Has.None.StartsWith(_claimCheckHeaderPrefix));
                Assert.That(observed.Message, Is.EqualTo(command));
            });
        }

        [Test]
        public async Task TransientHandlerFailures_RereadThePayloadEachAttempt_OneSideEffect_BlobRetained()
        {
            // Arrange — no immediate retries, so the retry is scheduled through the durable inbox and the
            // envelope is deserialized — and the payload re-read — again; the handler fails once.
            InMemoryBlobContainerClient blobs = new();
            await using TwoNodeHarness harness = await TwoNodeHarness.StartAsync(blobs, listenerSettings: settings =>
            {
                settings["Cloudstrap:Messaging:Retries:NumberOfImmediate"] = "0";
                settings["Cloudstrap:Messaging:Retries:NumberOfDelayed"] = "5";
            });
            FlakyExportCommand command = new(Guid.NewGuid(), new string('z', 300_000), FailuresBeforeSuccess: 1);

            // Act
            await harness.SendAsync(command);
            ExportObservation observed = (ExportObservation)await harness.ListenerRecorder.WaitForNextAsync(_timeout);
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(harness.ListenerAttempts.AttemptsFor(command.Id), Is.EqualTo(2), "first attempt plus one scheduled retry");
                Assert.That(harness.ListenerRecorder.Received, Is.Empty, "exactly one side effect");
                Assert.That(observed.Message, Is.EqualTo(command));
                Assert.That(blobs.Downloads, Is.GreaterThanOrEqualTo(2), "the payload was re-read for the retry");
                Assert.That(blobs.Count, Is.EqualTo(1), "the blob is retained after success");
            });
        }

        [Test]
        public async Task Correlation_FlowsUnchangedForOffloadedMessages_AndWolverineSpansReachTheHostsPipeline()
        {
            // Arrange
            InMemoryBlobContainerClient blobs = new();
            await using TwoNodeHarness harness = await TwoNodeHarness.StartAsync(blobs);
            string correlationId = $"corr-{Guid.NewGuid():N}";
            ExportReadyCommand command = new(Guid.NewGuid(), new string('c', 300_000));

            // Act
            harness.Sender.Services.GetRequiredService<ICorrelationContextAccessor>().CorrelationId = correlationId;
            await harness.SendAsync(command);
            ExportObservation observed = (ExportObservation)await harness.ListenerRecorder.WaitForNextAsync(_timeout);
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(blobs.Uploads, Is.EqualTo(1));
                Assert.That(observed.CorrelationId, Is.EqualTo(correlationId));
                Assert.That(harness.ListenerActivities.Select(activity => activity.Source.Name), Does.Contain("Wolverine"));
            });
        }

        [Test]
        public void Leaf_RegistersNoExporterAndNoProvider()
        {
            // Arrange
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());

            // Act
            builder.AddCloudstrapMessaging().UseAzureBlobClaimCheck(claimCheck => claimCheck.ContainerClient = new InMemoryBlobContainerClient());

            // Assert — the tripwire: no exporter, no TracerProvider, no MeterProvider registered by the packages.
            string[] descriptors = [.. builder.Services.Select(descriptor =>
                $"{descriptor.ServiceType.FullName}|{descriptor.ImplementationType?.FullName}|{descriptor.ImplementationInstance?.GetType().FullName}")];
            Assert.Multiple(() =>
            {
                Assert.That(descriptors, Has.None.Contains("Exporter"));
                Assert.That(builder.Services.Any(d => d.ServiceType == typeof(TracerProvider)), Is.False);
                Assert.That(builder.Services.Any(d => d.ServiceType == typeof(MeterProvider)), Is.False);
            });
        }

        [Test]
        public void ContractsAndHandlers_ReferenceNoLeafClaimCheckOrAzureTypes()
        {
            // Arrange — the contracts and the handlers; handlers may take Wolverine's Envelope and the suite's accessor.
            Type[] subjects =
            [
                typeof(ExportReadyCommand), typeof(SmallCommand), typeof(FlakyExportCommand),
                typeof(ExportReadyCommandHandler), typeof(SmallCommandHandler), typeof(FlakyExportCommandHandler),
            ];

            // Act
            string[] offenders =
            [
                .. subjects.SelectMany(type => ReferencedTypes(type)
                    .Where(IsForbidden)
                    .Select(referenced => $"{type.Name} -> {referenced.FullName}")),
            ];

            // Assert
            Assert.That(offenders, Is.Empty, $"Contracts/handlers reference forbidden types: {string.Join(", ", offenders)}");
        }

        private static IEnumerable<Type> ReferencedTypes(Type type)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            IEnumerable<Type> attributes = type.GetCustomAttributes(inherit: false).Select(attribute => attribute.GetType());
            IEnumerable<Type> bases = type.BaseType is null ? [] : [type.BaseType];
            IEnumerable<Type> fields = type.GetFields(all).Select(field => field.FieldType);
            IEnumerable<Type> properties = type.GetProperties(all).Select(property => property.PropertyType);
            IEnumerable<Type> methods = type.GetMethods(all).SelectMany(method =>
                method.GetParameters().Select(parameter => parameter.ParameterType)
                    .Concat(method.GetCustomAttributes(inherit: false).Select(attribute => attribute.GetType()))
                    .Concat(method.GetParameters().SelectMany(parameter => parameter.GetCustomAttributes(inherit: false).Select(attribute => attribute.GetType())))
                    .Append(method.ReturnType));
            return attributes.Concat(bases).Concat(type.GetInterfaces()).Concat(fields).Concat(properties).Concat(methods);
        }

        private static bool IsForbidden(Type type)
        {
            string assembly = type.Assembly.GetName().Name ?? string.Empty;
            string ns = type.Namespace ?? string.Empty;
            return string.Equals(assembly, "Cloudstrap.Messaging.AzureBlob", StringComparison.Ordinal)
                || assembly.StartsWith("Azure.", StringComparison.Ordinal)
                || ns.StartsWith("Wolverine.Persistence", StringComparison.Ordinal);
        }
    }
}
