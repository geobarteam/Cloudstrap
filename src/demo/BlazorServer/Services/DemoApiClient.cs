namespace Cloudstrap.Demo.BlazorServer.Services
{
    using System.Net.Http.Json;
    using Cloudstrap.Demo.Contracts;

    /// <summary>
    /// The <see cref="IDemoApiClient"/> implementation. Its <see cref="HttpClient"/> is supplied by
    /// Cloudstrap: the base address and timeout come from <c>Cloudstrap:HttpClients:DemoApi</c>, and
    /// the user-token handler is already in its pipeline — nothing here deals with tokens.
    /// </summary>
    public sealed class DemoApiClient : IDemoApiClient
    {
        private readonly HttpClient _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="DemoApiClient"/> class.
        /// </summary>
        /// <param name="client">The configured client.</param>
        public DemoApiClient(HttpClient client)
        {
            ArgumentNullException.ThrowIfNull(client);

            _client = client;
        }

        /// <inheritdoc/>
        public async Task<DownstreamWhoAmIDto> GetWhoAmIAsync(CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await _client.GetAsync(
                new Uri("api/v1/downstream/whoami", UriKind.Relative),
                cancellationToken);
            response.EnsureSuccessStatusCode();

            DownstreamWhoAmIDto? whoAmI =
                await response.Content.ReadFromJsonAsync<DownstreamWhoAmIDto>(cancellationToken);

            return whoAmI ?? new DownstreamWhoAmIDto(string.Empty, string.Empty, string.Empty, string.Empty);
        }
    }
}
