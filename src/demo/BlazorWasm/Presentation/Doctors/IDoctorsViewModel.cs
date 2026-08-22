namespace Cloudstrap.Demo.BlazorWasm.Presentation.Doctors
{
    using Cloudstrap.BlazorCommon;
    using Cloudstrap.Demo.Contracts;

    /// <summary>
    /// State and actions of the doctors page — the ViewModel-pattern demonstration for
    /// deliverable #11: convention-registered by <c>AddCloudstrapBlazorCommon</c>, initialized
    /// through <see cref="IViewModel"/>.
    /// </summary>
    public interface IDoctorsViewModel : IViewModel
    {
        /// <summary>Gets a value indicating whether the auth-state probe found a signed-in user.</summary>
        bool SignedIn
        {
            get;
        }

        /// <summary>Gets the signed-in user's display name.</summary>
        string SignedInName
        {
            get;
        }

        /// <summary>Gets the doctors list, or <see langword="null"/> until loaded.</summary>
        IReadOnlyList<DoctorDto>? Doctors
        {
            get;
        }

        /// <summary>Gets or sets the name of the doctor being added.</summary>
        string NewName
        {
            get; set;
        }

        /// <summary>Gets or sets the specialty of the doctor being added.</summary>
        string NewSpecialty
        {
            get; set;
        }

        /// <summary>
        /// Posts the new doctor to the Bff; failures surface through the consumer's
        /// <see cref="IErrorHandler"/> instead of throwing into the render loop.
        /// </summary>
        /// <returns>A task that completes when the add round-trip finishes.</returns>
        Task AddDoctorAsync();
    }
}
