using NUnit.Framework;

namespace Cloudstrap.Scaffolding.Tests
{
    [TestFixture]
    public class ScaffoldingSanityTests
    {
        [Test]
        public void RuntimeVersion_OnPinnedSdk_IsNet10()
        {
            // Arrange
            int expectedMajor = 10;

            // Act
            int actualMajor = Environment.Version.Major;

            // Assert
            Assert.That(actualMajor, Is.EqualTo(expectedMajor));
        }
    }
}
