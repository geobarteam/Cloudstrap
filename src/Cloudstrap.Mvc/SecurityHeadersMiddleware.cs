namespace Cloudstrap.Mvc
{
    using Microsoft.AspNetCore.Http;

    /// <summary>
    /// Adds the two constant response headers: <c>X-Content-Type-Options: nosniff</c> and
    /// <c>Referrer-Policy: no-referrer</c>.
    /// </summary>
    /// <remarks>
    /// Two constant headers do not justify a security-headers dependency (D-4). The richer HTML set —
    /// content security policy, frame options, permissions policy — is application-specific: wrong
    /// defaults break real applications, so the NetEscapades bundle via the <c>BeforeRouting</c> hook is
    /// the documented recipe instead. Deliberately re-expressed package-locally rather than shared with
    /// <c>Cloudstrap.WebApi</c> (D-2).
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
