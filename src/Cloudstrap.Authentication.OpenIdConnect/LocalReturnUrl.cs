namespace Cloudstrap.Authentication.OpenIdConnect
{
    /// <summary>
    /// The one place the local-return-URL rule lives: a caller-supplied return URL is honored only when
    /// it can never leave the application.
    /// </summary>
    /// <remarks>
    /// Rejected shapes: absolute URLs (<c>https://evil.example.com/…</c>), protocol-relative URLs
    /// (<c>//evil.example.com</c>), backslash variants (<c>/\evil.example.com</c> — browsers normalize
    /// the backslash to a slash), and their percent-encoded forms (which arrive here already decoded
    /// once by query-string parsing and fall into one of the shapes above, or stay a harmless local
    /// path segment).
    /// </remarks>
    internal static class LocalReturnUrl
    {
        /// <summary>
        /// Returns the candidate when it is a local URL, and the fallback otherwise.
        /// </summary>
        /// <param name="candidate">The caller-supplied return URL.</param>
        /// <param name="fallback">The local URL used when the candidate is not local.</param>
        /// <returns>The URL to return the user to.</returns>
        public static string ResolveOrDefault(string? candidate, string fallback) =>
            IsLocal(candidate) ? candidate! : fallback;

        /// <summary>
        /// Decides whether a URL is local: exactly one leading slash, so it can only ever resolve
        /// within the application.
        /// </summary>
        /// <param name="url">The candidate URL.</param>
        /// <returns><see langword="true"/> when the URL is local.</returns>
        private static bool IsLocal(string? url) =>
            !string.IsNullOrEmpty(url)
            && url[0] == '/'
            && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
    }
}
