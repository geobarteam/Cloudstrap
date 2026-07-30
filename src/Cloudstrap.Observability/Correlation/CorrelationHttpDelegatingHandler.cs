namespace Cloudstrap.Observability.Correlation
{
    using Cloudstrap.Core;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Propagates the ambient correlation identifier to outbound HTTP requests in the configured header —
    /// the same header, and therefore the same value, the inbound middleware honors. Set-if-absent: a header
    /// already on the request (a retry, a resilience re-send, or an explicit caller value) survives
    /// untouched. Register per client through <see cref="HttpClientBuilderExtensions.AddCloudstrapCorrelationHandler"/>.
    /// </summary>
    public sealed class CorrelationHttpDelegatingHandler : DelegatingHandler
    {
        private readonly ICorrelationContextAccessor _accessor;
        private readonly IOptions<CorrelationOptions> _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="CorrelationHttpDelegatingHandler"/> class.
        /// </summary>
        /// <param name="accessor">The ambient correlation accessor the identifier is read from.</param>
        /// <param name="options">The correlation settings carrying the header name.</param>
        /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
        public CorrelationHttpDelegatingHandler(
            ICorrelationContextAccessor accessor,
            IOptions<CorrelationOptions> options)
        {
            ArgumentNullException.ThrowIfNull(accessor);
            ArgumentNullException.ThrowIfNull(options);

            _accessor = accessor;
            _options = options;
        }

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            AddCorrelationHeaderIfAbsent(request);

            return base.SendAsync(request, cancellationToken);
        }

        /// <inheritdoc/>
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            AddCorrelationHeaderIfAbsent(request);

            return base.Send(request, cancellationToken);
        }

        private void AddCorrelationHeaderIfAbsent(HttpRequestMessage request)
        {
            string? correlationId = _accessor.CorrelationId;

            if (string.IsNullOrWhiteSpace(correlationId))
            {
                return;
            }

            string headerName = _options.Value.HeaderName;

            if (!request.Headers.Contains(headerName))
            {
                request.Headers.TryAddWithoutValidation(headerName, correlationId);
            }
        }
    }
}
