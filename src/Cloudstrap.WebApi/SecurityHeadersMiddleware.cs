namespace Cloudstrap.WebApi
{
    using Microsoft.AspNetCore.Http;

    /// <summary>
    /// Adds the two response headers that matter for a JSON API: <c>X-Content-Type-Options: nosniff</c> and
    /// <c>Referrer-Policy: no-referrer</c>.
    /// </summary>
    /// <remarks>
    /// Two constant headers do not justify a security-headers dependency. The richer set — content security
    /// policy, frame options, permissions policy — belongs to an HTML surface, where the values are
    /// application-specific and a library is the right home for them.
    /// </remarks>
    /// <param name="next">The next middleware in the pipeline.</param>
    internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
    {
        /// <summary>
        /// Attaches the headers to the response, then runs the rest of the pipeline.
        /// </summary>
        /// <param name="context">The request being handled.</param>
        /// <returns>A task that completes when the pipeline has run.</returns>
        public Task Invoke(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            context.Response.OnStarting(
                static state =>
                {
                    IHeaderDictionary headers = ((HttpContext)state).Response.Headers;

                    // Never overwrite: an application that set its own value meant it.
                    if (!headers.ContainsKey("X-Content-Type-Options"))
                    {
                        headers["X-Content-Type-Options"] = "nosniff";
                    }

                    if (!headers.ContainsKey("Referrer-Policy"))
                    {
                        headers["Referrer-Policy"] = "no-referrer";
                    }

                    return Task.CompletedTask;
                },
                context);

            return next(context);
        }
    }
}
