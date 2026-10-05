namespace Cloudstrap.TestInfrastructure.Tests
{
    using NUnit.Framework;

    /// <summary>
    /// #8 D-6: the hoisted message recorder lets a test await a message's arrival at a fixture handler without
    /// polling, times out loudly, honours cancellation, and keeps arrival order.
    /// </summary>
    [TestFixture]
    public sealed class InvocationRecorderTests
    {
        private static readonly TimeSpan _short = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan _generous = TimeSpan.FromSeconds(5);
        private static readonly string[] _arrivalOrder = ["first", "second", "third"];

        [Test]
        public async Task WaitForNextAsync_AfterRecord_ReturnsThatMessage()
        {
            // Arrange
            using InvocationRecorder recorder = new();
            recorder.Record("ping");

            // Act
            object message = await recorder.WaitForNextAsync(_generous);

            // Assert
            Assert.That(message, Is.EqualTo("ping"));
        }

        [Test]
        public void WaitForNextAsync_NothingRecorded_ThrowsTimeoutException()
        {
            // Arrange
            using InvocationRecorder recorder = new();

            // Act & Assert
            Assert.ThrowsAsync<TimeoutException>(() => recorder.WaitForNextAsync(_short));
        }

        [Test]
        public void WaitForNextAsync_TokenCancelled_ThrowsOperationCanceledException()
        {
            // Arrange
            using InvocationRecorder recorder = new();
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            // Act & Assert
            Assert.CatchAsync<OperationCanceledException>(() => recorder.WaitForNextAsync(_generous, cancellation.Token));
        }

        [Test]
        public void Received_ListsRecordedMessagesInArrivalOrder()
        {
            // Arrange
            using InvocationRecorder recorder = new();

            // Act
            recorder.Record("first");
            recorder.Record("second");
            recorder.Record("third");

            // Assert
            Assert.That(recorder.Received, Is.EqualTo(_arrivalOrder));
        }

        [Test]
        public void Record_NullMessage_ThrowsArgumentNullException()
        {
            // Arrange
            using InvocationRecorder recorder = new();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => recorder.Record(null!));
        }
    }
}
