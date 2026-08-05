namespace Cloudstrap.WasmTestProject.Host.Bff.Controllers
{
    using Cloudstrap.Observability;
    using Cloudstrap.WasmTestProject.Contracts;
    using Cloudstrap.WasmTestProject.Host.Bff.Services;
    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// The doctors API: an in-memory CRUD round-trip whose write path records an
    /// <c>AddDoctor</c> business span (deliverable #2 demo).
    /// </summary>
    [ApiController]
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

        [HttpPost]
        public ActionResult<DoctorDto> Add(AddDoctorDto doctor)
        {
            // Operation and component stay low-cardinality per the IBusinessTrace contract —
            // the doctor's name goes in the payload, never in the span.
            using IBusinessTraceScope span = _businessTrace.StartSpan("AddDoctor", "DoctorStore");
            DoctorDto created = _store.Add(doctor);
            span.SetOutcome("succeeded");

            return Ok(created);
        }
    }
}
