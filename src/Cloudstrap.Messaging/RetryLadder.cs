namespace Cloudstrap.Messaging
{
    using Wolverine;
    using Wolverine.ErrorHandling;

    /// <summary>
    /// The default failure policy: <see cref="RetryOptions.NumberOfImmediate"/> in-process retries, then
    /// <see cref="RetryOptions.NumberOfDelayed"/> scheduled retries with a doubling cooldown starting at
    /// <see cref="FirstDelay"/>, then the message is dead-lettered. Registered as the <em>last</em> global
    /// failure rule, so exception-specific rules the consumer adds match first.
    /// </summary>
    internal static class RetryLadder
    {
        /// <summary>The cooldown before the first scheduled retry; each following cooldown doubles.</summary>
        internal static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Appends the ladder to the engine's global failure rules.
        /// </summary>
        /// <param name="options">The engine options.</param>
        /// <param name="retries">The configured retry counts.</param>
        public static void Apply(WolverineOptions options, RetryOptions retries)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(retries);

            IFailureActions stage = options.Policies.OnException<Exception>();

            if (retries.NumberOfImmediate > 0)
            {
                stage = stage.RetryTimes(retries.NumberOfImmediate).Then;
            }

            if (retries.NumberOfDelayed > 0)
            {
                stage = stage.ScheduleRetry(Cooldowns(retries.NumberOfDelayed)).Then;
            }

            _ = stage.MoveToErrorQueue();
        }

        /// <summary>
        /// Computes the scheduled-retry cooldowns: <see cref="FirstDelay"/>, doubling each time.
        /// </summary>
        /// <param name="count">How many cooldowns to produce.</param>
        /// <returns>The cooldowns, in order.</returns>
        internal static TimeSpan[] Cooldowns(int count)
        {
            TimeSpan[] delays = new TimeSpan[count];
            TimeSpan delay = FirstDelay;
            for (int i = 0; i < count; i++)
            {
                delays[i] = delay;
                delay += delay;
            }

            return delays;
        }
    }
}
