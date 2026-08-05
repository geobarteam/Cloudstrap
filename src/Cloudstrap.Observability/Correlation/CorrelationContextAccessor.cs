namespace Cloudstrap.Observability.Correlation
{
    /// <summary>
    /// The default <see cref="ICorrelationContextAccessor"/>: an instance-scoped async-local value,
    /// last-write-wins, never throwing.
    /// </summary>
    internal sealed class CorrelationContextAccessor : ICorrelationContextAccessor
    {
        private readonly AsyncLocal<string?> _correlationId = new();

        public string? CorrelationId
        {
            get => _correlationId.Value;
            set => _correlationId.Value = value;
        }
    }
}
