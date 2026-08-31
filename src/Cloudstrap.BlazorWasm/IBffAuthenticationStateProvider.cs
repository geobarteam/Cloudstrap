namespace Cloudstrap.BlazorWasm
{
    /// <summary>
    /// The refresh seam of the BFF authentication state: consumers call it after a login or logout
    /// navigation completes to drop the cached state and refetch it from the BFF.
    /// </summary>
    public interface IBffAuthenticationStateProvider
    {
        /// <summary>
        /// Drops the cached authentication state, notifies subscribers, and refetches the state
        /// from the BFF's user endpoint.
        /// </summary>
        void ClearAuthenticationState();
    }
}
