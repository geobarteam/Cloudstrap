namespace Cloudstrap.BlazorWasm
{
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Http;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Configures the named auth client — base address and the <see cref="CookieHandler"/> — for
    /// whatever name the bound options carry at resolve time. Deferred on purpose: the configure
    /// delegate is invoked exactly once, by the options pipeline, never on a throwaway instance.
    /// </summary>
    internal sealed class AuthHttpClientConfigurator : IConfigureNamedOptions<HttpClientFactoryOptions>
    {
        private readonly IOptions<CloudstrapBlazorWasmOptions> _options;
        private readonly BlazorWasmRegistrationState _state;

        public AuthHttpClientConfigurator(
            IOptions<CloudstrapBlazorWasmOptions> options,
            BlazorWasmRegistrationState state)
        {
            _options = options;
            _state = state;
        }

        public void Configure(string? name, HttpClientFactoryOptions options)
        {
            if (name != _options.Value.AuthHttpClientName)
            {
                return;
            }

            options.HttpClientActions.Add(client => client.BaseAddress = new Uri(_state.BaseAddress));
            options.HttpMessageHandlerBuilderActions.Add(builder =>
                builder.AdditionalHandlers.Add(builder.Services.GetRequiredService<CookieHandler>()));
        }

        public void Configure(HttpClientFactoryOptions options)
        {
            // Only the named auth client is configured here.
        }
    }
}
