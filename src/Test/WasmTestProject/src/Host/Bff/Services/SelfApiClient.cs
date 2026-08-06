namespace Cloudstrap.WasmTestProject.Host.Bff.Services
{
    using System.Net.Http.Json;
    using Cloudstrap.WasmTestProject.Contracts;

    /// <summary>
    /// The <see cref="ISelfApiClient"/> implementation. Its <see cref="HttpClient"/> is supplied by
    /// Cloudstrap: the base address and timeout come from <c>Cloudstrap:HttpClients:SelfApi</c> and the
    /// correlation handler is already in its pipeline, so nothing here deals with either.
    /// </summary>
    public sealed class SelfApiClient : ISelfApiClient
    {
        private readonly HttpClient _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="SelfApiClient"/> class.
        /// </summary>
        /// <param name="client">The configured client.</param>
        public SelfApiClient(HttpClient client)
        {
            ArgumentNullException.ThrowIfNull(client);

            _client = client;
        }

        /// <inheritdoc/>
        public async Task<string> GetPeerCorrelationIdAsync(CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await _client.GetAsync(
                new Uri("api/diagnostics/correlation", UriKind.Relative),
                cancellationToken);
            response.EnsureSuccessStatusCode();

            CorrelationDto? correlation =
                await response.Content.ReadFromJsonAsync<CorrelationDto>(cancellationToken);

            return correlation?.CorrelationId ?? string.Empty;
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
