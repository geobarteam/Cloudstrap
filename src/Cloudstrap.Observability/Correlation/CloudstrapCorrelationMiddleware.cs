namespace Cloudstrap.Observability.Correlation
{
    using Cloudstrap.Core;
    using Microsoft.AspNetCore.Diagnostics.HealthChecks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Options;
    using Microsoft.Extensions.Primitives;

    /// <summary>
    /// The one correlation middleware: establishes the ambient correlation identifier for every request —
    /// the inbound header value when present, a generated identifier otherwise — and enforces the
    /// correlation requirement with a <c>400 application/problem+json</c> response naming the configured
    /// header. Exemptions: configured health and excluded paths, endpoints carrying
    /// <see cref="HealthCheckOptions"/> metadata, and <see cref="AllowNoCorrelationAttribute"/>.
    /// </summary>
    internal sealed class CloudstrapCorrelationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ICorrelationContextAccessor _accessor;
        private readonly ICorrelationSource _source;
        private readonly IOptions<CorrelationOptions> _options;
        private readonly IProblemDetailsService _problemDetails;

        public CloudstrapCorrelationMiddleware(
            RequestDelegate next,
            ICorrelationContextAccessor accessor,
            ICorrelationSource source,
            IOptions<CorrelationOptions> options,
            IProblemDetailsService problemDetails)
        {
            _next = next;
            _accessor = accessor;
            _source = source;
            _options = options;
            _problemDetails = problemDetails;
        }

        public async Task Invoke(HttpContext context)
        {
            CorrelationOptions options = _options.Value;

            string? correlationId = ReadHeader(context, options.HeaderName);

            if (correlationId is null)
            {
                if (IsRequired(context, options.Request) && !IsExempt(context, options.Request))
                {
                    await WriteMissingCorrelationProblem(context, options.HeaderName);
                    return;
                }

                correlationId = _source.GenerateCorrelation();
            }

            _accessor.CorrelationId = correlationId;

            // Also request-scoped, so code holding the HttpContext outside this middleware's async scope —
            // an exception handler placed ahead of it, for instance — can still read the identifier.
            context.Items[HttpContextExtensions.CorrelationIdItemKey] = correlationId;

            await _next(context);
        }

        private static string? ReadHeader(HttpContext context, string headerName)
        {
            if (context.Request.Headers.TryGetValue(headerName, out StringValues values))
            {
                string? value = values.ToString();

                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            return null;
        }

        private static bool IsRequired(HttpContext context, CorrelationRequestOptions request) =>
            request.RequireForAllEndpoints
            || context.GetEndpoint()?.Metadata.GetMetadata<CorrelationRequiredAttribute>() is not null;

        private static bool IsExempt(HttpContext context, CorrelationRequestOptions request)
        {
            string path = context.Request.Path.Value ?? string.Empty;

            if (request.HealthEndpoints.Exists(endpoint => PathEquals(path, endpoint))
                || request.ExcludeEndpoints.Exists(endpoint => PathEquals(path, endpoint)))
            {
                return true;
            }

            Endpoint? endpoint = context.GetEndpoint();

            return endpoint?.Metadata.GetMetadata<AllowNoCorrelationAttribute>() is not null
                || endpoint?.Metadata.GetMetadata<HealthCheckOptions>() is not null;
        }

        private static bool PathEquals(string path, string exemptPath) =>
            string.Equals(path, exemptPath, StringComparison.OrdinalIgnoreCase);

        private async Task WriteMissingCorrelationProblem(HttpContext context, string headerName)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;

            await _problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Missing correlation identifier.",
                    Detail = $"The request must carry a correlation identifier in the '{headerName}' header.",
                },
            });
        }
    }
}
