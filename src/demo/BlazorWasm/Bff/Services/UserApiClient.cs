namespace Cloudstrap.Demo.BlazorWasm.Bff.Services
{
    using System.Net.Http.Json;
    using Cloudstrap.Demo.Contracts;

    /// <summary>
    /// The <see cref="IUserApiClient"/> implementation. Its <see cref="HttpClient"/> is supplied by
    /// Cloudstrap: the base address and timeout come from <c>Cloudstrap:HttpClients:UserApi</c>, and
    /// the user-token handler is already in its pipeline — nothing here deals with tokens.
    /// </summary>
    public sealed class UserApiClient : IUserApiClient
    {
        private readonly HttpClient _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserApiClient"/> class.
        /// </summary>
        /// <param name="client">The configured client.</param>
        public UserApiClient(HttpClient client)
        {
            ArgumentNullException.ThrowIfNull(client);

            _client = client;
        }

        /// <inheritdoc/>
        public async Task<MachineStatusDto> GetMachineStatusAsync(CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await _client.GetAsync(
                new Uri("api/v1/machine/status", UriKind.Relative),
                cancellationToken);
            response.EnsureSuccessStatusCode();

            MachineStatusDto? status =
                await response.Content.ReadFromJsonAsync<MachineStatusDto>(cancellationToken);

            return status ?? new MachineStatusDto(string.Empty, string.Empty, string.Empty);
        }
    }
}
