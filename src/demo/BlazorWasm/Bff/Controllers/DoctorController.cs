namespace Cloudstrap.Demo.BlazorWasm.Bff.Controllers
{
    using Cloudstrap.Demo.BlazorWasm.Bff.Services;
    using Cloudstrap.Demo.Contracts;
    using Cloudstrap.Observability;
    using Microsoft.AspNetCore.Antiforgery;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// The doctors API: an in-memory CRUD round-trip whose write path records an
    /// <c>AddDoctor</c> business span (deliverable #2 demo). The whole feature — read and write —
    /// requires a signed-in user on the default (cookie) scheme, the <see cref="UserController"/>
    /// pattern; the home page is the only anonymous page.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/doctor")]
    public sealed class DoctorController : ControllerBase
    {
        private readonly InMemoryDoctorStore _store;
        private readonly IBusinessTrace _businessTrace;

        public DoctorController(InMemoryDoctorStore store, IBusinessTrace businessTrace)
        {
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(businessTrace);

            _store = store;
            _businessTrace = businessTrace;
        }

        [HttpGet]
        public ActionResult<IReadOnlyList<DoctorDto>> Get() => Ok(_store.GetAll());

        // Deliverable #13 demo (AC-BW7): the mutating POST really validates the XSRF token the
        // WASM client captured from /bff/user — a session-cookie-only forgery is rejected with 400.
        // Validation goes through IAntiforgery.ValidateRequestAsync: this host is an API-controller
        // app (AddCloudstrapWebApi → AddControllers), where the [ValidateAntiForgeryToken] filter
        // service does not exist — that attribute needs AddControllersWithViews.
        [HttpPost]
        public async Task<ActionResult<DoctorDto>> Add(
            AddDoctorDto doctor,
            [FromServices] IAntiforgery antiforgery)
        {
            try
            {
                await antiforgery.ValidateRequestAsync(HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return BadRequest("The XSRF token is missing or invalid.");
            }

            // Deliverable #11 demo: the reachable error path — reject a blank name with a 400
            // before opening the business span, so the ViewModel can route the failure to the
            // consumer's IErrorHandler.
            if (string.IsNullOrWhiteSpace(doctor.Name))
            {
                ModelState.AddModelError(nameof(doctor.Name), "A doctor name is required.");
                return ValidationProblem(ModelState);
            }

            // Operation and component stay low-cardinality per the IBusinessTrace contract —
            // the doctor's name goes in the payload, never in the span.
            using IBusinessTraceScope span = _businessTrace.StartSpan("AddDoctor", "DoctorStore");
            DoctorDto created = _store.Add(doctor);
            span.SetOutcome("succeeded");

            return Ok(created);
        }
    }
}
