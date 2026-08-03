namespace Cloudstrap.WebApi.Tests.Infrastructure
{
    using Asp.Versioning;
    using Cloudstrap.Observability.Correlation;
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
