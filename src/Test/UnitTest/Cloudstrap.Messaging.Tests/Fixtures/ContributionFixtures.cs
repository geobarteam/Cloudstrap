namespace Cloudstrap.Messaging.Tests.Fixtures
{
    /// <summary>
    /// A fixture command whose handler always fails with the test-local <see cref="DeterministicFailureException"/>,
    /// so an engine contribution's exception-specific failure rule can be told apart from the retry ladder.
    /// </summary>
    public sealed record DeterministicFailureCommand(string Id);

    /// <summary>The deterministic failure an engine contribution dead-letters without retries.</summary>
    public sealed class DeterministicFailureException : Exception
    {
        /// <summary>Initializes a new instance of the <see cref="DeterministicFailureException"/> class.</summary>
        public DeterministicFailureException()
        {
        }

        /// <summary>Initializes a new instance of the <see cref="DeterministicFailureException"/> class.</summary>
        /// <param name="message">The message.</param>
        public DeterministicFailureException(string message)
            : base(message)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="DeterministicFailureException"/> class.</summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The inner exception.</param>
        public DeterministicFailureException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>The always-failing fixture handler; every attempt is counted.</summary>
    public static class DeterministicFailureCommandHandler
    {
        /// <summary>Counts the attempt, then fails deterministically.</summary>
        public static void Handle(DeterministicFailureCommand command, AttemptCounter attempts)
        {
            int attempt = attempts.Increment(command.Id);
            throw new DeterministicFailureException($"Attempt {attempt} of {command.Id} fails deterministically.");
        }
    }

    /// <summary>
    /// A marker singleton a test registers on the host so an engine contribution can prove it received the
    /// host's own service provider (the leaf resolves its blob client this way).
    /// </summary>
    public sealed class ContributionProbe
    {
        /// <summary>Gets the ordered record of who ran: contributions and the consumer's delegate.</summary>
        public List<string> CallOrder { get; } = [];

        /// <summary>Gets or sets the probe instance a contribution resolved from the provider it was given.</summary>
        public ContributionProbe? ResolvedByContribution
        {
            get; set;
        }
    }
}
