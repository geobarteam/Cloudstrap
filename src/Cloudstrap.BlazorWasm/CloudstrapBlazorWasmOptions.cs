namespace Cloudstrap.BlazorWasm
{
    /// <summary>
    /// Settings for the Cloudstrap Blazor WebAssembly client helpers, bound from the
    /// <c>Cloudstrap:BlazorWasm</c> configuration section.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In a WebAssembly application this section lives in <c>wwwroot/appsettings.json</c>, which the
    /// browser downloads publicly — it may carry paths and header names, <strong>never secrets</strong>.
    /// </para>
    /// <para>
    /// The section is optional: absent, every default applies. A configuration delegate passed to
    /// <c>AddCloudstrapBlazorWasm</c> wins over configuration values.
    /// </para>
    /// </remarks>
    public sealed class CloudstrapBlazorWasmOptions
    {
        /// <summary>
        /// The configuration section the options bind from.
        /// </summary>
        public const string SectionName = "Cloudstrap:BlazorWasm";

        /// <summary>
        /// Gets or sets the relative path of the BFF's user endpoint, resolved against the
        /// application's base address. Pair it with the server's
        /// <c>Cloudstrap:OpenIdConnect:UserEndpointPath</c> — both sides must agree.
        /// </summary>
        /// <value>The relative user endpoint path. Defaults to <c>"bff/user"</c>.</value>
        public string UserEndpointPath { get; set; } = "bff/user";

        /// <summary>
        /// Gets or sets the XSRF header name — used both to capture the token from the BFF's user
        /// endpoint response and to attach it to mutating requests. One option drives both sides, so
        /// capture and attachment can never diverge; it must also agree with the server's antiforgery
        /// header configuration.
        /// </summary>
        /// <value>The XSRF header name. Defaults to <c>"X-XSRF-TOKEN"</c>.</value>
        public string XsrfHeaderName { get; set; } = "X-XSRF-TOKEN";

        /// <summary>
        /// Gets or sets the name of the internal <see cref="HttpClient"/> the BFF authentication
        /// state provider fetches the user endpoint with.
        /// </summary>
        /// <value>The named client name. Defaults to <c>"CloudstrapBffAuth"</c>.</value>
        public string AuthHttpClientName { get; set; } = "CloudstrapBffAuth";
    }
}
