namespace Cloudstrap.Extensions
{
    using Cloudstrap.Core;

    /// <summary>
    /// Supplies the message handler that attaches the signed-in user's access token to a Cloudstrap typed
    /// HTTP client. This package declares the seam and never implements it: the implementation ships with
    /// <c>Cloudstrap.Authentication.OpenIdConnect</c>, so nothing here depends on an authentication stack.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A client opts in through its configuration — <c>Cloudstrap:HttpClients:{name}:AddUserAccessToken</c>.
    /// For each opted-in client the registered provider is asked for a handler once, when that client's
    /// pipeline is first built.
    /// </para>
    /// <para>
    /// The provider is resolved from the container lazily at that moment, never at registration time, so the
    /// order of the <c>AddCloudstrap…</c> calls in <c>Program.cs</c> does not matter. When a client asks for
    /// a user token and no provider is registered, client creation fails with a message naming the flag and
    /// the package that supplies the implementation — an unauthenticated request is never sent in its place.
    /// </para>
    /// <para>
    /// Implementations are asked for a new handler per client and must not return a handler whose
    /// <see cref="DelegatingHandler.InnerHandler"/> is already set: the HTTP client factory owns the chain.
    /// </para>
    /// </remarks>
    public interface IUserAccessTokenHandlerProvider
    {
        /// <summary>
        /// Creates the handler attaching the signed-in user's access token to the client's requests.
        /// </summary>
        /// <param name="clientName">
        /// The logical client name, which is also its <c>Cloudstrap:HttpClients:</c> subsection.
        /// </param>
        /// <param name="tokenRequest">
        /// The client's token request parameters, or <see langword="null"/> when the section configures none.
        /// </param>
        /// <returns>The handler to add to the client's pipeline.</returns>
        DelegatingHandler CreateUserTokenHandler(string clientName, TokenRequestOptions? tokenRequest);
    }
}
