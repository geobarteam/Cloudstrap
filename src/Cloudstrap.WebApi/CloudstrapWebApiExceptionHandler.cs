namespace Cloudstrap.WebApi
{
    using Cloudstrap.Core;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.AspNetCore.Diagnostics;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.Extensions.Primitives;

    /// <summary>
    /// The terminal exception handler: turns anything the application did not handle into an RFC 9457
    /// <c>application/problem+json</c> response, generic by default and diagnosable on explicit opt-in, and
    /// logs the exception server-side exactly once.
    /// </summary>
    /// <param name="problemDetails">The framework's problem-details writer.</param>
    /// <param name="options">The Web API settings carrying the detail switch.</param>
    /// <param name="environment">The hosting environment backing the unset detail switch.</param>
    /// <param name="correlation">The ambient correlation accessor.</param>
    /// <param name="correlationOptions">The correlation settings naming the inbound header.</param>
    /// <param name="logger">The logger the exception is recorded on.</param>
    internal sealed class CloudstrapWebApiExceptionHandler(
        IProblemDetailsService problemDetails,
        IOptions<WebApiOptions> options,
        IHostEnvironment environment,
        ICorrelationContextAccessor correlation,
        IOptions<CorrelationOptions> correlationOptions,
        ILogger<CloudstrapWebApiExceptionHandler> logger) : IExceptionHandler
    {
        /// <summary>
        /// How far the inner-exception chain is walked. Bounded so a deeply wrapped failure cannot turn one
        /// error response into an unbounded payload.
        /// </summary>
        private const int _maxInnerExceptionDepth = 5;

        /// <summary>
        /// Writes the error response for an unhandled exception.
        /// </summary>
        /// <param name="httpContext">The request being answered.</param>
        /// <param name="exception">The unhandled exception.</param>
        /// <param name="cancellationToken">A token that cancels the write.</param>
        /// <returns>
        /// Always <see langword="true"/>: this handler is the terminal fallback, so nothing after it needs to
        /// run. Handlers a consumer registered before <c>AddCloudstrapWebApi</c> get their attempt first.
        /// </returns>
        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            WebApiLog.UnhandledException(
                logger,
                httpContext.Request.Method,
                httpContext.Request.Path.Value,
                exception);

            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

            ProblemDetails problem = new()
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
            };

            string? correlationId = ResolveCorrelationId(httpContext);

            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                problem.Extensions["correlationId"] = correlationId;
            }

            bool includeDetails = EnvironmentDefault.Resolve(
                options.Value.ExceptionHandling.IncludeDetails,
                environment.IsDevelopment());

            if (includeDetails)
            {
                problem.Extensions["exceptionType"] = exception.GetType().FullName;
                problem.Extensions["exceptionMessage"] = exception.Message;
                problem.Extensions["stackTrace"] = exception.StackTrace;
                problem.Extensions["innerExceptions"] = DescribeInnerChain(exception);
            }

            await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problem,
            });

            return true;
        }

        /// <summary>
        /// Resolves the correlation identifier to quote back to the caller.
        /// </summary>
        /// <param name="httpContext">The request being answered.</param>
        /// <returns>The correlation identifier, or <see langword="null"/> when none can be determined.</returns>
        /// <remarks>
        /// Three sources, in order of reliability. The request-scoped identifier the correlation middleware
        /// stored on the <see cref="HttpContext"/> is authoritative and survives an exception unwinding past
        /// that middleware — which is the normal case here, since the exception handler sits above it so that
        /// a failure in routing itself is still answered. The async-local accessor answers only while the
        /// middleware's scope is still on the stack. The inbound header is the last resort, covering a
        /// failure that happened before the correlation middleware ran at all.
        /// </remarks>
        private string? ResolveCorrelationId(HttpContext httpContext)
        {
            string? stored = httpContext.GetCloudstrapCorrelationId();

            if (!string.IsNullOrWhiteSpace(stored))
            {
                return stored;
            }

            if (!string.IsNullOrWhiteSpace(correlation.CorrelationId))
            {
                return correlation.CorrelationId;
            }

            return httpContext.Request.Headers
                .TryGetValue(correlationOptions.Value.HeaderName, out StringValues inbound)
                ? inbound.ToString()
                : null;
        }

        /// <summary>
        /// Describes the inner-exception chain, up to <see cref="_maxInnerExceptionDepth"/> levels.
        /// </summary>
        /// <param name="exception">The exception whose chain is walked.</param>
        /// <returns>One entry per inner exception, outermost first.</returns>
        private static List<InnerExceptionDescription> DescribeInnerChain(Exception exception)
        {
            List<InnerExceptionDescription> chain = [];

            Exception? current = exception.InnerException;

            while (current is not null && chain.Count < _maxInnerExceptionDepth)
            {
                chain.Add(new InnerExceptionDescription(current.GetType().FullName, current.Message));
                current = current.InnerException;
            }

            return chain;
        }

        /// <summary>
        /// One link of the inner-exception chain, as it appears in the problem-details payload.
        /// </summary>
        /// <param name="Type">The exception's full type name.</param>
        /// <param name="Message">The exception's message.</param>
        private sealed record InnerExceptionDescription(string? Type, string Message);
    }
}
