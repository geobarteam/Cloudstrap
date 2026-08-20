namespace Cloudstrap.WasmTestProject.Host.Mvc.Controllers
{
    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// The minimal consumer error action the Cloudstrap.Mvc README documents: stock
    /// <c>UseExceptionHandler</c> re-executes this route for HTML-preferring callers, keeping the 500
    /// status it set before re-execution.
    /// </summary>
    [Route("/error")]
    public sealed class ErrorController : Controller
    {
        /// <summary>Serves the neutral error page, with no exception content.</summary>
        /// <returns>The error view.</returns>
        [HttpGet]
        public IActionResult Get()
        {
            return View("Index");
        }
    }
}
