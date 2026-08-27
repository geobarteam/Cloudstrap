namespace Cloudstrap.BlazorServer
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Adds the three constant response headers: <c>X-Content-Type-Options: nosniff</c>,
    /// <c>Referrer-Policy: no-referrer</c> and — unless
    /// <see cref="CloudstrapBlazorServerOptions.EnableFrameOptions"/> is switched off —
    /// <c>X-Frame-Options: SAMEORIGIN</c> (D-12).
    /// </summary>
    /// <remarks>
    /// Three constant headers do not justify a security-headers dependency. The richer HTML set — content
    /// security policy, permissions policy — is application-specific: wrong defaults break real
    /// applications, so the NetEscapades bundle via the <c>BeforeRouting</c> hook is the documented recipe
    /// instead. Deliberately re-expressed package-locally rather than shared with <c>Cloudstrap.Mvc</c>.
    /// </remarks>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="options">The bound Blazor Server settings carrying the frame-options switch.</param>
    internal sealed class SecurityHeadersMiddleware(
        RequestDelegate next,
        IOptions<CloudstrapBlazorServerOptions> options)
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
                state =>
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

                    if (options.Value.EnableFrameOptions && !headers.ContainsKey("X-Frame-Options"))
                    {
                        headers["X-Frame-Options"] = "SAMEORIGIN";
                    }

                    return Task.CompletedTask;
                },
                context);

            return next(context);
        }
    }
}
