namespace Cloudstrap.Core.Tests
{
    using NUnit.Framework;

    [TestFixture]
    public sealed class ConfigurationValidationExceptionTests
    {
        [Test]
        public void Ctor_WithMessageAndFailures_ExposesFailuresList()
        {
            // Arrange
            string[] failures = ["Application:SystemName is required.", "OpenTelemetry:Endpoint is required."];

            // Act
            var exception = new ConfigurationValidationException("Invalid.", failures);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(exception.Failures, Is.EqualTo(failures));

                // AC-C3: the message itself lists every validation failure.
                Assert.That(exception.Message, Does.StartWith("Invalid."));
                Assert.That(exception.Message, Does.Contain("Application:SystemName is required."));
                Assert.That(exception.Message, Does.Contain("OpenTelemetry:Endpoint is required."));
            });
        }

        [Test]
        public void Ctor_WithMessageOnly_HasEmptyFailures()
        {
            // Arrange & Act
            var exception = new ConfigurationValidationException("Invalid.");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(exception.Message, Is.EqualTo("Invalid."));
                Assert.That(exception.Failures, Is.Empty);
            });
        }

        [Test]
        public void Ctor_WithInnerException_PreservesInner()
        {
            // Arrange
            var inner = new InvalidOperationException("boom");

            // Act
            var exception = new ConfigurationValidationException("Invalid.", inner);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(exception.InnerException, Is.SameAs(inner));
                Assert.That(exception.Failures, Is.Empty);
            });
        }
    }
}
