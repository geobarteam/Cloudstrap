namespace Cloudstrap.BlazorWasm
{
    /// <summary>
    /// Stores the current XSRF request token: populated by the BFF authentication state provider
    /// from the configured response header, consumed by <see cref="CookieHandler"/> on mutating
    /// requests. One singleton store serves every client registered through the package.
    /// </summary>
    /// <remarks>
    /// The store is a plain seam: tests and advanced consumers may pre-seed a token or inspect the
    /// captured one by resolving this interface.
    /// </remarks>
    public interface IAntiforgeryTokenStore
    {
        /// <summary>
        /// Gets or sets the current XSRF token, or <see langword="null"/> when none has been captured.
        /// </summary>
        string? Token
        {
            get; set;
        }
    }
}
