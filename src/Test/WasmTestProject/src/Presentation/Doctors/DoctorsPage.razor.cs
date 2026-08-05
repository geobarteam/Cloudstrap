namespace Cloudstrap.WasmTestProject.Presentation.Doctors
{
    using System.Net.Http.Json;
    using Cloudstrap.WasmTestProject.Contracts;
    using Microsoft.AspNetCore.Components;

    /// <summary>
    /// Lists doctors from the Bff API and posts new ones back to it.
    /// </summary>
    public partial class DoctorsPage
    {
        [Inject]
        public HttpClient Http { get; set; } = null!;

        protected IReadOnlyList<DoctorDto>? Doctors
        {
            get; private set;
        }

        protected string NewName { get; set; } = string.Empty;

        protected string NewSpecialty { get; set; } = string.Empty;

        protected override Task OnInitializedAsync() => ReloadAsync();

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
