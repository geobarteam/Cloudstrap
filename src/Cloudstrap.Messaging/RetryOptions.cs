namespace Cloudstrap.Messaging
{
    using System.ComponentModel.DataAnnotations;

    /// <summary>
    /// The retry ladder applied to a failing handler, bound from <c>Cloudstrap:Messaging:Retries</c>: first
    /// <see cref="NumberOfImmediate"/> in-process retries, then <see cref="NumberOfDelayed"/> scheduled
    /// retries with a doubling cooldown (5 s, 10 s, 20 s, …), then the message is dead-lettered.
    /// </summary>
    /// <remarks>
    /// The ladder is the engine's <em>last</em> global failure rule: exception-specific rules added through
    /// the <c>Wolverine</c> configurator delegate (<c>options.Policies.OnException&lt;T&gt;()</c>) match first
    /// and replace it for the exceptions they name.
    /// </remarks>
    public sealed class RetryOptions
    {
        /// <summary>
        /// Gets or sets how many times a failing message is retried immediately, in process.
        /// </summary>
        /// <value>The immediate retry count. Defaults to 5.</value>
        [Range(0, int.MaxValue)]
        public int NumberOfImmediate { get; set; } = 5;

        /// <summary>
        /// Gets or sets how many times a still-failing message is rescheduled with an increasing cooldown
        /// after the immediate retries are exhausted.
        /// </summary>
        /// <value>The delayed retry count. Defaults to 5.</value>
        [Range(0, int.MaxValue)]
        public int NumberOfDelayed { get; set; } = 5;
    }
}
