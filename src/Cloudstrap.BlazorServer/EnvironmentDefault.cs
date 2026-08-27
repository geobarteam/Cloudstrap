namespace Cloudstrap.BlazorServer
{
    /// <summary>
    /// The one place the "explicit wins, unset follows the environment" rule is written.
    /// </summary>
    /// <remarks>
    /// One setting in this package is <see langword="bool"/>? for exactly this reason:
    /// <see cref="ExceptionHandlingSettings.UseDeveloperExceptionPage"/> — unset means <c>Development</c>
    /// only. Deliberately re-expressed package-locally rather than shared with <c>Cloudstrap.Mvc</c>: a
    /// cross-package reference for a one-line helper couples otherwise independent closures.
    /// </remarks>
    internal static class EnvironmentDefault
    {
        /// <summary>
        /// Resolves a three-state setting against the default its environment implies.
        /// </summary>
        /// <param name="explicitValue">The configured value, or <see langword="null"/> when unset.</param>
        /// <param name="environmentDefault">The value implied by the hosting environment.</param>
        /// <returns>
        /// <paramref name="explicitValue"/> when it is set; otherwise <paramref name="environmentDefault"/>.
        /// </returns>
        public static bool Resolve(bool? explicitValue, bool environmentDefault)
        {
            return explicitValue ?? environmentDefault;
        }
    }
}
