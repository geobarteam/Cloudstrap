namespace Cloudstrap.WebApi.Tests.Infrastructure
{
    using Asp.Versioning;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Authentication.JwtBearer;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Routing;

    /// <summary>A widget as version 1.0 describes it.</summary>
    /// <param name="Name">The widget name.</param>
    /// <param name="Version">The API version that produced the payload.</param>
    public sealed record WidgetDto(string Name, string Version);

    /// <summary>A payload with an optional member, so the null-handling default is observable.</summary>
    /// <param name="Name">The payload name.</param>
    /// <param name="Description">An optional description, deliberately left unset by the fixture.</param>
    public sealed record PayloadDto(string Name, string? Description);

    /// <summary>A generated link, so the lowercase-URL default is observable.</summary>
    /// <param name="Path">The generated path.</param>
    public sealed record LinkDto(string Path);

    /// <summary>The ambient correlation identifier, echoed back to the caller.</summary>
    /// <param name="CorrelationId">The ambient correlation identifier.</param>
    public sealed record CorrelationEchoDto(string CorrelationId);

    /// <summary>The raw claim types carried by the authenticated principal.</summary>
    /// <param name="ClaimTypes">The claim types, exactly as the token spelled them.</param>
    public sealed record ClaimsDto(string[] ClaimTypes);

    /// <summary>
    /// A controller spanning two versions on one route, so both stock version readers are exercised.
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    [ApiVersion("2.0")]
    [Route("api/widgets")]
    public sealed class WidgetsController : ControllerBase
    {
        /// <summary>Serves the version 1.0 payload.</summary>
        /// <returns>The version 1.0 widget.</returns>
        [HttpGet]
        [MapToApiVersion("1.0")]
        public ActionResult<WidgetDto> GetV1()
        {
            return Ok(new WidgetDto("gadget", "1.0"));
        }

        /// <summary>Serves the version 2.0 payload.</summary>
        /// <returns>The version 2.0 widget.</returns>
        [HttpGet]
        [MapToApiVersion("2.0")]
        public ActionResult<WidgetDto> GetV2()
        {
            return Ok(new WidgetDto("gadget", "2.0"));
        }
    }

    /// <summary>
    /// A controller versioned by URL segment, so route substitution is exercised.
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    [ApiVersion("2.0")]
    [Route("api/v{version:apiVersion}/widgets")]
    public sealed class VersionedWidgetsController : ControllerBase
    {
        /// <summary>Serves the widget for the version in the route.</summary>
        /// <returns>The widget.</returns>
        [HttpGet]
        public ActionResult<WidgetDto> Get()
        {
            return Ok(new WidgetDto("gadget", RouteData.Values["version"]?.ToString() ?? "none"));
        }
    }

    /// <summary>
    /// A controller reachable on version 1.0 only, so "one document per version" is not vacuous.
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/ledger")]
    public sealed class LedgerController : ControllerBase
    {
        /// <summary>Serves the version 1.0 ledger.</summary>
        /// <returns>The widget standing in for a ledger entry.</returns>
        [HttpGet]
        public ActionResult<WidgetDto> Get()
        {
            return Ok(new WidgetDto("ledger", "1.0"));
        }
    }

    /// <summary>
    /// A controller reachable on version 2.0 only, the counterpart to <see cref="LedgerController"/>.
    /// </summary>
    [ApiController]
    [ApiVersion("2.0")]
    [Route("api/v{version:apiVersion}/gadgets")]
    public sealed class GadgetsController : ControllerBase
    {
        /// <summary>Serves the version 2.0 gadget.</summary>
        /// <returns>The gadget.</returns>
        [HttpGet]
        public ActionResult<WidgetDto> Get()
        {
            return Ok(new WidgetDto("gadget", "2.0"));
        }
    }

    /// <summary>
    /// A controller on a deprecated version, so the deprecation metadata is observable in the document.
    /// </summary>
    [ApiController]
    [ApiVersion("1.0", Deprecated = true)]
    [Route("api/v{version:apiVersion}/retired")]
    public sealed class RetiredController : ControllerBase
    {
        /// <summary>Serves the deprecated payload.</summary>
        /// <returns>The widget.</returns>
        [HttpGet]
        public ActionResult<WidgetDto> Get()
        {
            return Ok(new WidgetDto("retired", "1.0"));
        }
    }

    /// <summary>
    /// A controller carrying no version metadata at all — the ported convention's subject.
    /// </summary>
    [ApiController]
    [Route("api/legacy")]
    public sealed class LegacyController : ControllerBase
    {
        /// <summary>Serves the legacy payload.</summary>
        /// <returns>The widget.</returns>
        [HttpGet]
        public ActionResult<WidgetDto> Get()
        {
            return Ok(new WidgetDto("legacy", "unattributed"));
        }
    }

    /// <summary>
    /// A controller returning a payload whose optional member is unset.
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/payload")]
    public sealed class PayloadController : ControllerBase
    {
        /// <summary>Serves a payload with an unset optional member.</summary>
        /// <returns>The payload.</returns>
        [HttpGet]
        public ActionResult<PayloadDto> Get()
        {
            return Ok(new PayloadDto("widget", Description: null));
        }
    }

    /// <summary>
    /// A controller generating a link to a mixed-case action, so URL casing is observable.
    /// </summary>
    /// <param name="links">The framework link generator.</param>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/Link")]
    public sealed class LinkController(LinkGenerator links) : ControllerBase
    {
        /// <summary>Serves a link generated for the mixed-case target action.</summary>
        /// <returns>The generated link.</returns>
        [HttpGet]
        public ActionResult<LinkDto> Get()
        {
            string path = links.GetPathByAction(HttpContext, action: "Target", controller: "Link")
                ?? string.Empty;

            return Ok(new LinkDto(path));
        }

        /// <summary>The link target.</summary>
        /// <returns>An empty successful response.</returns>
        [HttpGet("Target")]
        public IActionResult Target()
        {
            return Ok();
        }
    }

    /// <summary>
    /// A controller whose action fails with a deeply nested exception chain, so both error-response modes
    /// and the depth bound are observable.
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/boom")]
    public sealed class BoomController : ControllerBase
    {
        /// <summary>The text carried by the innermost exception of the fixture chain.</summary>
        public const string RootCauseMessage = "contoso root cause";

        /// <summary>Throws an eight-deep exception chain.</summary>
        /// <returns>Never returns.</returns>
        [HttpGet]
        public IActionResult Get()
        {
            Exception failure = BuildNestedFailure(depth: 8);
            failure.Data["path"] = Request.Path.Value;

            throw failure;
        }

        private static Exception BuildNestedFailure(int depth)
        {
            Exception current = new InvalidOperationException(RootCauseMessage);

            for (int level = 1; level <= depth; level++)
            {
                current = new InvalidOperationException($"failure level {level}", current);
            }

            return current;
        }
    }

    /// <summary>
    /// A controller setting a security header itself, so the middleware's no-overwrite rule is observable.
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/headers")]
    public sealed class HeadersController : ControllerBase
    {
        /// <summary>Sets its own referrer policy and returns.</summary>
        /// <returns>An empty successful response.</returns>
        [HttpGet]
        public IActionResult Get()
        {
            Response.Headers["Referrer-Policy"] = "same-origin";

            return Ok();
        }
    }

    /// <summary>
    /// The endpoint <c>Cloudstrap:Application:ExceptionHandlerPath</c> names. The Web API handler terminates
    /// rather than re-executing, so this action must never run for an unhandled exception.
    /// </summary>
    [ApiController]
    [ApiVersionNeutral]
    [Route("error")]
    public sealed class ErrorController : ControllerBase
    {
        /// <summary>The marker proving this action ran.</summary>
        public const string Marker = "re-executed-error-endpoint";

        /// <summary>Serves the marker.</summary>
        /// <returns>The marker text.</returns>
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(Marker);
        }
    }

    /// <summary>
    /// A controller whose actions differ only in their authorization metadata, so the fallback policy's
    /// blast radius and its per-endpoint opt-out are both observable.
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/guarded")]
    public sealed class GuardedController : ControllerBase
    {
        /// <summary>An action carrying no authorization metadata at all.</summary>
        /// <returns>A marker body.</returns>
        [HttpGet]
        public IActionResult Get()
        {
            return Ok("guarded");
        }

        /// <summary>An action opting out of the fallback policy.</summary>
        /// <returns>A marker body.</returns>
        [HttpGet("open")]
        [AllowAnonymous]
        public IActionResult Open()
        {
            return Ok("open");
        }

        /// <summary>An action demanding authorization explicitly.</summary>
        /// <returns>A marker body.</returns>
        [HttpGet("attributed")]
        [Authorize]
        public IActionResult Attributed()
        {
            return Ok("attributed");
        }
    }

    /// <summary>
    /// A controller exercising token validation directly, without involving the authorization pipeline —
    /// so what it proves is exactly whether a token was accepted, and why.
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/token")]
    public sealed class TokenController : ControllerBase
    {
        /// <summary>
        /// Authenticates the request against the bearer scheme and reports the outcome.
        /// </summary>
        /// <returns>
        /// <c>200</c> with the principal's raw claim types when the token validates, <c>401</c> otherwise.
        /// </returns>
        [HttpGet]
        public async Task<ActionResult<ClaimsDto>> Get()
        {
            AuthenticateResult result = await HttpContext.AuthenticateAsync(
                JwtBearerDefaults.AuthenticationScheme);

            if (!result.Succeeded || result.Principal is null)
            {
                return Unauthorized();
            }

            return Ok(new ClaimsDto([.. result.Principal.Claims.Select(claim => claim.Type)]));
        }
    }

    /// <summary>
    /// A controller observing the ambient correlation identifier established by the shipped middleware.
    /// </summary>
    /// <param name="accessor">The ambient correlation accessor.</param>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/correlation")]
    public sealed class CorrelationController(ICorrelationContextAccessor accessor) : ControllerBase
    {
        /// <summary>Echoes the ambient correlation identifier.</summary>
        /// <returns>The ambient correlation identifier.</returns>
        [HttpGet]
        public ActionResult<CorrelationEchoDto> Get()
        {
            return Ok(new CorrelationEchoDto(accessor.CorrelationId ?? string.Empty));
        }

        /// <summary>Echoes the ambient correlation identifier, refusing requests that carry none.</summary>
        /// <returns>The ambient correlation identifier.</returns>
        [HttpGet("required")]
        [CorrelationRequired]
        public ActionResult<CorrelationEchoDto> Required()
        {
            return Ok(new CorrelationEchoDto(accessor.CorrelationId ?? string.Empty));
        }
    }
}
