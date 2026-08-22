namespace Cloudstrap.Demo.BlazorWasm.Presentation.Doctors
{
    using System.Net.Http.Json;
    using System.Text.Json;
    using Cloudstrap.BlazorCommon;
    using Cloudstrap.Demo.Contracts;

    /// <summary>
    /// The doctors page's ViewModel: performs the auth-state probe and the doctors round-trip
    /// against the Bff, and routes failures to the consumer-owned <see cref="IErrorHandler"/>.
    /// Registered by convention — <c>AddCloudstrapBlazorCommon</c> picks it up via the
    /// <c>ViewModel</c> suffix; navigation stays out (pages inject <c>NavigationManager</c>
    /// directly, the D-3 posture).
    /// </summary>
    public sealed class DoctorsViewModel : IDoctorsViewModel
    {
        private readonly HttpClient _http;
        private readonly IErrorHandler _errorHandler;

        public DoctorsViewModel(HttpClient http, IErrorHandler errorHandler)
        {
            ArgumentNullException.ThrowIfNull(http);
            ArgumentNullException.ThrowIfNull(errorHandler);

            _http = http;
            _errorHandler = errorHandler;
        }

        public bool SignedIn
        {
            get; private set;
        }

        public string SignedInName { get; private set; } = string.Empty;

        public IReadOnlyList<DoctorDto>? Doctors
        {
            get; private set;
        }

        public string NewName { get; set; } = string.Empty;

        public string NewSpecialty { get; set; } = string.Empty;

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            // The state probe runs first — an anonymous visitor performs exactly one anonymous 200
            // fetch and the [Authorize]'d doctors fetch never runs signed out, so the browser
            // console stays clean. The page decides what to do about a signed-out user.
            UserStateDto? state = await _http.GetFromJsonAsync<UserStateDto>(
                "api/v1/user/state", cancellationToken);
            if (state is not { SignedIn: true })
            {
                return;
            }

            SignedIn = true;
            SignedInName = state.Name;
            await ReloadAsync(cancellationToken);
        }

        public async Task AddDoctorAsync()
        {
            try
            {
                using HttpResponseMessage response = await _http.PostAsJsonAsync(
                    "api/doctor", new AddDoctorDto(NewName, NewSpecialty));
                if (!response.IsSuccessStatusCode)
                {
                    _errorHandler.ShowError(await ReadServerMessageAsync(response));
                    return;
                }

                NewName = string.Empty;
                NewSpecialty = string.Empty;
                await ReloadAsync(CancellationToken.None);
            }
            catch (HttpRequestException exception)
            {
                _errorHandler.HandleError(exception);
            }
        }

        private async Task ReloadAsync(CancellationToken cancellationToken) =>
            Doctors = await _http.GetFromJsonAsync<List<DoctorDto>>("api/doctor", cancellationToken);

        private static async Task<string> ReadServerMessageAsync(HttpResponseMessage response)
        {
            // ValidationProblem body shape: { "errors": { "Name": ["A doctor name is required."] } }
            try
            {
                using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (problem.RootElement.TryGetProperty("errors", out JsonElement errors))
                {
                    foreach (JsonProperty property in errors.EnumerateObject())
                    {
                        foreach (JsonElement message in property.Value.EnumerateArray())
                        {
                            string? text = message.GetString();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                return text;
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Not a problem-details body — fall through to the generic message.
            }

            return $"Adding the doctor failed ({(int)response.StatusCode}).";
        }
    }
}
