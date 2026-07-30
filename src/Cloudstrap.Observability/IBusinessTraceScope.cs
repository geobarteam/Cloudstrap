namespace Cloudstrap.Observability
{
    /// <summary>
    /// An open business span; disposing it ends the span.
    /// </summary>
    public interface IBusinessTraceScope : IDisposable
    {
        /// <summary>
        /// Gets a value indicating whether the underlying span is recorded by the telemetry pipeline. When
        /// <see langword="false"/> — telemetry disabled or sampled out — every member is a safe no-op.
        /// </summary>
        /// <value><see langword="true"/> when the span is recorded.</value>
        bool IsRecording
        {
            get;
        }

        /// <summary>
        /// Sets the low-cardinality outcome of the operation (for example <c>succeeded</c> or
        /// <c>rejected-validation</c>).
        /// </summary>
        /// <param name="outcome">The outcome value.</param>
        /// <exception cref="ArgumentException"><paramref name="outcome"/> is <see langword="null"/> or whitespace.</exception>
        void SetOutcome(string outcome);
    }
}
