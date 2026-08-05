namespace Cloudstrap.Observability.Correlation
{
    using Microsoft.AspNetCore.Http;

    /// <summary>
    /// Reads the correlation identifier the Cloudstrap correlation middleware established for a request.
    /// </summary>
    public static class HttpContextExtensions
    {
        /// <summary>
        /// The <see cref="HttpContext.Items"/> key the correlation middleware stores the identifier under.
        /// </summary>
        internal const string CorrelationIdItemKey = "Cloudstrap.Correlation.Id";

        /// <summary>
        /// Gets the correlation identifier established for this request.
        /// </summary>
        /// <param name="context">The request to read the identifier from.</param>
        /// <returns>
        /// The correlation identifier — the inbound header value when the caller sent one, the generated
        /// identifier otherwise — or <see langword="null"/> when the correlation middleware has not run for
        /// this request yet.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para>
        /// Prefer <see cref="ICorrelationContextAccessor"/> in ordinary application code: it works in message
        /// handlers and background work as well, where there is no <see cref="HttpContext"/> at all.
        /// </para>
        /// <para>
        /// This method exists for the one case the accessor cannot serve: code holding the
        /// <see cref="HttpContext"/> that runs <em>outside</em> the correlation middleware's scope, such as
        /// an exception handler placed ahead of it in the pipeline. The accessor is async-local, and
        /// async-local values do not flow back up a call chain once an exception has unwound past the frame
        /// that set them; <see cref="HttpContext.Items"/> is request-scoped and does.
        /// </para>
        /// </remarks>
        public static string? GetCloudstrapCorrelationId(this HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            return context.Items.TryGetValue(CorrelationIdItemKey, out object? value)
                ? value as string
                : null;
        }
    }
}
