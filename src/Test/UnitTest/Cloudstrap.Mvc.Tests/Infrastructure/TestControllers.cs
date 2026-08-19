namespace Cloudstrap.Mvc.Tests.Infrastructure
{
    using Cloudstrap.Observability.Correlation;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// The controller the conventional default route selects at the application root.
    /// </summary>
    public sealed class HomeController : Controller
    {
        /// <summary>The marker proving the default route selected this action.</summary>
        public const string Marker = "contoso-home-index";

        /// <summary>Serves the marker over the conventional default route.</summary>
        /// <returns>The marker text.</returns>
        public IActionResult Index()
        {
            return Content(Marker);
        }

        /// <summary>Serves a generated link to <see cref="Index"/>, so URL prefixing is observable.</summary>
        /// <returns>The generated link.</returns>
        public IActionResult Link()
        {
            return Content(Url.Action(nameof(Index), "Home") ?? string.Empty);
        }
    }

    /// <summary>
    /// A controller binding the conventional route's optional <c>id</c> segment.
    /// </summary>
    public sealed class WidgetsController : Controller
    {
        /// <summary>Echoes the bound identifier.</summary>
        /// <param name="id">The identifier bound from the route.</param>
        /// <returns>A body carrying the identifier.</returns>
        public IActionResult Details(int id)
        {
            return Content($"widget-{id}");
        }

        /// <summary>An MVC form target without antiforgery validation of its own.</summary>
        /// <returns>A marker body.</returns>
        [HttpPost]
        public IActionResult Create()
        {
            return Content("widget-created");
        }
    }

    /// <summary>
    /// A controller observing the ambient correlation identifier established by the shipped middleware.
    /// </summary>
    /// <param name="accessor">The ambient correlation accessor.</param>
    [Route("correlation")]
    public sealed class CorrelationController(ICorrelationContextAccessor accessor) : Controller
    {
        /// <summary>Echoes the ambient correlation identifier.</summary>
        /// <returns>The ambient correlation identifier.</returns>
        [HttpGet]
        public IActionResult Get()
        {
            return Content(accessor.CorrelationId ?? string.Empty);
        }

        /// <summary>Echoes the ambient correlation identifier, refusing requests that carry none.</summary>
        /// <returns>The ambient correlation identifier.</returns>
        [HttpGet("required")]
        [CorrelationRequired]
        public IActionResult Required()
        {
            return Content(accessor.CorrelationId ?? string.Empty);
        }
    }

    /// <summary>
    /// A controller setting a security header itself, so the middleware's no-overwrite rule is observable.
    /// </summary>
    [Route("headers")]
    public sealed class HeadersController : Controller
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
    /// A controller whose action fails with a deeply nested exception chain, so both error-response modes
    /// and the depth bound are observable.
    /// </summary>
    [Route("boom")]
    public sealed class BoomController : Controller
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
    /// The consumer's error page at <c>Cloudstrap:Application:ExceptionHandlerPath</c>'s default — the
    /// endpoint stock <c>UseExceptionHandler</c> re-executes for HTML-preferring callers.
    /// </summary>
    [Route("error")]
    public sealed class ErrorController : Controller
    {
        /// <summary>The marker proving the error page rendered.</summary>
        public const string Marker = "contoso-error-page";

        /// <summary>Serves the neutral error page.</summary>
        /// <returns>The marker as HTML.</returns>
        [HttpGet]
        public IActionResult Get()
        {
            return Content(Marker, "text/html");
        }
    }

    /// <summary>
    /// A controller writing to and reading from <see cref="Microsoft.AspNetCore.Http.ISession"/>, so the
    /// establish-on-write cookie semantics and the round-trip are observable.
    /// </summary>
    [Route("session")]
    public sealed class SessionController : Controller
    {
        /// <summary>The value stored in session state by <see cref="Write"/>.</summary>
        public const string Marker = "contoso-session-value";

        /// <summary>The session key <see cref="Write"/> stores under.</summary>
        public const string Key = "marker";

        /// <summary>Stores the marker in session state, establishing the session cookie.</summary>
        /// <returns>A confirmation body.</returns>
        [HttpGet("write")]
        public IActionResult Write()
        {
            HttpContext.Session.SetString(Key, Marker);

            return Content("written");
        }

        /// <summary>Reads the stored marker back from session state.</summary>
        /// <returns>The stored marker, or <c>404</c> when the session carries none.</returns>
        [HttpGet("read")]
        public IActionResult Read()
        {
            string? value = HttpContext.Session.GetString(Key);

            return value is null ? NotFound() : Content(value);
        }
    }

    /// <summary>
    /// A page behind <c>[Authorize]</c>, so the scheme-map predicate and the challenge are observable.
    /// </summary>
    public sealed class SecureController : Controller
    {
        /// <summary>The marker proving the protected page rendered.</summary>
        public const string Marker = "contoso-secure-page";

        /// <summary>Serves the protected page.</summary>
        /// <returns>The marker text.</returns>
        [Authorize]
        public IActionResult Index()
        {
            return Content(Marker);
        }
    }

    /// <summary>
    /// An attribute-routed controller, proving attribute routes are mapped alongside the conventional route.
    /// </summary>
    [Route("api/catalog")]
    public sealed class CatalogController : Controller
    {
        /// <summary>The marker proving the attribute route answered.</summary>
        public const string Marker = "contoso-catalog";

        /// <summary>Serves the marker over the attribute route.</summary>
        /// <returns>The marker text.</returns>
        [HttpGet]
        public IActionResult Get()
        {
            return Content(Marker);
        }
    }
}
