namespace Cloudstrap.Demo.BlazorWasm.Presentation.Doctors
{
    using System.Net.Http.Json;
    using Cloudstrap.Demo.Contracts;
    using Microsoft.AspNetCore.Components;

    /// <summary>
    /// Lists doctors from the Bff API and posts new ones back to it. The whole feature requires a
    /// signed-in user: an anonymous visit auto-triggers login and returns here. (The auth-state
    /// probe is SUT demo code — deliverable #13 brings the real BFF user-info contract.)
    /// </summary>
    public partial class DoctorsPage
    {
        [Inject]
        public HttpClient Http { get; set; } = null!;

        [Inject]
        public NavigationManager Navigation { get; set; } = null!;

        protected IReadOnlyList<DoctorDto>? Doctors
        {
            get; private set;
        }

        protected string SignedInName { get; private set; } = string.Empty;

        protected string NewName { get; set; } = string.Empty;

        protected string NewSpecialty { get; set; } = string.Empty;

        protected override async Task OnInitializedAsync()
        {
            // The state probe runs first — an anonymous visitor performs exactly one anonymous 200
            // fetch and is then redirected into login; the [Authorize]'d doctors fetch never runs
            // signed out, so the browser console stays clean. The login endpoint is a server route
            // with no Blazor page, hence forceLoad.
            UserStateDto? state = await Http.GetFromJsonAsync<UserStateDto>("api/v1/user/state");
            if (state is not { SignedIn: true })
            {
                Navigation.NavigateTo("account/login?returnUrl=/doctors", forceLoad: true);
                return;
            }

            SignedInName = state.Name;
            await ReloadAsync();
        }

        protected async Task AddDoctorAsync()
        {
            using HttpResponseMessage response = await Http.PostAsJsonAsync(
                "api/doctor", new AddDoctorDto(NewName, NewSpecialty));
            response.EnsureSuccessStatusCode();

            NewName = string.Empty;
            NewSpecialty = string.Empty;
            await ReloadAsync();
        }

        private async Task ReloadAsync()
        {
            Doctors = await Http.GetFromJsonAsync<List<DoctorDto>>("api/doctor");
        }
    }
}
