namespace Cloudstrap.Demo.BlazorWasm.Presentation.Doctors
{
    using System.Text.Json;
    using Cloudstrap.BlazorCommon;
    using Cloudstrap.Demo.Contracts;
    using Microsoft.AspNetCore.Components.Authorization;
    using Refit;

    /// <summary>
    /// The doctors page's ViewModel: reads auth state from the #13 package's BFF-driven provider
    /// (the same call captures the XSRF token before any POST), drives the doctors round-trip
    /// through the Refit client, and routes failures to the consumer-owned <see cref="IErrorHandler"/>.
    /// Registered by convention — <c>AddCloudstrapBlazorCommon</c> picks it up via the
    /// <c>ViewModel</c> suffix; navigation stays out (pages inject <c>NavigationManager</c>
    /// directly, the D-3 posture).
    /// </summary>
    public sealed class DoctorsViewModel : IDoctorsViewModel
    {
        private readonly IDoctorServiceClient _client;
        private readonly AuthenticationStateProvider _authenticationStateProvider;
        private readonly IErrorHandler _errorHandler;

        public DoctorsViewModel(
            IDoctorServiceClient client,
            AuthenticationStateProvider authenticationStateProvider,
            IErrorHandler errorHandler)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(authenticationStateProvider);
            ArgumentNullException.ThrowIfNull(errorHandler);

            _client = client;
            _authenticationStateProvider = authenticationStateProvider;
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
            // The package's cached BFF fetch: one anonymous-safe call answers the auth state AND
            // captures the XSRF token into the shared store — so it always runs before any POST,
            // and the [Authorize]'d doctors fetch never runs signed out (clean browser console).
            AuthenticationState state = await _authenticationStateProvider.GetAuthenticationStateAsync();
            if (state.User.Identity is not { IsAuthenticated: true })
            {
                return;
            }

            SignedIn = true;
            SignedInName = state.User.Identity.Name ?? string.Empty;
            await ReloadAsync(cancellationToken);
        }

        public async Task AddDoctorAsync()
        {
            try
            {
                await _client.AddDoctorAsync(new AddDoctorDto(NewName, NewSpecialty));

                NewName = string.Empty;
                NewSpecialty = string.Empty;
                await ReloadAsync(CancellationToken.None);
            }
            catch (ApiException exception)
            {
                _errorHandler.ShowError(ReadServerMessage(exception));
            }
            catch (HttpRequestException exception)
            {
                _errorHandler.HandleError(exception);
            }
        }

        private async Task ReloadAsync(CancellationToken cancellationToken) =>
            Doctors = await _client.GetDoctorsAsync(cancellationToken);

        private static string ReadServerMessage(ApiException exception)
        {
            // ValidationProblem body shape: { "errors": { "Name": ["A doctor name is required."] } }
            if (!string.IsNullOrWhiteSpace(exception.Content))
            {
                try
                {
                    using JsonDocument problem = JsonDocument.Parse(exception.Content);
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
            }

            return $"Adding the doctor failed ({(int)exception.StatusCode}).";
        }
    }
}
