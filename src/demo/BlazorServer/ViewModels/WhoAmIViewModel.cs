namespace Cloudstrap.Demo.BlazorServer.ViewModels
{
    using Cloudstrap.BlazorServer;
    using Cloudstrap.Demo.BlazorServer.Services;
    using Cloudstrap.Demo.Contracts;
    using Microsoft.AspNetCore.Components.Authorization;

    /// <summary>
    /// The WhoAmI page's ViewModel, convention-registered by its <c>ViewModel</c> suffix through
    /// <c>AddCloudstrapBlazorCommon</c> (#11) — no explicit registration line anywhere. The API call is
    /// wrapped in <see cref="IBlazorInteractionTrace.StartInteraction"/> (#12, D-9): the call exports as a
    /// root span of its own, with the outbound dependency call parented under it.
    /// </summary>
    public sealed class WhoAmIViewModel : IWhoAmIViewModel
    {
        private readonly IDemoApiClient _demoApi;
        private readonly IBlazorInteractionTrace _interactionTrace;
        private readonly AuthenticationStateProvider _authenticationStateProvider;

        /// <summary>Initializes a new instance of the <see cref="WhoAmIViewModel"/> class.</summary>
        /// <param name="demoApi">The user-flagged typed client into the Api demo host.</param>
        /// <param name="interactionTrace">The #12 interaction trace scope.</param>
        /// <param name="authenticationStateProvider">The signed-in user's authentication state.</param>
        public WhoAmIViewModel(
            IDemoApiClient demoApi,
            IBlazorInteractionTrace interactionTrace,
            AuthenticationStateProvider authenticationStateProvider)
        {
            _demoApi = demoApi;
            _interactionTrace = interactionTrace;
            _authenticationStateProvider = authenticationStateProvider;
        }

        /// <inheritdoc/>
        public string UserName { get; private set; } = string.Empty;

        /// <inheritdoc/>
        public DownstreamWhoAmIDto? WhoAmI
        {
            get; private set;
        }

        /// <inheritdoc/>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            AuthenticationState state = await _authenticationStateProvider.GetAuthenticationStateAsync();
            UserName = state.User.Identity?.Name ?? string.Empty;

            using (_interactionTrace.StartInteraction("whoami"))
            {
                WhoAmI = await _demoApi.GetWhoAmIAsync(cancellationToken);
            }
        }
    }
}
