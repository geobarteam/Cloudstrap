namespace Cloudstrap.WasmTestProject.Host.Mvc.Controllers
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// The demo pages: a session-backed visit counter on the conventional default route, and a
    /// deliberately failing action exercising the error contract.
    /// </summary>
    public sealed class HomeController : Controller
    {
        private const string _visitsKey = "visits";

        /// <summary>Increments the session visit counter and renders it.</summary>
        /// <returns>The home view, with the visit count as its model.</returns>
        public IActionResult Index()
        {
            int visits = (HttpContext.Session.GetInt32(_visitsKey) ?? 0) + 1;
            HttpContext.Session.SetInt32(_visitsKey, visits);

            return View(visits);
        }

        /// <summary>Throws a nested exception — the error-path fixture.</summary>
        /// <returns>Never returns.</returns>
        public IActionResult Boom()
        {
            Exception failure = new InvalidOperationException(
                "demo failure",
                new InvalidOperationException("demo root cause"));
            failure.Data["path"] = Request.Path.Value;

            throw failure;
        }
    }
}
