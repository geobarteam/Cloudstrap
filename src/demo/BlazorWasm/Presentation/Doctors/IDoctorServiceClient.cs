namespace Cloudstrap.Demo.BlazorWasm.Presentation.Doctors
{
    using Cloudstrap.Demo.Contracts;
    using Refit;

    /// <summary>
    /// The doctors API as a Refit interface (deliverable #13 demo): registered in one line by
    /// <c>AddCloudstrapWasmRefitClient&lt;IDoctorServiceClient&gt;</c>, so every call rides the
    /// cookie+XSRF pipeline. The name ends in <c>Client</c> on purpose — it sits outside the #11
    /// convention scan; the package helper owns its registration.
    /// </summary>
    public interface IDoctorServiceClient
    {
        /// <summary>
        /// Loads the doctors list.
        /// </summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The doctors.</returns>
        [Get("/api/doctor")]
        Task<List<DoctorDto>> GetDoctorsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a doctor — a mutating call, so the package attaches the captured XSRF token and the
        /// Bff's stock antiforgery validation must pass (AC-BW7 live).
        /// </summary>
        /// <param name="doctor">The doctor to add.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The created doctor.</returns>
        [Post("/api/doctor")]
        Task<DoctorDto> AddDoctorAsync([Body] AddDoctorDto doctor, CancellationToken cancellationToken = default);
    }
}
