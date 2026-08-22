namespace Cloudstrap.Demo.BlazorWasm.Presentation.Doctors
{
    using Microsoft.AspNetCore.Components;

    /// <summary>
    /// Lists doctors from the Bff API and posts new ones back to it — through the
    /// convention-registered <see cref="IDoctorsViewModel"/> (deliverable #11's ViewModel-pattern
    /// demonstration). The page keeps only framework concerns: awaiting
    /// <c>InitializeAsync</c> and redirecting a signed-out visitor into login (navigation stays out
    /// of the ViewModel — the D-3 posture).
    /// </summary>
    public partial class DoctorsPage
    {
        [Inject]
        public IDoctorsViewModel ViewModel { get; set; } = null!;

        [Inject]
        public NavigationManager Navigation { get; set; } = null!;

        protected override async Task OnInitializedAsync()
        {
            await ViewModel.InitializeAsync();
            if (!ViewModel.SignedIn)
            {
                // The login endpoint is a server route with no Blazor page, hence forceLoad.
                Navigation.NavigateTo("account/login?returnUrl=/doctors", forceLoad: true);
            }
        }

        protected Task AddDoctorAsync() => ViewModel.AddDoctorAsync();
    }
}
